using System.Globalization;
using PackFrame;

internal static class SteamRootChecks
{
    public static async Task Run(string root)
    {
        var home = Path.Combine(root, "steam-user");
        var dataHome = Path.Combine(root, "custom-data");
        var native = Path.Combine(home, ".local", "share", "Steam");
        var flatpak = Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        var linux = RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, home, null);
        Require(linux.ToHashSet().SetEquals(new[]
        {
            native, Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".steam", "debian-installation"), flatpak
        }), "Linux discovers native, Steam symlink and Flatpak client roots without requiring the game in the client library");

        var custom = RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, home, dataHome);
        Require(custom.Contains(Path.Combine(dataHome, "Steam")) && linux.All(custom.Contains),
            "absolute XDG_DATA_HOME adds the custom Linux client root and retains fallback locations");
        Require(RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, home, "relative-data")
                .SequenceEqual(linux),
            "relative XDG_DATA_HOME is ignored instead of probing the working directory");
        Require(RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, home,
                Path.Combine(home, ".local", "share")).Count == linux.Count,
            "default XDG_DATA_HOME does not duplicate the native client root");

        var mac = RimWorldLocator.GetSteamRoots(Array.Empty<string>(), false, true, home, dataHome);
        Require(mac.SequenceEqual(new[] { Path.Combine(home, "Library", "Application Support", "Steam") }),
            "macOS discovers the Steam client Application Support directory and ignores Linux environment paths");
        var windows = Path.Combine(root, "Program Files", "Steam");
        Require(RimWorldLocator.GetSteamRoots(new[] { windows, windows.ToUpperInvariant() },
                false, false, home, dataHome).SequenceEqual(new[] { windows }),
            "Windows roots remain case-insensitively deduplicated without Unix additions");
        Require(RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, null, null).Count == 0 &&
                RimWorldLocator.GetSteamRoots(Array.Empty<string>(), false, true, " ", null).Count == 0,
            "missing home directories do not become relative Steam roots");
        Require(RimWorldLocator.GetSteamRoots(Array.Empty<string>(), true, false, null, dataHome)
                .SequenceEqual(new[] { Path.Combine(dataHome, "Steam") }),
            "absolute XDG_DATA_HOME remains usable when the home directory is unavailable");
        Require(RimWorldLocator.GetSteamRoots(new[] { "\0", " ", native, native.ToUpperInvariant() },
                true, false, null, null).Count == 2,
            "invalid roots are skipped and Linux paths retain case-sensitive distinctions");

        foreach (var platform in new[] { "Linux", "macOS" })
        {
            var fixtureRoot = Path.Combine(root, "secondary-library-" + platform);
            var profile = Path.Combine(fixtureRoot, "home");
            var client = platform == "Linux"
                ? Path.Combine(profile, ".local", "share", "Steam")
                : Path.Combine(profile, "Library", "Application Support", "Steam");
            var library = Path.Combine(fixtureRoot, "game-library");
            var game = Path.Combine(library, "steamapps", "common", "RimWorld");
            var workshop = Path.Combine(library, "steamapps", "workshop");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(Path.Combine(workshop, "downloads", "294100", "11"));
            Directory.CreateDirectory(Path.Combine(client, "steamapps"));
            Directory.CreateDirectory(Path.Combine(client, "logs"));
            File.WriteAllText(Path.Combine(workshop, "appworkshop_294100.acf"), """
                "AppWorkshop" {
                    "appid" "294100" "NeedsDownload" "1"
                    "WorkshopItemsInstalled" { }
                    "WorkshopItemDetails" { "11" { "manifest" "101" "latest_manifest" "101" "subscribedby" "123" } }
                }
                """);
            var timestamp = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.WriteAllLines(Path.Combine(client, "logs", "content_log.txt"), new[]
            {
                $"[{timestamp}] AppID 294100 Workshop update changed : Running Update,Downloading,Staging,",
                $"[{timestamp}] Downloading 20 chunks for depot 294100 (101)"
            });
            var steamRoots = RimWorldLocator.GetSteamRoots(Array.Empty<string>(), platform == "Linux",
                platform == "macOS", profile, null);
            var snapshot = await SteamWorkshopLocalStateReader.QueryFromSteamRootsAsync(game, steamRoots,
                DateTime.UtcNow.AddMinutes(-5), new ulong[] { 11 });
            Require(snapshot is not null && snapshot.Items.TryGetValue(11, out var item) && item.IsDownloading,
                platform + " secondary-library game uses transfer logs from the discovered client root");

            var otherLibrary = Path.Combine(fixtureRoot, "workshop-library");
            Directory.CreateDirectory(Path.Combine(otherLibrary, "steamapps"));
            Directory.Move(workshop, Path.Combine(otherLibrary, "steamapps", "workshop"));
            File.WriteAllText(Path.Combine(client, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\" { \"1\" { \"path\" \"" + otherLibrary.Replace("\\", "\\\\") + "\" } }");
            snapshot = await SteamWorkshopLocalStateReader.QueryFromSteamRootsAsync(game, steamRoots,
                DateTime.UtcNow.AddMinutes(-5), new ulong[] { 11 });
            Require(snapshot is not null && snapshot.Items.TryGetValue(11, out item) && item.IsDownloading,
                platform + " client libraryfolders.vdf discovers Workshop data outside the game library");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
