# Compatibility

Legend:

* **Verified (CI)** — checked automatically on a GitHub-hosted Windows runner (real Windows APIs, virtual display).
* **Expected** — follows from how Windows applies the filter, but not yet checked on physical hardware.
* **Not yet verified** — needs a manual test (see [TESTING.md](TESTING.md)).
* **Limitation** — known not to work or works only partly.

Nothing below is claimed as "tested in practice" unless it says so. Please report results.

## Operating system

| Environment | Status |
|---|---|
| Windows 11 x64 | Target platform. Builds and integration tests run on GitHub's hosted `windows-latest` runner. **Not yet verified on a Windows 11 desktop PC.** |
| Windows 10 x64 (1809+) | Expected to work; not tested. `WDA_EXCLUDEFROMCAPTURE` (used for the identifier popups) needs 2004+; older versions just skip it. |
| Windows on ARM64 | Not built in v0.1.0 (x64 build may run under emulation; the Magnification API does not support 32-bit-on-64-bit, but x64 emulation on ARM64 is untested). |
| 32-bit Windows / 32-bit process | Not supported (Magnification API is unsupported under WOW64). |
| Remote Desktop sessions | Unknown, not tested. |

## Filter mechanism (Magnification API)

| Behaviour | Status |
|---|---|
| `MagInitialize` / `MagSetFullscreenColorEffect` / `MagGetFullscreenColorEffect` accepted by Windows; matrix read back exactly | **Verified (CI)** |
| Filter lifecycle: enable, rapid toggling, smooth update, emergency reset, shutdown | **Verified (CI)** with the real backend |
| Matrix applied to the composed desktop image | **Verified (CI)**: a red↔blue test matrix changed a known-color window from #C83C32 to #323CC8 exactly, and reset restored it |
| Effect visible on a physical monitor | Not yet verified — use *About → Check that the filter works on this PC* |
| Whether screen reads (BitBlt) return original or filtered pixels | **Verified (CI): filtered pixels.** The identifier converts back with the inverse matrix (marked as estimates) and re-checks on each PC |
| One filter for all monitors | By design (the API has one full-screen effect) |
| Mouse cursor | May or may not be filtered depending on hardware cursor support — not yet verified |
| HDR / advanced color | Expected to work, but colors can look different than in SDR; calibrate with HDR off if possible. Not yet verified. |
| Night light | Independent (applied later in the display pipeline); tints everything — turn off during calibration. |
| Windows color filters (Settings → Accessibility) | Stack with this app; the app warns when they are on. |
| Windows Magnifier with color inversion | Competes for the same effect; the app warns at start-up and does not fight later changes. |
| Sleep/resume, lock/unlock, display changes | Filter is re-applied automatically (logic unit-tested; not yet verified on hardware). |

## Applications

| Application | Expected | Status |
|---|---|---|
| Windows desktop, File Explorer, Settings | Works (composited by DWM) | Not yet verified |
| Google Chrome, Microsoft Edge, Firefox | Works | Not yet verified |
| YouTube / web video | Works; DRM-protected video may behave differently | Not yet verified |
| Discord | Works | Not yet verified |
| Image viewers (Photos, etc.) | Works | Not yet verified |
| Borderless/windowed games | Works | Not yet verified |
| Exclusive-fullscreen games | **Limitation**: may bypass the filter. Use borderless/windowed mode. | — |
| Screenshots / screen recordings | GDI-based captures include the filter (verified in CI); other capture methods may differ | Partly verified |

## Minecraft

| Edition / mode | Expected | Status |
|---|---|---|
| Java Edition, windowed or maximized | Filter applies; auto-detection via `javaw.exe` + "Minecraft…" title | **Not yet tested in-game** |
| Java Edition, F11 fullscreen | Usually composed by Windows (filter applies), but some driver/OS combinations use a direct-flip path that may bypass it | **Not yet tested in-game** |
| Java Edition via third-party launchers (Prism, MultiMC, etc.) | Same `javaw.exe` + title → detected | Not yet tested |
| Lunar / Badlion / other clients with custom titles | Not auto-detected unless the title starts with "Minecraft" — add the app manually in **Gaming** | Not yet tested |
| Bedrock (Minecraft for Windows, `Minecraft.Windows.exe`) | Filter applies in windowed/fullscreen-window mode; detected by executable name | **Not yet tested in-game** |
| Bedrock, older Store (UWP) builds | Detected via `ApplicationFrameHost.exe` + "Minecraft" title | Not yet tested |
| Minecraft Launcher | Intentionally not detected | Unit-tested |

Detection reads only the executable name and window title (no process memory, no injection), so it should
not conflict with anti-cheat systems on servers. This is a design property, not a tested guarantee for every
anti-cheat product.

## Displays

| Setup | Status |
|---|---|
| Single monitor, 100–200 % scaling | Expected (per-monitor DPI aware app) — not yet verified |
| Multiple monitors | One filter for all; app UI handles per-monitor DPI — not yet verified |
| Monitor hot-plug / resolution change | Filter re-applied after display changes — not yet verified |
