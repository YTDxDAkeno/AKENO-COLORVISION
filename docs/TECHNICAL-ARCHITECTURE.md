# Technical architecture

## 1. Goals and constraints

* A **real** system-wide color transform (not a transparent overlay) that works for the desktop,
  browsers, video, Discord and windowed/borderless games — Minecraft in particular.
* Instant on/off, safe failure behaviour, and a recovery path even if the app hangs or crashes.
* No kernel drivers, no DLL injection, no administrator rights, no network access.
* Portable, self-contained Windows x64 distribution.

## 2. Technology decision

| Choice | Reason |
|---|---|
| **C# / .NET 10 (LTS)** | Mature, fast, memory-safe; P/Invoke makes Win32 interop straightforward; self-contained single-file publishing means the user installs nothing. |
| **WPF** for the UI | Stable, fully featured desktop UI with good accessibility (UI Automation, keyboard navigation), no extra runtime, builds on CI and even cross-compiles from Linux. WinUI 3 was considered but adds the Windows App SDK runtime/packaging complexity without benefits for this app. |
| **WinForms `NotifyIcon`** for the tray | Battle-tested tray implementation that ships in the same Windows Desktop runtime (no extra dependency). |
| **No third-party runtime libraries** | Smaller attack surface, simpler licensing. Tests use xUnit. |
| **System.Text.Json** | Versioned profile/settings files. |
| **GitHub Actions (windows-latest)** | Builds, runs unit tests *and* real Windows-API integration tests, packages the ZIP and installer. |

## 3. Solution layout

```
src/
  Akeno.ColorVision.ColorScience   pure math: ColorMatrix, sRGB, Machado CVD model, daltonization,
                                   adjustments, CIEDE2000/OKLab, color naming, validator, evaluator
  Akeno.ColorVision.Core           platform-independent logic: profiles (+migration), settings,
                                   IColorFilterBackend + FilterController state machine, hotkey model,
                                   calibration analysis, game rules + auto switcher, session guard, logging
  Akeno.ColorVision.Windows        Win32 interop without UI: MessagePumpThread, global hotkeys,
                                   foreground monitor, screen sampler, environment inspector, startup
  Akeno.ColorVision.Rendering      MagnificationColorFilterBackend, BackendCatalog
  Akeno.ColorVision.Recovery       AkenoColorVision-EmergencyReset.exe (stand-alone, trimmed)
  Akeno.ColorVision.App            WPF app (AkenoColorVision.exe): services, view-models, views
  Shared/RecoveryProtocol.cs       file/event names shared by app and reset tool
tests/
  Akeno.ColorVision.UnitTests      cross-platform tests (269)
  Akeno.ColorVision.IntegrationTests  real Windows API tests (skipped elsewhere)
  Akeno.ColorVision.VisualTests    manual visual test board (WPF)
tools/
  Akeno.ColorVision.PresetLab      simulation-based preset evaluation/tuning
```

Dependency direction: `App → Rendering → Windows → Core → ColorScience`. `Recovery` depends on nothing
but the shared protocol file, so it keeps working if anything else is broken.

## 4. Rendering backends

### 4.1 Backend A — Windows Magnification API (shipped)

The Magnification API's full-screen color effect (`MagSetFullscreenColorEffect`, Windows 8+) asks the
Desktop Window Manager to apply a 5×5 color matrix to the composed desktop. This is the same mechanism
screen-color tools use; it needs no special privileges and adds no rendering pass of our own.

Implementation details (`MagnificationColorFilterBackend`):

* **Declarations.** `MagInitialize`, `MagUninitialize`, `MagSetFullscreenColorEffect(ref MAGCOLOREFFECT)`,
  `MagGetFullscreenColorEffect(out MAGCOLOREFFECT)`. `MAGCOLOREFFECT` is a blittable struct with a
  `fixed float transform[25]` (100 bytes) — verified by tests on Windows.
* **Matrix order.** MAGCOLOREFFECT uses the *row-vector* convention (`[R G B A 1] · T`, offsets in row 4),
  confirmed against Microsoft's documented grayscale and inversion examples. Internally the app uses a
  column-vector 3×4 affine `ColorMatrix`; `ToMagColorEffect()` is the single conversion point.
* **Read-back verification.** After every set, the backend reads the active matrix back and logs a
  warning if Windows reports something different. On the CI runner, set → get round-trips exactly, and a
  test window's pixels change exactly as the matrix predicts.
* **Threading.** All Magnification calls run on one dedicated background STA thread with its own Win32
  message loop (`MessagePumpThread`). The UI thread never calls the API directly.
* **Smooth switching.** Profile changes cross-fade in 6 steps over ~120 ms (smoothstep), optional.
* **No magnification.** Only the color effect is used; `MagSetFullscreenTransform` is never called.
* **Pre-existing effects.** At initialisation the current effect is read. If another program's effect was
  active (e.g. Magnifier's color inversion) the user is told, but it is **not** blindly re-applied on exit
  (its owner may have exited, which could leave a stale distortion nobody can remove). The owning program
  re-applies its own effect when needed. This trade-off is deliberate: never leave a distorted desktop.
* **Health check.** Every 5 s the active matrix is compared with the intended one. If it reverted to
  identity (driver reset, DWM restart) it is re-applied; if another program changed it, the app does not
  fight it and shows a notice instead.
* **System events.** Re-applied after resume from sleep, session unlock/reconnect and display changes.

Limitations (shown in the app): matrix-only (no curves/LUTs), one matrix for all monitors, exclusive-
fullscreen content that bypasses composition may be unaffected, and the API is documented as unsupported
under WOW64 (the app is 64-bit only).

### 4.2 Backend B — GPU capture + shader (evaluated, not shipped)

Pipeline considered: Windows Graphics Capture → D3D11 texture → pixel shader (LUT) → click-through
topmost overlay.

| Question | Finding |
|---|---|
| Recursive capture | Solvable: `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` (Win10 2004+) hides the overlay from capture. |
| Input | Overlay must be `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE`; works for most apps, but a topmost layered window can interfere with some games' fullscreen optimisation. |
| Latency | At least one extra frame (capture → shader → present), typically 1–2 frames, plus frame pacing jitter; noticeable in fast games. |
| Multi-monitor / DPI | One capture + overlay per monitor, recreated on resolution/DPI changes. |
| Exclusive fullscreen | The overlay cannot appear on top; capture may also fail. No improvement over backend A. |
| HDR | Capture delivers FP16 scRGB for HDR monitors; shaders must handle linear HDR values. |
| Capture border / permission | Windows 11 can hide the yellow border with borderless capture access; still a "screen is being captured" privacy concern. |
| Protected content | DRM video is captured as black, so the overlay would black it out. |
| Device loss | Needs full D3D device-loss handling. |

Conclusion: backend B would enable non-linear corrections and per-monitor settings, but it is heavier,
adds latency, cannot help exclusive-fullscreen games either, and cannot be validated without hands-on
Windows hardware testing. Following the priority rules, **v0.1.0 ships only the verified Magnification
backend** and reports backend B honestly as *Unsupported — not included in this version* in diagnostics.

### 4.3 Backend abstraction and status

```csharp
public interface IColorFilterBackend : IDisposable
{
    BackendCapabilities CheckCapabilities();   // no side effects
    BackendResult Initialize();
    BackendResult ApplyTransformation(ColorMatrix m);
    BackendResult UpdateTransformation(ColorMatrix m);
    BackendResult Disable();
    BackendResult Restore();                    // shutdown: back to the pre-start state where safe
    ColorMatrix? ReadCurrentTransformation();
    BackendResult ForceIdentity();              // last resort, bypasses sequencing
}
```

Support levels shown to the user: *Available and tested* (only after the user confirms the visible
5-second test), *Available, not yet verified on this PC*, *Experimental*, *Unsupported*, *Failed*.
There is no silent fallback to a fake implementation.

## 5. Filter state machine and safety

`FilterController` (Core) is the single owner of the screen effect:

```
NotInitialized ──Initialize──▶ Off ◀──Disable/Emergency── On
      │                         │ ──────Enable──────────▶ │
      └──fail──▶ Failed ◀──apply/update/disable fail──────┘
                     └──Enable (retry)──▶ …        any ──Shutdown──▶ ShutDown
```

* Every matrix passes `TransformValidator` first: finite values, |coefficient| ≤ 4, |offset| ≤ 0.6,
  black/white luminance difference ≥ 0.30, |determinant| ≥ 0.02 (no collapsing colors). Unsafe matrices
  are rejected and the previous one stays.
* Any backend failure triggers `ForceIdentity()` before entering `Failed` — never a half-applied state.
* Thread-safe with a lock; events are raised outside the lock.

### Emergency and recovery layers

| Layer | Handles |
|---|---|
| **Emergency shortcut** (`Ctrl+Alt+Shift+Backspace`) | Registered on a separate hotkey thread (not the UI thread). Calls `EmergencyReset`, which waits at most 2 s for the controller lock and otherwise forces identity directly. If even that fails, the process terminates itself so Windows/the watchdog take over. |
| **Reset colors** button / tray item | Same reset from the UI. |
| **Emergency reset tool** (`AkenoColorVision-EmergencyReset.exe`) | Signals a named event the app's hotkey thread waits on; if no acknowledgement within 2.5 s it kills the app; then resets the effect from its own process; writes a safe-mode flag. |
| **Crash watchdog** | The app starts the reset tool with `--watchdog --pid N`. It waits on the process handle (no CPU). If the app exits while its session lock file still exists (crash, killed in Task Manager), it resets the colors and leaves a note. |
| **Safe mode** | Next start after an unclean exit or emergency reset (or with `--safe-mode` / Shift held): the filter is not turned on automatically. |
| **Clean shutdown** | On exit, sign-out and Windows shutdown: filter off → `Restore` → `MagUninitialize` → delete session lock. |

What cannot be guaranteed: Microsoft does not document what happens to a full-screen color effect when
its owning process dies abruptly. The watchdog and reset tool cover both possibilities (effect removed by
Windows, or left behind), and a reboot always clears it. If both the app and the watchdog are killed at the
same time, the user must run the reset tool or sign out/reboot.

## 6. Threads

| Thread | Purpose |
|---|---|
| UI (WPF dispatcher) | Windows, view-models, services. |
| `Akeno Magnification` | All Magnification API calls (`MessagePumpThread`). |
| `Akeno Hotkeys` | `RegisterHotKey` messages, emergency event, activation event, `SetWinEventHook` foreground events. |

`MessagePumpThread` waits with `MsgWaitForMultipleObjectsEx`, so idle threads use no CPU.

## 7. Game detection

`SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)` plus a 2-second poll for title changes.
For the foreground window only the executable name (`QueryFullProcessImageName` with
`PROCESS_QUERY_LIMITED_INFORMATION`) and the window title are read. No memory reading, no hooks inside
other processes, nothing an anti-cheat system would object to. `AutoProfileSwitcher` (pure, unit-tested)
decides: activate the game profile, restore 1.5 s after the game loses focus (no flicker on quick
alt-tab), and respect manual changes.

## 8. Color identifier

GDI `BitBlt` of a 9×9 block around the cursor (physical pixels; the app is per-monitor DPI aware), 3×3
average. Whether those pixels are pre- or post-filter is not documented, so the app **measures** it: a
small window with a known color is shown and sampled while the filter is on, and the result
(original/filtered) is stored. Filtered samples are converted back with the inverse matrix and labelled
as estimates. The identifier's own popups use `WDA_EXCLUDEFROMCAPTURE` so they are never sampled.
On the CI Windows session the integration test `ObserveWhetherScreenReadsSeeTheColorEffect` found that
`BitBlt` returns the **filtered** pixels (exact match to the transformed color), which also confirms that
Windows applies the matrix to the composed desktop image. The runtime check is kept because GPU drivers
and Windows versions may differ.

## 9. Persistence

`%LOCALAPPDATA%\AkenoColorVision\` (or `AkenoColorVision-Data` next to the exe if `portable.txt` exists):

| File | Content |
|---|---|
| `settings.json` | Versioned settings (schema 1), normalised on load, quarantined if corrupt. |
| `profiles/<id>.json` | Versioned profiles (schema 2; schema 1 legacy format migrated with backup). |
| `logs/akeno-colorvision-YYYYMMDD.log` | Local log, 7-day retention, user names stripped, can be disabled. |
| `session.lock`, `safe-mode.flag`, `last-recovery.txt` | Crash detection / safe mode. |

All writes are atomic (temp file + replace). Nothing is ever sent over the network.

## 10. Security and privacy properties

`asInvoker` manifest (no admin), 64-bit only, native DLLs loaded from System32 only
(`DefaultDllImportSearchPaths`), no drivers, no injection, no network code, no telemetry, profile imports
size-limited and validated, diagnostics free of screenshots/titles/answers/user names.
