using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.App.Imaging;

/// <summary>
/// CPU implementation of exactly the same affine matrix + clamping the system filter performs.
/// Used for side-by-side previews inside the app (the real filter would change the whole screen,
/// including the "original" side).
/// </summary>
public static class ImageTransform
{
    public static BitmapSource Apply(BitmapSource source, ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.IsIdentity())
        {
            return source;
        }

        BitmapSource src = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = src.PixelWidth;
        var height = src.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        src.CopyPixels(pixels, stride, 0);

        // lut[(input * 3 + output) * 256 + value] = contribution of an 8-bit input value to an output channel (0..255 scale).
        var lut = new float[9 * 256];
        for (var input = 0; input < 3; input++)
        {
            for (var output = 0; output < 3; output++)
            {
                var coefficient = (float)matrix[output, input];
                var offset = ((input * 3) + output) * 256;
                for (var v = 0; v < 256; v++)
                {
                    lut[offset + v] = coefficient * v;
                }
            }
        }

        var off0 = (float)(matrix.OffsetOf(0) * 255);
        var off1 = (float)(matrix.OffsetOf(1) * 255);
        var off2 = (float)(matrix.OffsetOf(2) * 255);

        Parallel.For(0, height, y =>
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + (x * 4);
                int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                var outR = lut[(0 * 3 + 0) * 256 + r] + lut[(1 * 3 + 0) * 256 + g] + lut[(2 * 3 + 0) * 256 + b] + off0;
                var outG = lut[(0 * 3 + 1) * 256 + r] + lut[(1 * 3 + 1) * 256 + g] + lut[(2 * 3 + 1) * 256 + b] + off1;
                var outB = lut[(0 * 3 + 2) * 256 + r] + lut[(1 * 3 + 2) * 256 + g] + lut[(2 * 3 + 2) * 256 + b] + off2;
                pixels[i + 2] = ToByte(outR);
                pixels[i + 1] = ToByte(outG);
                pixels[i] = ToByte(outB);
            }
        });

        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    /// <summary>Loads a user-chosen image (PNG/JPEG/BMP) scaled down for preview. Stays on this PC.</summary>
    public static BitmapSource LoadForPreview(string path, int maxWidth = 1280)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        if (frame.PixelWidth > maxWidth)
        {
            var scale = (double)maxWidth / frame.PixelWidth;
            frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var copy = new WriteableBitmap(converted);
        copy.Freeze();
        return copy;
    }

    private static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);
}

/// <summary>WPF color helpers.</summary>
public static class ColorConversions
{
    public static Color ToColor(this Rgb rgb)
    {
        var (r, g, b) = rgb.ToBytes();
        return Color.FromRgb(r, g, b);
    }

    public static SolidColorBrush ToBrush(this Rgb rgb)
    {
        var brush = new SolidColorBrush(rgb.ToColor());
        brush.Freeze();
        return brush;
    }

    public static Rgb ToRgb(this Color color) => Rgb.FromBytes(color.R, color.G, color.B);
}
