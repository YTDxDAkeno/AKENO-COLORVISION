using System.Runtime.InteropServices;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Rendering.Magnification;

namespace Akeno.ColorVision.IntegrationTests;

/// <summary>
/// Exercises the real Windows Magnification API. These tests prove that the native declarations,
/// the MAGCOLOREFFECT memory layout and the matrix order are accepted by Windows (a matrix written
/// with MagSetFullscreenColorEffect is read back identically). They cannot prove that the effect is
/// visible on a monitor — that requires the manual checks in docs/TESTING.md.
/// The matrices used are nearly identity so a developer running the tests barely sees anything.
/// </summary>
[Collection("Magnification")]
public class MagnificationBackendTests
{
    // A subtle but non-identity transform (≈2 % change) for read-back checks.
    private static readonly ColorMatrix Subtle = ColorMatrix.FromRowMajor([0.98, 0.02, 0, 0.01, 0.98, 0.01, 0, 0.02, 0.98], 0.005, 0, 0.005);

    [Fact]
    public void MagColorEffectIsHundredBytes()
    {
        // typedef struct { float transform[5][5]; } MAGCOLOREFFECT;  → 25 × 4 bytes
        Assert.Equal(100, Marshal.SizeOf<MAGCOLOREFFECT>());
        unsafe
        {
            Assert.Equal(100, sizeof(MAGCOLOREFFECT));
        }
    }

    [Fact]
    public void MagColorEffectPreservesElementOrder()
    {
        var values = Enumerable.Range(0, 25).Select(i => (float)i).ToArray();
        var effect = MAGCOLOREFFECT.FromArray(values);
        Assert.Equal(values, effect.ToArray());
    }

    [SkippableFact]
    public void MagnificationDllIsAvailableOnWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        Assert.True(MagnificationNative.IsAvailable(out var detail), detail);
    }

    [SkippableFact]
    public void CapabilitiesReportAvailableButNotVerified()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var backend = new MagnificationColorFilterBackend();
        var caps = backend.CheckCapabilities();
        Assert.Equal(BackendSupportLevel.AvailableNotVerified, caps.Level);
        Assert.True(caps.SupportsColorMatrix);
        Assert.False(caps.SupportsNonLinearTransforms);
        backend.UserConfirmedVisible = true;
        Assert.Equal(BackendSupportLevel.AvailableAndTested, backend.CheckCapabilities().Level);
    }

    [SkippableFact]
    public void SetAndReadBackRoundTripsThroughWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var backend = new MagnificationColorFilterBackend { SmoothTransitions = false };
        var init = backend.Initialize();
        Skip.IfNot(init.Success, $"Magnification API not usable in this session: {init}");
        try
        {
            var apply = backend.ApplyTransformation(Subtle);
            Assert.True(apply.Success, apply.ToString());
            var read = backend.ReadCurrentTransformation();
            Assert.NotNull(read);
            Assert.True(read.ApproximatelyEquals(Subtle, 1e-5), $"Read back:\n{read}\nExpected:\n{Subtle}");

            var disable = backend.Disable();
            Assert.True(disable.Success, disable.ToString());
            Assert.True(backend.ReadCurrentTransformation()!.IsIdentity(1e-6));
        }
        finally
        {
            backend.ForceIdentity();
            backend.Restore();
        }
    }

    [SkippableFact]
    public void ControllerLifecycleWithRealBackend()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        var backend = new MagnificationColorFilterBackend { SmoothTransitions = true };
        using var controller = new FilterController(backend);
        var init = controller.Initialize();
        Skip.IfNot(init.Success, $"Magnification API not usable in this session: {init}");

        Assert.True(controller.SetTransformation(Subtle).Success);
        Assert.True(controller.Enable().Success);
        Assert.Equal(FilterState.On, controller.State);

        // Rapid enable/disable must always end in a consistent state.
        for (var i = 0; i < 20; i++)
        {
            controller.Toggle();
        }

        Assert.Equal(FilterState.On, controller.State);
        Assert.True(backend.ReadCurrentTransformation()!.ApproximatelyEquals(Subtle, 1e-5));

        // Smooth update to another matrix ends exactly on the target.
        var other = ColorMatrix.FromRowMajor([0.99, 0.01, 0, 0, 0.99, 0.01, 0.01, 0, 0.99]);
        Assert.True(controller.SetTransformation(other).Success);
        Assert.True(backend.ReadCurrentTransformation()!.ApproximatelyEquals(other, 1e-5));

        Assert.True(controller.EmergencyReset(TimeSpan.FromSeconds(2)).Success);
        Assert.Equal(FilterState.Off, controller.State);
        Assert.True(backend.ReadCurrentTransformation()!.IsIdentity(1e-6));

        controller.Shutdown();
        Assert.Equal(FilterState.ShutDown, controller.State);
        Assert.False(backend.IsInitialized);
    }

    [SkippableFact]
    public void ForceIdentityWorksEvenWithoutInitialize()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var backend = new MagnificationColorFilterBackend();
        var result = backend.ForceIdentity();
        Skip.If(!result.Success && result.Message.Contains("MagInitialize", StringComparison.Ordinal), $"Magnification API not usable in this session: {result}");
        Assert.True(result.Success, result.ToString());
        backend.Restore();
    }

    [SkippableFact]
    public void RejectsNonFiniteMatrixWithoutTouchingScreen()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only.");
        using var backend = new MagnificationColorFilterBackend();
        var init = backend.Initialize();
        Skip.IfNot(init.Success, $"Magnification API not usable in this session: {init}");
        try
        {
            var bad = ColorMatrix.FromRowMajor([double.NaN, 0, 0, 0, 1, 0, 0, 0, 1]);
            Assert.False(backend.ApplyTransformation(bad).Success);
        }
        finally
        {
            backend.Restore();
        }
    }
}
