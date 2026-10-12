using System.Runtime.InteropServices;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Windows.Native;

namespace Akeno.ColorVision.Windows;

/// <summary>A small square of screen pixels around a point.</summary>
public sealed record ScreenSample(int X, int Y, int Size, Rgb Center, Rgb Average, byte[] Bgra)
{
    /// <summary>All pixels are pure black: often a sign of protected (DRM) or inaccessible content.</summary>
    public bool LooksBlocked => Bgra.Length > 0 && Bgra.Where((_, i) => i % 4 != 3).All(b => b == 0);
}

/// <summary>
/// Reads pixels from the composed desktop with GDI <c>BitBlt</c> (physical pixel coordinates).
/// <para>
/// Whether these pixels are the colors <i>before</i> or <i>after</i> the system-wide Magnification
/// color effect is not documented by Microsoft. The app therefore measures it at runtime (see the
/// color identifier's self-check) instead of assuming. Protected content (e.g. some streaming video)
/// may read back as black.
/// </para>
/// </summary>
public static class ScreenSampler
{
    public static bool TryGetCursorPosition(out int x, out int y)
    {
        if (NativeMethods.GetCursorPos(out var p))
        {
            x = p.X;
            y = p.Y;
            return true;
        }

        x = y = 0;
        return false;
    }

    /// <summary>Samples a <paramref name="size"/>×<paramref name="size"/> square centred on (x, y). Size must be odd and 1..31.</summary>
    public static ScreenSample? TrySample(int x, int y, int size = 9)
    {
        size = Math.Clamp(size | 1, 1, 31);
        var half = size / 2;
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var old = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            bitmap = NativeMethods.CreateDIBSection(screenDc, ref header, NativeMethods.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            old = NativeMethods.SelectObject(memDc, bitmap);
            if (!NativeMethods.BitBlt(memDc, 0, 0, size, size, screenDc, x - half, y - half, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                return null;
            }

            var data = new byte[size * size * 4];
            Marshal.Copy(bits, data, 0, data.Length);

            Rgb Pixel(int px, int py)
            {
                var i = ((py * size) + px) * 4;
                return Rgb.FromBytes(data[i + 2], data[i + 1], data[i]);
            }

            var center = Pixel(half, half);

            // Average the central 3x3 block to reduce the influence of dithering and anti-aliasing.
            double r = 0, g = 0, b = 0;
            var n = 0;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var px = half + dx;
                    var py = half + dy;
                    if (px < 0 || py < 0 || px >= size || py >= size)
                    {
                        continue;
                    }

                    var c = Pixel(px, py);
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    n++;
                }
            }

            return new ScreenSample(x, y, size, center, new Rgb(r / n, g / n, b / n), data);
        }
        finally
        {
            if (old != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDc, old);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Decides whether sampling returns original or filtered colors by comparing a sample of a
    /// patch with a known color against the known color and against the filtered known color.
    /// </summary>
    public static SamplingVerdict ClassifySampling(Rgb known, Rgb sampled, ColorMatrix activeFilter)
    {
        ArgumentNullException.ThrowIfNull(activeFilter);
        var filtered = activeFilter.TransformClamped(known);
        if (known.MaxDifference(filtered) < 12 / 255.0)
        {
            return SamplingVerdict.Inconclusive; // filter barely changes this color
        }

        var toOriginal = sampled.MaxDifference(known);
        var toFiltered = sampled.MaxDifference(filtered);
        const double tolerance = 6 / 255.0;
        if (toOriginal <= tolerance && toFiltered > tolerance)
        {
            return SamplingVerdict.ReadsOriginal;
        }

        if (toFiltered <= tolerance && toOriginal > tolerance)
        {
            return SamplingVerdict.ReadsFiltered;
        }

        return SamplingVerdict.Inconclusive;
    }
}

public enum SamplingVerdict
{
    Inconclusive,
    ReadsOriginal,
    ReadsFiltered,
}
