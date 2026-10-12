using System.Runtime.InteropServices;

namespace Akeno.ColorVision.Rendering.Magnification;

/// <summary>
/// <c>MAGCOLOREFFECT</c> from magnification.h: <c>typedef struct tagMAGCOLOREFFECT { float transform[5][5]; }</c>.
/// 25 consecutive 32-bit floats (100 bytes), row-major, row-vector convention
/// (<c>[R G B A 1] · T</c>). Blittable, so it is passed to native code by pointer without copying.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MAGCOLOREFFECT
{
    public const int Size = 25;

    public fixed float transform[Size];

    public static MAGCOLOREFFECT FromArray(ReadOnlySpan<float> values)
    {
        if (values.Length != Size)
        {
            throw new ArgumentException("MAGCOLOREFFECT needs exactly 25 values.", nameof(values));
        }

        var effect = default(MAGCOLOREFFECT);
        for (var i = 0; i < Size; i++)
        {
            effect.transform[i] = values[i];
        }

        return effect;
    }

    public readonly float[] ToArray()
    {
        var result = new float[Size];
        for (var i = 0; i < Size; i++)
        {
            result[i] = transform[i];
        }

        return result;
    }
}

/// <summary>
/// P/Invoke declarations for Magnification.dll (Windows 8+, x64/ARM64 processes only —
/// Microsoft documents that the Magnification API is not supported under WOW64).
/// </summary>
internal static class MagnificationNative
{
    public const string Library = "Magnification.dll";

    /// <summary>Creates and initializes the magnifier run-time objects.</summary>
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagInitialize();

    /// <summary>Destroys the magnifier run-time objects.</summary>
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagUninitialize();

    /// <summary>Changes the color transformation matrix associated with the full-screen magnifier. Must not be NULL.</summary>
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

    /// <summary>Retrieves the color transformation matrix associated with the full-screen magnifier.</summary>
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MagGetFullscreenColorEffect(out MAGCOLOREFFECT pEffect);

    /// <summary>True if Magnification.dll and the full-screen color effect entry points exist on this system.</summary>
    public static bool IsAvailable(out string detail)
    {
        if (!NativeLibrary.TryLoad(Library, typeof(MagnificationNative).Assembly, DllImportSearchPath.System32, out var handle))
        {
            detail = "Magnification.dll could not be loaded.";
            return false;
        }

        try
        {
            foreach (var export in new[] { "MagInitialize", "MagUninitialize", "MagSetFullscreenColorEffect", "MagGetFullscreenColorEffect" })
            {
                if (!NativeLibrary.TryGetExport(handle, export, out _))
                {
                    detail = $"Magnification.dll does not export {export} (Windows 8 or later is required).";
                    return false;
                }
            }

            detail = "Magnification.dll with full-screen color effect support found.";
            return true;
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
