using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Filtering;

namespace Akeno.ColorVision.UnitTests;

/// <summary>In-memory backend that records what would be on screen.</summary>
internal sealed class FakeBackend : IColorFilterBackend
{
    private readonly object _gate = new();

    public string Id => "fake";

    public string DisplayName => "Fake";

    public bool IsInitialized { get; private set; }

    public ColorMatrix OnScreen { get; private set; } = ColorMatrix.Identity;

    public ColorMatrix? Preexisting { get; set; }

    public bool FailInitialize { get; set; }

    public bool FailApply { get; set; }

    public bool FailDisable { get; set; }

    public int ApplyCount { get; private set; }

    public int ForceIdentityCount { get; private set; }

    public bool Restored { get; private set; }

    public ManualResetEventSlim? BlockUpdates { get; set; }

    public BackendCapabilities CheckCapabilities() =>
        new(Id, DisplayName, BackendSupportLevel.AvailableNotVerified, "fake", [], true, false, false);

    public BackendResult Initialize()
    {
        if (FailInitialize)
        {
            return BackendResult.Fail("init failed");
        }

        IsInitialized = true;
        if (Preexisting is not null)
        {
            OnScreen = Preexisting;
        }

        return BackendResult.Ok();
    }

    public BackendResult ApplyTransformation(ColorMatrix matrix)
    {
        if (FailApply)
        {
            OnScreen = matrix; // simulate a half-applied state the controller must clean up
            return BackendResult.Fail("apply failed");
        }

        lock (_gate)
        {
            ApplyCount++;
            OnScreen = matrix;
        }

        return BackendResult.Ok();
    }

    public BackendResult UpdateTransformation(ColorMatrix matrix)
    {
        BlockUpdates?.Wait(TimeSpan.FromSeconds(10));
        return ApplyTransformation(matrix);
    }

    public BackendResult Disable()
    {
        if (FailDisable)
        {
            return BackendResult.Fail("disable failed");
        }

        OnScreen = ColorMatrix.Identity;
        return BackendResult.Ok();
    }

    public BackendResult Restore()
    {
        Restored = true;
        OnScreen = ColorMatrix.Identity;
        IsInitialized = false;
        return BackendResult.Ok();
    }

    public ColorMatrix? ReadCurrentTransformation() => IsInitialized ? OnScreen : null;

    public BackendResult ForceIdentity()
    {
        lock (_gate)
        {
            ForceIdentityCount++;
            OnScreen = ColorMatrix.Identity;
        }

        return BackendResult.Ok();
    }

    public void Dispose()
    {
    }
}

public class FilterControllerTests
{
    private static readonly ColorMatrix Correction = CorrectionPipeline.Build(new CorrectionParameters { RedGreenToBlue = 0.8, RedGreenToLightness = 0.15 }, 1);

    [Fact]
    public void StartsUninitializedAndInitializesToOff()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        Assert.Equal(FilterState.NotInitialized, controller.State);
        Assert.True(controller.Initialize().Success);
        Assert.Equal(FilterState.Off, controller.State);
        Assert.True(backend.OnScreen.IsIdentity());
    }

    [Fact]
    public void EnableAppliesTargetAndDisableRestoresIdentity()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.Initialize();
        controller.SetTransformation(Correction);
        Assert.True(backend.OnScreen.IsIdentity(), "setting a matrix while off must not change the screen");

        Assert.True(controller.Enable().Success);
        Assert.Equal(FilterState.On, controller.State);
        Assert.True(backend.OnScreen.ApproximatelyEquals(Correction));

        Assert.True(controller.Disable().Success);
        Assert.Equal(FilterState.Off, controller.State);
        Assert.True(backend.OnScreen.IsIdentity());
    }

    [Fact]
    public void EnableInitializesAutomatically()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        Assert.True(controller.Enable().Success);
        Assert.Equal(FilterState.On, controller.State);
    }

    [Fact]
    public void SetTransformationWhileOnUpdatesScreen()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();
        var other = ColorAdjustments.Saturation(1.4);
        controller.SetTransformation(other);
        Assert.True(backend.OnScreen.ApproximatelyEquals(other));
    }

    [Fact]
    public void RejectsUnsafeMatrixAndKeepsPreviousTarget()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();

        var black = ColorMatrix.Scale(0, 0, 0);
        var result = controller.SetTransformation(black);
        Assert.False(result.Success);
        Assert.True(controller.TargetMatrix.ApproximatelyEquals(Correction));
        Assert.True(backend.OnScreen.ApproximatelyEquals(Correction));
    }

    [Fact]
    public void ToggleAlternates()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Toggle();
        Assert.True(controller.IsOn);
        controller.Toggle();
        Assert.False(controller.IsOn);
    }

    [Fact]
    public void RapidToggleFromManyThreadsEndsConsistent()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        Parallel.For(0, 200, _ => controller.Toggle());
        // 200 toggles from Off end in Off; the screen must match the state.
        Assert.Equal(FilterState.Off, controller.State);
        Assert.True(backend.OnScreen.IsIdentity());
        controller.Enable();
        Assert.True(backend.OnScreen.ApproximatelyEquals(Correction));
    }

    [Fact]
    public void ApplyFailureLeavesScreenNormalAndStateFailed()
    {
        var backend = new FakeBackend { FailApply = true };
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        var result = controller.Enable();
        Assert.False(result.Success);
        Assert.Equal(FilterState.Failed, controller.State);
        Assert.True(backend.OnScreen.IsIdentity(), "fail-safe must force identity");
        Assert.True(backend.ForceIdentityCount > 0);
        Assert.NotNull(controller.LastError);
    }

    [Fact]
    public void CanRecoverAfterFailure()
    {
        var backend = new FakeBackend { FailApply = true };
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();
        backend.FailApply = false;
        Assert.True(controller.Enable().Success);
        Assert.Equal(FilterState.On, controller.State);
    }

    [Fact]
    public void InitializeFailureIsReported()
    {
        var backend = new FakeBackend { FailInitialize = true };
        using var controller = new FilterController(backend);
        Assert.False(controller.Initialize().Success);
        Assert.Equal(FilterState.Failed, controller.State);
        Assert.False(controller.Enable().Success);
    }

    [Fact]
    public void DisableFailureFallsBackToIdentity()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();
        backend.FailDisable = true;
        controller.Disable();
        Assert.True(backend.OnScreen.IsIdentity());
        Assert.Equal(FilterState.Failed, controller.State);
    }

    [Fact]
    public void EmergencyResetFromOnRestoresNormalColors()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();
        Assert.True(controller.EmergencyReset(TimeSpan.FromSeconds(1)).Success);
        Assert.Equal(FilterState.Off, controller.State);
        Assert.True(backend.OnScreen.IsIdentity());
    }

    [Fact]
    public async Task EmergencyResetBypassesBusyController()
    {
        var gate = new ManualResetEventSlim(false);
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();

        backend.BlockUpdates = gate;
        var stuck = Task.Run(() => controller.SetTransformation(ColorAdjustments.Saturation(1.2)));
        await Task.Delay(100); // let the update take the lock and block

        var result = controller.EmergencyReset(TimeSpan.FromMilliseconds(100));
        Assert.True(result.Success);
        Assert.True(backend.ForceIdentityCount > 0);
        Assert.True(backend.OnScreen.IsIdentity());
        Assert.Equal(FilterState.Off, controller.State);

        gate.Set();
        await stuck.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmergencyResetWhenNeverInitializedIsHarmless()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        Assert.True(controller.EmergencyReset(TimeSpan.FromSeconds(1)).Success);
    }

    [Fact]
    public void ShutdownTurnsOffAndRestores()
    {
        var backend = new FakeBackend();
        var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Enable();
        controller.Shutdown();
        Assert.Equal(FilterState.ShutDown, controller.State);
        Assert.True(backend.OnScreen.IsIdentity());
        Assert.True(backend.Restored);
        Assert.False(controller.Enable().Success);
        controller.Shutdown(); // idempotent
    }

    [Fact]
    public void DetectsForeignEffect()
    {
        var backend = new FakeBackend { Preexisting = ColorAdjustments.HueRotation(180) };
        using var controller = new FilterController(backend);
        controller.Initialize();
        Assert.True(controller.ForeignEffectDetected);
    }

    [Fact]
    public void ReapplyOnlyWhenOn()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        controller.SetTransformation(Correction);
        controller.Initialize();
        controller.Reapply();
        Assert.Equal(0, backend.ApplyCount);
        controller.Enable();
        controller.Reapply();
        Assert.Equal(2, backend.ApplyCount);
    }

    [Fact]
    public void RaisesStateChangedOutsideLock()
    {
        var backend = new FakeBackend();
        using var controller = new FilterController(backend);
        var states = new List<FilterState>();
        controller.StateChanged += (_, e) =>
        {
            states.Add(e.Current);
            _ = controller.State; // re-entrant read must not deadlock
            _ = controller.TargetMatrix;
        };
        controller.SetTransformation(Correction);
        controller.Enable();
        controller.Disable();
        Assert.Contains(FilterState.On, states);
        Assert.Equal(FilterState.Off, states[^1]);
    }
}
