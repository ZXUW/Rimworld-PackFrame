using PackFrame;
using SkiaSharp;

internal static class CoverChecks
{
    public static void Run(string root)
    {
        var folder = Path.Combine(root, "cover-check");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.json"), "{}");
        foreach (var (width, height) in new[] { (3840, 2160), (200, 100), (1200, 2400) })
        {
            var source = Path.Combine(folder, "source.png");
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.CornflowerBlue);
                using var paint = new SKPaint { Color = SKColors.Orange };
                for (var x = 0; x < width; x += 35) canvas.DrawCircle(x, height / 2, 90, paint);
            }
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(source)) data.SaveTo(file);
            var output = ModpackRepoExporter.SetCoverImage(folder, source)!;
            using var result = SKBitmap.Decode(output);
            var scale = Math.Min(1d, 960d / Math.Max(width, height));
            if (result.Width != (int)Math.Round(width * scale) || result.Height != (int)Math.Round(height * scale))
                throw new Exception("Cover dimensions incorrect");
            Console.WriteLine($"PASS: cover {width}x{height} -> {result.Width}x{result.Height}, {new FileInfo(output).Length:N0} bytes");
        }
        var bad = Path.Combine(folder, "bad.png");
        File.WriteAllText(bad, "invalid");
        var before = File.ReadAllBytes(Path.Combine(folder, "images", "cover.jpg"));
        try { ModpackRepoExporter.SetCoverImage(folder, bad); throw new Exception("Invalid image accepted"); }
        catch (InvalidDataException) { }
        if (!before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "images", "cover.jpg"))))
            throw new Exception("Invalid cover destroyed existing cover");
        Console.WriteLine("PASS: invalid cover preserves existing image");
    }
}
