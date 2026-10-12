using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.IntegrationTests;

public class MessagePumpThreadTests
{
    [SkippableFact]
    public void InvokeRunsOnPumpThreadAndReturnsValue()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("test");
        var callerThread = Environment.CurrentManagedThreadId;
        var pumpThread = pump.Invoke(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(2));
        Assert.NotEqual(callerThread, pumpThread);
        Assert.Equal(42, pump.Invoke(() => 42, TimeSpan.FromSeconds(2)));
    }

    [SkippableFact]
    public void ExceptionsPropagateToCaller()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("test");
        Assert.Throws<InvalidOperationException>(() => pump.Invoke<int>(() => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(2)));
        Assert.Equal(1, pump.Invoke(() => 1, TimeSpan.FromSeconds(2))); // still alive
    }

    [SkippableFact]
    public void WaitHandleCallbacksRunOnPump()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("test");
        using var signal = new AutoResetEvent(false);
        using var done = new ManualResetEventSlim(false);
        pump.AddWaitHandle(signal, done.Set);
        Thread.Sleep(50);
        signal.Set();
        Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
    }
}

public class GlobalHotkeyTests
{
    [SkippableFact]
    public void RegistersAndDetectsConflicts()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("hotkeys-test");
        using var manager = new GlobalHotkeyManager(pump);
        HotkeyGesture.TryParse("Ctrl + Alt + Shift + F24", out var gesture, out _);
        var first = manager.Register(900, gesture!);
        Skip.IfNot(first.Success, $"Hotkeys not available in this session: {first.Message}");

        // The same combination under a different id must be reported as a conflict.
        var second = manager.Register(901, gesture!);
        Assert.False(second.Success);
        Assert.Contains("already used", second.Message, StringComparison.OrdinalIgnoreCase);

        manager.Unregister(900);
        var third = manager.Register(902, gesture!);
        Assert.True(third.Success, third.Message);
        manager.Unregister(902);
    }

    [SkippableFact]
    public void RejectsUnsafeGestureWithoutCallingWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var pump = new MessagePumpThread("hotkeys-test");
        using var manager = new GlobalHotkeyManager(pump);
        HotkeyGesture.TryParse("Shift + A", out var gesture, out _);
        Assert.False(manager.Register(903, gesture!).Success);
    }
}

public class EnvironmentAndSamplingTests
{
    [SkippableFact]
    public void EnvironmentInspectionDoesNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        var env = EnvironmentInspector.Inspect();
        Assert.True(env.OsBuild > 0);
        Assert.True(env.Is64BitProcess);
    }

    [SkippableFact]
    public void ForegroundQueryDoesNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        _ = ForegroundWindowMonitor.GetCurrent();
        _ = ForegroundWindowMonitor.ListVisibleApps();
    }

    [SkippableFact]
    public void ScreenSamplingReturnsPixels()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        var sample = ScreenSampler.TrySample(10, 10, 5);
        Skip.If(sample is null, "No interactive desktop available for screen sampling in this session.");
        Assert.Equal(5 * 5 * 4, sample!.Bgra.Length);
        Assert.True(sample.Average.IsFinite);
    }

    [Fact]
    public void SamplingClassifierDistinguishesOriginalFromFiltered()
    {
        var filter = ColorMatrix.FromRowMajor([0, 0, 1, 0, 1, 0, 1, 0, 0]); // swap red/blue
        var known = Rgb.FromHex("#C83C32");
        Assert.Equal(SamplingVerdict.ReadsOriginal, ScreenSampler.ClassifySampling(known, known, filter));
        Assert.Equal(SamplingVerdict.ReadsFiltered, ScreenSampler.ClassifySampling(known, filter.TransformClamped(known), filter));
        Assert.Equal(SamplingVerdict.Inconclusive, ScreenSampler.ClassifySampling(known, Rgb.Gray(0.5), filter));
        Assert.Equal(SamplingVerdict.Inconclusive, ScreenSampler.ClassifySampling(known, known, ColorMatrix.Identity));
    }
}
