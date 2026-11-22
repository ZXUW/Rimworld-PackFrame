using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// 抓取 RimWorld 工坊全量条目，只保留 1.5 及以后版本的模组。
// 用法: dotnet run --project tools/WorkshopSync -- <输出目录>

const int AppId = 294100;
const int PerPage = 30;
const int PageLimit = 1000;
string[] Sorts = { "totaluniquesubscribers", "mostrecent", "lastupdated", "toprated" };
var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.5", "1.6", "1.7" };

var outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var reuseIds = args.Contains("--reuse-ids");
var withDescriptions = args.Contains("--with-descriptions");
Directory.CreateDirectory(outDir);
var catalogPath = Path.Combine(outDir, "workshop-catalog.json");
var tsvPath = Path.Combine(outDir, "workshop-catalog.tsv");
var idsPath = Path.Combine(outDir, "workshop-ids.txt");
var jsonlPath = Path.Combine(outDir, "workshop-mods.jsonl");

using var http = new HttpClient(new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    ConnectTimeout = TimeSpan.FromSeconds(15)
})
{
    Timeout = TimeSpan.FromSeconds(60)
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (PackFrame WorkshopSync)");

Console.WriteLine("== 1/2 浏览页枚举 id ==");
var ids = new List<string>();
if (reuseIds && File.Exists(idsPath))
{
    ids.AddRange(File.ReadAllLines(idsPath).Where(line => line.Length > 0));
    Console.WriteLine($"  复用 {idsPath}，{ids.Count} 个 id");
}
else
{
    // 工坊深分页最多到第 1000 页（约 3 万条），所以按版本标签切片；
    // 单个标签仍超过上限时，再换几种排序把缺的补回来。
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var tag in new[] { "1.5", "1.6", "1.7" })
    {
        var firstHtml = await GetWithRetry(http, BrowseUrl(tag, Sorts[0], 1));
        var tagTotal = ExtractTotalCount(firstHtml);
        var tagPages = tagTotal > 0 ? (tagTotal + PerPage - 1) / PerPage : PageLimit;
        var sorts = tagPages > PageLimit ? Sorts : new[] { Sorts[0] };
        Console.WriteLine($"  [{tag}] {tagTotal:N0} 条，约 {tagPages} 页，使用 {sorts.Length} 种排序");

        foreach (var sort in sorts)
        {
            var before = ids.Count;
            var stale = 0;
            for (var page = 1; page <= PageLimit; page++)
            {
                string html;
                try
                {
                    html = page == 1 && sort == Sorts[0]
                        ? firstHtml
                        : await GetWithRetry(http, BrowseUrl(tag, sort, page));
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"    {tag}/{sort} 第 {page} 页失败: {exception.Message}");
                    break;
                }

                var pageIds = Regex.Matches(html, @"sharedfiles/filedetails/\?id=(\d+)")
                    .Select(match => match.Groups[1].Value)
                    .Where(seen.Add)
                    .ToList();
                ids.AddRange(pageIds);

                // 换排序补漏时，连续多页没有新 id 就说明剩下的都见过了
                stale = pageIds.Count == 0 ? stale + 1 : 0;
                if (stale >= 30) break;
                if (pageIds.Count == 0 && seen.Count == 0) break;
                if (page % 100 == 0) Console.WriteLine($"    {tag}/{sort} 第 {page} 页，累计 {ids.Count}");
                await Task.Delay(120);
            }
            Console.WriteLine($"    {tag}/{sort} 新增 {ids.Count - before}，累计 {ids.Count}（上限 {PageLimit} 页）");
        }
    }

    File.WriteAllLines(idsPath, ids, new UTF8Encoding(false));
    Console.WriteLine($"  枚举完成: {ids.Count} 个唯一 id（已写入 {idsPath}）");
}

static string BrowseUrl(string tag, string sort, int page) =>
    $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort={sort}&section=readytouseitems&p={page}&requiredtags%5B%5D={tag}";

Console.WriteLine("== 2/2 批量取详情 ==");
var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
var batch = 100;
for (var offset = 0; offset < ids.Count; offset += batch)
{
    var slice = ids.Skip(offset).Take(batch).ToList();
    var form = new List<KeyValuePair<string, string>> { new("itemcount", slice.Count.ToString(CultureInfo.InvariantCulture)) };
    for (var i = 0; i < slice.Count; i++) form.Add(new($"publishedfileids[{i}]", slice[i]));

    string json;
    try
    {
        json = await PostWithRetry(http, "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", form);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"  批次 {offset} 失败: {exception.Message}");
        continue;
    }

    using var document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty("response", out var response) ||
        !response.TryGetProperty("publishedfiledetails", out var details) ||
        details.ValueKind != JsonValueKind.Array) continue;

    foreach (var detail in details.EnumerateArray())
    {
        if (!detail.TryGetProperty("publishedfileid", out var idNode) ||
            !detail.TryGetProperty("title", out var titleNode)) continue;
        var id = idNode.GetString();
        if (string.IsNullOrWhiteSpace(id)) continue;

        var tags = new List<string>();
        if (detail.TryGetProperty("tags", out var tagNodes) && tagNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagNodes.EnumerateArray())
            {
                var value = tag.TryGetProperty("tag", out var tagValue) ? tagValue.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value)) tags.Add(value!);
            }
        }

        var name = (titleNode.GetString() ?? string.Empty).Trim();
        var updated = detail.TryGetProperty("time_updated", out var updatedNode) ? updatedNode.GetInt64() : 0;
        var description = withDescriptions && detail.TryGetProperty("description", out var descNode)
            ? (descNode.GetString() ?? string.Empty).Trim()
            : string.Empty;
        entries[id] = new CatalogEntry(name, tags, updated, description);
    }

    if ((offset / batch) % 20 == 0)
        Console.WriteLine($"  详情 {offset + slice.Count}/{ids.Count}，已解析 {entries.Count}");
    await Task.Delay(120);
}

var kept = entries
    .Where(pair => pair.Value.Tags.Any(tag => versions.Contains(tag)))
    .OrderBy(pair => pair.Value.Name, StringComparer.CurrentCultureIgnoreCase)
    .ToList();

Console.WriteLine($"  详情条目 {entries.Count}，1.5+ 保留 {kept.Count}");

var catalog = new CatalogDocument
{
    V = 1,
    Generated = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
    Total = kept.Count,
    Mods = kept.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
};
File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}), new UTF8Encoding(false));

var tsv = new StringBuilder("workshopId\tname\ttags\tcategory\n");
foreach (var (id, entry) in kept)
{
    tsv.Append(id).Append('\t')
        .Append(entry.Name.Replace('\t', ' ')).Append('\t')
        .Append(string.Join(',', entry.Tags)).Append('\t')
        .Append('\n');
}
File.WriteAllText(tsvPath, tsv.ToString(), new UTF8Encoding(true));

Console.WriteLine($"写出 {catalogPath}");
Console.WriteLine($"写出 {tsvPath}");

if (withDescriptions)
{
    // 一行一条，直接喂给 AI 打标；description 保留完整原文
    var lines = new List<string>(kept.Count);
    var options = new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    foreach (var (id, entry) in kept)
    {
        lines.Add(JsonSerializer.Serialize(new
        {
            id,
            name = entry.Name,
            tags = entry.Tags,
            description = entry.Description
        }, options));
    }
    File.WriteAllLines(jsonlPath, lines, new UTF8Encoding(false));
    Console.WriteLine($"写出 {jsonlPath}");
}

static async Task<string> GetWithRetry(HttpClient http, string url)
{
    return await SendWithRetry(() => http.GetAsync(url));
}

static async Task<string> PostWithRetry(HttpClient http, string url, IEnumerable<KeyValuePair<string, string>> form)
{
    return await SendWithRetry(() => http.PostAsync(url, new FormUrlEncodedContent(form)));
}

static async Task<string> SendWithRetry(Func<Task<HttpResponseMessage>> send)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var response = await send();
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode is 429 or 500 or 502 or 503 or 504 && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                continue;
            }
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }
        catch (Exception) when (attempt < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
        }
    }
}

static int ExtractTotalCount(string html)
{
    var text = Regex.Match(html, @"([\d,]+)\s+entries matching filters", RegexOptions.IgnoreCase);
    if (text.Success && int.TryParse(text.Groups[1].Value.Replace(",", ""), out var byText) && byText > 0)
        return byText;

    var json = Regex.Match(html, "\"total_count\"\\s*:\\s*(\\d+)");
    return json.Success && int.TryParse(json.Groups[1].Value, out var byJson) ? byJson : 0;
}

internal sealed class CatalogDocument
{
    public int V { get; set; }
    public string Generated { get; set; } = string.Empty;
    public int Total { get; set; }
    public Dictionary<string, CatalogEntry> Mods { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class CatalogEntry
{
    public CatalogEntry()
    {
    }

    public CatalogEntry(string name, List<string> tags, long updated, string description)
    {
        Name = name;
        Tags = tags;
        Updated = updated;
        Description = description;
    }

    public string Name { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public long Updated { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description { get; set; } = string.Empty;
}
