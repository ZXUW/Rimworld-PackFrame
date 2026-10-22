using System;
using System.IO;
using SkiaSharp;

namespace PackFrame;

internal static class CoverImageEncoder
{
    public const int MaximumDimension = 960;
    public static byte[] Encode(string source)
    {
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidDataException("Invalid cover image: " + source);
        var scale = Math.Min(1d, MaximumDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var target = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint())
        using (var sourceImage = SKImage.FromBitmap(bitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x35, 0x4A));
            canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82)
            ?? throw new IOException("Could not encode cover image.");
        return data.ToArray();
    }
}
