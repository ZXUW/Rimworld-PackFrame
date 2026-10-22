using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PackFrame;

internal static class PackLicenses
{
    private const string MitText = """
        MIT License

        Copyright (c) 2026 ZXUW

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    public static void Ensure(string repoPath, IReadOnlyList<string> localMods)
    {
        Directory.CreateDirectory(repoPath);
        var license = Path.Combine(repoPath, "LICENSE");
        if (!File.Exists(license))
        {
            File.WriteAllText(license, ReadCcBy(), new UTF8Encoding(false));
        }

        var licenses = Path.Combine(repoPath, "LICENSES");
        Directory.CreateDirectory(licenses);
        var mit = Path.Combine(licenses, "ZXUW-MIT.txt");
        if (!File.Exists(mit))
        {
            File.WriteAllText(mit, MitText + Environment.NewLine, new UTF8Encoding(false));
        }

        var notices = Path.Combine(repoPath, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(notices))
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Third-party notices");
            builder.AppendLine();
            builder.AppendLine("## Local mods");
            builder.AppendLine();
            if (localMods.Count == 0)
            {
                builder.AppendLine("This pack does not include local mods.");
            }
            else
            {
                builder.AppendLine("The following local mods are included in `mods.zip`:");
                foreach (var name in localMods.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine();
                    builder.AppendLine("- " + name);
                    builder.AppendLine("  - Copyright: ZXUW, 2026");
                    builder.AppendLine("  - License: MIT (see `LICENSES/ZXUW-MIT.txt`)");
                }
            }
            builder.AppendLine();
            builder.AppendLine("## Steam Workshop mods");
            builder.AppendLine();
            builder.AppendLine("This pack references Steam Workshop mods by ID. Their files are");
            builder.AppendLine("not redistributed in this repository and remain under the licenses");
            builder.AppendLine("chosen by their respective authors.");
            builder.AppendLine();
            builder.AppendLine("## Pack data");
            builder.AppendLine();
            builder.AppendLine("The pack manifest, configuration files, README and images are");
            builder.AppendLine("licensed under CC BY 4.0 (see `LICENSE`).");
            File.WriteAllText(notices, builder.ToString(), new UTF8Encoding(false));
        }

        var readme = Path.Combine(repoPath, "README.md");
        var section = """

            ## License

            - Pack manifest, configuration, README and images: CC BY 4.0
            - Local mods included in `mods.zip`: MIT
            - Steam Workshop mods: original licenses; see `THIRD-PARTY-NOTICES.md`
            """;
        var text = File.Exists(readme) ? File.ReadAllText(readme) : string.Empty;
        if (!text.Contains("## License", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(
                readme,
                text.TrimEnd() + Environment.NewLine + section + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }
    }

    private static string ReadCcBy()
    {
        using var stream = typeof(PackLicenses).Assembly.GetManifestResourceStream(
            "PackFrame.Assets.CC-BY-4.0.txt"
        );
        if (stream is null)
        {
            throw new InvalidOperationException("CC BY 4.0 resource is missing.");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
