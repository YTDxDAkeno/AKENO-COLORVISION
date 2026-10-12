using System.Runtime.InteropServices;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Rendering.Magnification;
using Akeno.ColorVision.Windows;
using Xunit.Abstractions;

namespace Akeno.ColorVision.IntegrationTests;

/// <summary>
/// Shows a window with a known solid color, applies a red↔blue swap through the real Magnification
/// backend, and samples the window with GDI BitBlt before, during and after.
/// <para>
/// This answers, on a real Windows session, the question Microsoft's documentation leaves open:
/// do screen reads return the original or the filtered pixels? If they return filtered pixels, it is
/// also direct evidence that Windows applied our matrix to the composed desktop image.
/// The outcome is written to the test output; the test fails only if the observation is inconsistent.
/// </para>
/// </summary>
[Collection("Magnification")]
public class ComposedOutputTests(ITestOutputHelper output)
{
    private static readonly Rgb Known = Rgb.FromHex("#C83C32");
    private static readonly ColorMatrix Swap = ColorMatrix.FromRowMajor([0, 0, 1, 0, 1, 0, 1, 0, 0]);

    [SkippableFact]
    public void ObserveWhetherScreenReadsSeeTheColorEffect()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("test-window");
        using var window = SolidColorWindow.Create(pump, 220, 220, 200, 200, Known);
        Thread.Sleep(500);

        var before = ScreenSampler.TrySample(320, 320, 5);
        Skip.If(before is null, "No interactive desktop for screen sampling.");
        Skip.If(before!.Center.MaxDifference(Known) > 8 / 255.0, $"Test window not visible on screen (read {before.Center.ToHex()}).");

        using var backend = new MagnificationColorFilterBackend { SmoothTransitions = false };
        var init = backend.Initialize();
        Skip.IfNot(init.Success, $"Magnification API not usable: {init}");
        ScreenSample? during;
        ColorMatrix? active;
        try
        {
            Assert.True(backend.ApplyTransformation(Swap).Success);
            Thread.Sleep(700);
            active = backend.ReadCurrentTransformation();
            during = ScreenSampler.TrySample(320, 320, 5);
        }
        finally
        {
            backend.ForceIdentity();
            backend.Restore();
        }

        Thread.Sleep(500);
        var after = ScreenSampler.TrySample(320, 320, 5);
        Assert.NotNull(during);
        Assert.NotNull(after);
        Assert.True(active!.ApproximatelyEquals(Swap, 1e-5), "Windows reports the swap matrix as active.");

        var verdict = ScreenSampler.ClassifySampling(Known, during!.Center, Swap);
        output.WriteLine($"Known color:            {Known.ToHex()}");
        output.WriteLine($"Read before effect:     {before.Center.ToHex()}");
        output.WriteLine($"Read with swap effect:  {during.Center.ToHex()} (filtered would be {Swap.TransformClamped(Known).ToHex()})");
        output.WriteLine($"Read after reset:       {after!.Center.ToHex()}");
        output.WriteLine($"VERDICT: screen reads return {verdict switch
        {
            SamplingVerdict.ReadsFiltered => "FILTERED pixels — Windows applied the matrix to the composed desktop image",
            SamplingVerdict.ReadsOriginal => "ORIGINAL pixels (captured before the color effect)",
            _ => "an inconclusive value",
        }}.");

        Assert.True(after.Center.MaxDifference(Known) <= 8 / 255.0, "After reset the window must read back as the original color again.");
        Assert.NotEqual(SamplingVerdict.Inconclusive, verdict);
    }

    /// <summary>A minimal Win32 popup window filled with one color (no UI framework needed).</summary>
    private sealed class SolidColorWindow : IDisposable
    {
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly WndProc Proc = DefWindowProc;
        private static readonly string ClassName = "AkenoTestSolid" + Environment.ProcessId;
        private static bool _registered;

        private readonly MessagePumpThread _pump;
        private readonly IntPtr _hwnd;
        private readonly IntPtr _brush;

        private SolidColorWindow(MessagePumpThread pump, IntPtr hwnd, IntPtr brush)
        {
            _pump = pump;
            _hwnd = hwnd;
            _brush = brush;
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static SolidColorWindow Create(MessagePumpThread pump, int x, int y, int w, int h, Rgb color) =>
            pump.Invoke(() =>
            {
                var (r, g, b) = color.ToBytes();
                var brush = CreateSolidBrush((uint)(r | (g << 8) | (b << 16)));
                var instance = GetModuleHandle(null);
                if (!_registered)
                {
                    var wc = new WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                        hInstance = instance,
                        hbrBackground = brush,
                        lpszClassName = ClassName,
                    };
                    RegisterClassEx(ref wc);
                    _registered = true;
                }

                var hwnd = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "Akeno test", WS_POPUP | WS_VISIBLE,
                    x, y, w, h, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                // Paint the client area with the brush (the class has no background brush of its own).
                var dc = GetDC(hwnd);
                var rect = new RECT { Right = w, Bottom = h };
                FillRect(dc, ref rect, brush);
                ReleaseDC(hwnd, dc);
                return new SolidColorWindow(pump, hwnd, brush);
            }, TimeSpan.FromSeconds(5));

        public void Dispose() => _pump.Invoke(() =>
        {
            DestroyWindow(_hwnd);
            DeleteObject(_brush);
        }, TimeSpan.FromSeconds(5));

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);
    }
}
