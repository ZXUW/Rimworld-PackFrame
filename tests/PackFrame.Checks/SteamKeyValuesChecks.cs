using System.Text;
using PackFrame;

internal static class SteamKeyValuesChecks
{
    public static void Run()
    {
        const string acf = """
            // Steam may rewrite this file while a download is active.
            "AppWorkshop"
            {
                "appid" "294100"
                "SizeOnDisk" "4096"
                "WorkshopItemsInstalled"
                {
                    "3748351639" { "manifest" "9876543210123456789" "size" "1024" }
                    "1234567890" { "manifest" "1234567890123456789" "size" "3072" }
                }
                "WorkshopItemDetails"
                {
                    "3748351639" { "manifest" "9876543210123456789" "timetouched" "1700000000" }
                }
            }
            // A final comment does not require a newline.
            """;
        Require(SteamKeyValues.TryParse(acf, out var root), "realistic Workshop ACF parses");
        var workshop = root.Child("appworkshop");
        var installed = workshop?.Child("workshopitemsinstalled");
        Require(workshop?.Value("APPID") == "294100" && installed?.Children.Count == 2 &&
                installed.Child("3748351639")?.Value("manifest") == "9876543210123456789" &&
                installed.Child("1234567890")?.Value("size") == "3072" &&
                workshop.Child("WorkshopItemDetails")?.Child("3748351639")?.Value("timetouched") == "1700000000",
            "nested item IDs, large manifest strings and case-insensitive lookups remain distinct");
        Require(root.Value("missing") is null && root.Child("missing") is null && root.Children.Count == 1 &&
                workshop!.Child("appid") is null && workshop.Value("WorkshopItemsInstalled") is null,
            "scalar values and child objects are exposed separately");

        const string strings = """
            libraryfolders {
                0 { "path" "C:\\Program Files (x86)\\Steam" }
                1 { path "D:\SteamLibrary" }
                "quote" "say \"hi\""
                "unknown" "keep\q\n\t"
                "url" "https://example.com/mod//literal"
                empty ""
            }
            """;
        Require(SteamKeyValues.TryParse("\uFEFF" + strings, out var libraries),
            "UTF-8 BOM, bare tokens, empty strings and normal Steam string escapes parse");
        var folders = libraries.Child("libraryfolders")!;
        Require(folders.Child("0")?.Value("path") == @"C:\Program Files (x86)\Steam" &&
                folders.Child("1")?.Value("path") == @"D:\SteamLibrary" &&
                folders.Value("quote") == "say \"hi\"" && folders.Value("unknown") == @"keep\q\n\t" &&
                folders.Value("url") == "https://example.com/mod//literal" && folders.Value("empty") == "",
            "quoted comments stay literal and unknown escapes preserve Windows path backslashes");
        Require(SteamKeyValues.TryParse("a b // line\r\nc d // eof", out var comments) &&
                comments.Value("a") == "b" && comments.Value("c") == "d",
            "multiple complete root pairs and comments at end of input are valid");

        Require(SteamKeyValues.TryParse("Size 100 size 100 Item { a b } ITEM { A b }", out var duplicates) &&
                duplicates.Value("SIZE") == "100" && duplicates.Children.Count == 1,
            "identical duplicate scalar or object keys are harmless regardless of key casing");
        foreach (var invalid in new[]
                 {
                     "", " \t\r\n", "// comment only", "}", "{ a b }", "a", "a {", "a { b c",
                     "a { b }", "a b }", "a {} trailing", "a {} }", "a \"unterminated", "\"a b",
                     "a \"unterminated\\", "a b\0", "a \"b\0c\"", "a 1 A 2", "a 1 A {}",
                     "a {} A 1", "a { x 1 } A { X 2 }", "a { x 1 } A { y 1 }"
                 })
            Require(!SteamKeyValues.TryParse(invalid, out var rejected) && rejected.Children.Count == 0 &&
                    rejected.Value("a") is null,
                "malformed, truncated, trailing or conflicting KeyValues never expose a partial tree");
        Require(!SteamKeyValues.TryParse(null!, out _), "null input is rejected");

        var depth32 = string.Concat(Enumerable.Repeat("node { ", 32)) + "leaf value " + new string('}', 32);
        var depth33 = "node { " + depth32 + " }";
        Require(SteamKeyValues.TryParse(depth32, out _) && !SteamKeyValues.TryParse(depth33, out _),
            "32 nested objects are accepted and deeper documents are rejected");

        var entries = new StringBuilder();
        for (var index = 0; index < 50000; index++) entries.Append('k').Append(index).Append(" v\n");
        Require(SteamKeyValues.TryParse(entries.ToString(), out _) &&
                !SteamKeyValues.TryParse(entries.Append("overflow v").ToString(), out _),
            "entry limit is enforced at 50,000 key/value or object entries");
        var repeated = string.Concat(Enumerable.Repeat("same value\n", 50000));
        Require(SteamKeyValues.TryParse(repeated, out _) &&
                !SteamKeyValues.TryParse(repeated + "same value", out _),
            "duplicate entries also count toward the work limit");

        const int limit = 8 * 1024 * 1024;
        var atLimit = "k v" + new string(' ', limit - 3);
        var multibyteOverLimit = "k \"" + new string('\u00E9', limit / 2) + "\"";
        Require(SteamKeyValues.TryParse(atLimit, out _) && !SteamKeyValues.TryParse(atLimit + " ", out _) &&
                !SteamKeyValues.TryParse(multibyteOverLimit, out _),
            "input size is bounded to 8 MiB of UTF-8, including multibyte text");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
