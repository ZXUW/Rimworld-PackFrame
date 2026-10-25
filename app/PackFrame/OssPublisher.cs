using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PackFrame;

public static class OssPublisher
{
    public static bool IsConfigured(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.OssEndpoint) &&
               !string.IsNullOrWhiteSpace(config.OssBucket) &&
               !string.IsNullOrWhiteSpace(config.OssAccessKeyId) &&
               !string.IsNullOrWhiteSpace(config.OssAccessKeySecret);
    }

    public static bool IsEnabled(AppConfig config) =>
        config.OssEnabled && IsConfigured(config);

    public static async Task<string> UploadAsync(
        AppConfig config,
        string filePath,
        string objectName,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsConfigured(config))
        {
            throw new InvalidOperationException("OSS is not configured.");
        }
        if (!config.OssEnabled)
        {
            throw new InvalidOperationException("OSS upload is disabled.");
        }
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The local mods archive was not found.", filePath);
        }

        var accessKeyId = config.OssAccessKeyId!.Trim();
        var accessKeySecret = config.OssAccessKeySecret!.Trim();
        var bucket = config.OssBucket!.Trim();
        var endpoint = NormalizeEndpoint(config.OssEndpoint!);
        var key = BuildObjectKey(config.OssPrefix, objectName);
        var resource = $"/{bucket}/{key}";
        var contentType = "application/zip";
        var date = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        var canonicalHeaders = string.Empty;
        var stringToSign =
            "PUT\n\n" + contentType + "\n" + date + "\n" + canonicalHeaders + resource;
        string signature;
        using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret)))
        {
            signature = Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))
            );
        }

        var url =
            $"https://{bucket}.{endpoint}/{EscapeKey(key)}";
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromHours(2)
        };
        await using var stream = File.OpenRead(filePath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = stream.Length;
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Date", date);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            "OSS " + accessKeyId + ":" + signature
        );

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OSS PUT returned {(int)response.StatusCode}: {body}"
            );
        }

        return url;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var value = endpoint.Trim().TrimEnd('/');
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = new Uri(value).Host;
        }
        return value;
    }

    private static string BuildObjectKey(string? prefix, string objectName)
    {
        var name = objectName.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return name;
        }

        return prefix.Trim().Trim('/') + "/" + name;
    }

    private static string EscapeKey(string key)
    {
        return string.Join(
            "/",
            key.Split('/').Select(segment => Uri.EscapeDataString(segment))
        );
    }
}
