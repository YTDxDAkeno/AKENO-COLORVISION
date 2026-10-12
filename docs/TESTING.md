# Testing

## 1. Verification levels

A successful build does not prove the screen filter works, and unit tests cannot prove game compatibility.
Each claim in this project is labelled with the level of evidence behind it:

| Level | Meaning | Status for v0.1.0 |
|---|---|---|
| **Compiled** | Builds for Windows x64 (Release) | ✅ Linux cross-build and GitHub `windows-latest` |
| **Unit tested** | Platform-independent logic | ✅ 269 tests pass (Linux and Windows CI) |
| **Integration tested** | Real Windows APIs on a Windows session (CI runner, virtual display) | ✅ 18 tests pass on `windows-latest`, including proof that the filter transforms the composed desktop image (§3.1) |
| **Visually verified on Windows** | A person saw the filter change a real monitor | ❌ Not yet — checklist §5 |
| **Tested inside Minecraft** | A person played Minecraft with the filter | ❌ Not yet — checklist §6 |

## 2. Automated tests

Run locally:

```
dotnet test tests/Akeno.ColorVision.UnitTests
dotnet test tests/Akeno.ColorVision.IntegrationTests     # real tests on Windows, reported as skipped elsewhere
python scripts/check-xaml-resources.py                   # every XAML resource key resolves
dotnet run --project tools/Akeno.ColorVision.PresetLab -- evaluate
```

### Unit tests (`tests/Akeno.ColorVision.UnitTests`, 269 tests)

| Area | What is covered |
|---|---|
| Color matrices | identity, row/column semantics, offsets, composition order and associativity, strength interpolation and clamping, inversion, singular matrices, clamping, NaN detection |
| MAGCOLOREFFECT layout | row-vector layout, offsets in row 4, alpha pass-through; Microsoft's documented **inversion** and **grayscale** examples reproduced exactly; round-trip |
| sRGB / luminance | transfer function round-trips and known values, negative values, WCAG contrast |
| Machado model | every tabulated row sums to 1 (all 33 matrices), published values, interpolation, severity clamping, grays unchanged, red/green converge with severity |
| CIEDE2000 | 16 pairs from Sharma, Wu & Dalal (2005) test data to 4 decimals, symmetry, Lab/OKLab white points |
| Adjustments | gray preservation of every remapping block, saturation/contrast/hue identities, primary hue rotation = hue rotation for equal angles, opponent-transfer direction, daltonization gains for the target observer |
| Color naming | 19 canonical colors categorised correctly, exact names (incl. "Dark Red #8B2424", "Olive Green #727A32"), no name/category contradiction anywhere on a 16³ RGB grid |
| Presets | all safe at default and full strength, grays neutral, each discrimination preset raises simulated ΔE ≥ 30 % and reduces hard pairs, Strong leaves no hard pairs, Minecraft preset best on Nether pairs |
| Validator | identity safe; black, washed-out, NaN, huge coefficients, large offsets, grayscale rejected; channel swap allowed; tint warning |
| Profiles | round-trip, legacy (schema 1, the format from the specification) migration, legacy modes, newer-schema rejection, invalid JSON, out-of-range clamping, name limits |
| Profile store | save/load, corrupt files quarantined (never deleted), migration with backup, duplicate ids repaired, built-ins cannot be saved, import gets new id, size limit |
| Settings | safe defaults, round-trip, missing/corrupt files, normalisation of bad values and hotkeys, null sub-objects |
| Hotkeys | parsing/formatting, invalid input, modifier requirement, reserved Windows/Magnifier shortcuts, default validity, duplicate repair |
| Filter controller | state machine, auto-initialise, unsafe matrix rejection, **atomic toggling from 200 parallel calls**, apply/disable failure → identity forced, recovery after failure, emergency reset (also while another thread holds the controller), shutdown, foreign effect detection, re-apply, events outside the lock |
| Game detection | Minecraft Java titles, launcher/other apps ignored, Bedrock (exe and UWP), disabled rules, user-rule priority; switcher: activate/restore with delay, no flicker on quick alt-tab, manual override, reset |
| Calibration | simulated deutan/protan answers lead to matching starting points, typical answers → gentle correction, control-pair warning, too few answers, **no diagnosis wording**, Strong start for "can't distinguish objects", candidate rounds, saved profile contains answers but no diagnosis |
| Recovery | clean start, leftover lock → unclean shutdown, clean exit, emergency flag consumed once, requested safe mode, portable data folder |
| Diagnostics | user-profile path removed, report sections, file logger toggle, memory logger capacity, atomic writes |

### Integration tests (`tests/Akeno.ColorVision.IntegrationTests`, 18 tests)

Use `Xunit.SkippableFact`, so on non-Windows machines (or sessions without a desktop) they are reported as
**skipped**, never as passed.

| Test | Proves |
|---|---|
| `MagColorEffectIsHundredBytes`, `MagColorEffectPreservesElementOrder` | Native struct layout (100 bytes, 25 floats in order). |
| `MagnificationDllIsAvailableOnWindows` | `Magnification.dll` exports all four functions. |
| `CapabilitiesReportAvailableButNotVerified` | Status reporting; "tested" only after user confirmation. |
| `SetAndReadBackRoundTripsThroughWindows` | A non-identity matrix set with `MagSetFullscreenColorEffect` is returned unchanged by `MagGetFullscreenColorEffect`; disable returns identity. |
| `ControllerLifecycleWithRealBackend` | Enable, 20 rapid toggles, smooth update, emergency reset, shutdown — with the real API. |
| `ForceIdentityWorksEvenWithoutInitialize`, `RejectsNonFiniteMatrixWithoutTouchingScreen` | Emergency path and input validation. |
| `ObserveWhetherScreenReadsSeeTheColorEffect` | Shows a known-color window, applies a red↔blue swap, samples with BitBlt; records whether screen reads see the effect (§3). |
| Message pump (3 tests) | Work runs on the pump thread, exceptions keep their type (this test found a real bug), wait-handle callbacks. |
| Hotkeys (2 tests) | `RegisterHotKey` works; a second registration of the same combination is reported as "already used". |
| Environment / sampling (4 tests) | Environment inspection, foreground query, BitBlt sampling, sampling classifier. |

## 3. Results

Latest CI run on `windows-latest` (see the Actions tab for logs and TRX files):

| Suite | Result |
|---|---|
| Release build (all projects) | ✅ |
| XAML resource check | ✅ 458 references, all resolved |
| Unit tests | ✅ 269 passed |
| Integration tests | ✅ 18 passed, 0 skipped on the Windows runner |
| Portable ZIP | ✅ built (`Akeno-ColorVision-v0.1.0-ci.3-win-x64.zip`) |
| Installer | ✅ built with Inno Setup 6.7 (`Akeno-ColorVision-Setup-v0.1.0-ci.3.exe`) |

Bugs found by the Windows CI runs and fixed before release:
1. `MessagePumpThread.Invoke` wrapped exceptions in `AggregateException` (found by an integration test).
2. `FilterController.Toggle` was not atomic — simultaneous toggles could both turn the filter on (found by
   the parallel unit test on Windows).

**Composed-output observation** (`ObserveWhetherScreenReadsSeeTheColorEffect`): Windows applied the red↔blue
test matrix to the composed desktop pixel-exactly — see [§3.1](#31-composed-output-observation).

### 3.1 Composed-output observation

From CI run #3 on `windows-latest` (test `ObserveWhetherScreenReadsSeeTheColorEffect`):

```
Known color:            #C83C32
Read before effect:     #C83C32
Read with swap effect:  #323CC8 (filtered would be #323CC8)
Read after reset:       #C83C32
VERDICT: screen reads return FILTERED pixels — Windows applied the matrix to the composed desktop image.
```

What this proves: on a real Windows session, a matrix set through the app's Magnification backend is applied
by Windows to the composed desktop image, pixel-exactly, and is removed again on reset. GDI screen reads
(`BitBlt`) return the *filtered* pixels, so the color identifier converts samples back with the inverse
matrix and labels them as estimates (the app still re-checks this on each PC).

What it does not prove: what a physical monitor shows on every GPU/driver, or behaviour in exclusive-
fullscreen or hardware-overlay paths. That still needs the manual checklists below.

## 4. Visual test board

`tests/Akeno.ColorVision.VisualTests` (`AkenoColorVision-VisualTests.exe`) shows the calibration pairs,
the Nether-inspired palette, ramps (red→green, red→brown→olive, gray ramp, saturation ramp, hue strip),
primaries, colored text samples and identifier reference patches. Run it with
`dotnet run --project tests/Akeno.ColorVision.VisualTests` on Windows.

## 5. Manual checklist — desktop (Windows 11)

Record results (✅/❌ + notes) and attach an exported diagnostics file.

**Setup:** Night light off, HDR off for the first pass, Windows color filters off, 100 % and 150 % scaling.

| # | Step | Expected |
|---|---|---|
| D1 | Start `AkenoColorVision.exe` from the extracted ZIP | Window opens; SmartScreen may ask once; Home shows "The filter is OFF". |
| D2 | About → *Run the 5-second test* | Whole screen swaps red and blue for 5 s, then returns; answer "Yes" → backend status becomes "Available and tested". |
| D3 | Home → *Turn filter on* (Red-Green Assistance) | Colors change everywhere; gray ramp in the visual test board stays neutral; black stays black, white stays white. |
| D4 | Move the strength slider 0 → 100 % | Change is smooth and continuous. |
| D5 | Switch through all presets on Home | Each applies instantly with a short fade; no flicker or flash. |
| D6 | `Ctrl+Alt+Shift+F` from another app (e.g. browser) | Filter toggles; "Filter ON/OFF" message appears at the top and disappears; focus stays in the browser. |
| D7 | Rapidly press the toggle shortcut 20 times | Ends in a consistent state; status text matches the screen. |
| D8 | Open Chrome/Edge, YouTube video, Discord, Photos | All filtered. Note any app that is not. |
| D9 | Color Filters → make a custom copy → move every slider | Each slider visibly changes the screen when the profile is active; values persist after restart. |
| D10 | Hold *Hold to compare* | Original colors while held, corrected when released. |
| D11 | Emergency: `Ctrl+Alt+Shift+Backspace` | Normal colors immediately; app shows the reset; filter off. |
| D12 | Run `AkenoColorVision-EmergencyReset.exe` while the filter is on | Message box "Screen colors have been reset"; app reports the reset; next start is in safe mode. |
| D13 | Kill `AkenoColorVision.exe` in Task Manager while the filter is on | Within ~1 s normal colors (Windows and/or watchdog); next start shows the safe-mode notice. Note which happened. |
| D14 | Sleep and resume; lock and unlock (Win+L) | Filter still on afterwards. |
| D15 | Change resolution / scaling, plug a second monitor | Filter still applied on all monitors; app UI crisp. |
| D16 | Turn on Windows color filters (Win+Ctrl+C if enabled) and restart the app | Warning notice about stacked filters. |
| D17 | HDR on | Notice about HDR; filter works; note how colors look. |
| D18 | Close window (X) | App stays in tray with balloon hint; tray menu works; *Exit* restores normal colors. |
| D19 | Settings → Start with Windows → sign out/in | App starts in the tray; filter on only if "Turn the filter on when the app starts" is enabled. |
| D20 | Color Identifier: point at the visual test board reference patches, press `Ctrl+Alt+Shift+C` | Popup next to the pointer with the listed name/HEX (filter off: exact ±1). With the filter on: label says "original" or "estimated". |
| D21 | Identifier → *Run the check now* (filter on) | Verdict shown; matches the CI observation. |
| D22 | Calibration end-to-end | All five steps work; *Try on whole screen* reverts after 10 s; saved profile becomes active. |
| D23 | Profiles → export, delete, import → *Try it* → let the 15-s countdown expire | Previous colors return automatically. |
| D24 | Keyboard only (Tab/Shift+Tab/Space/Enter/arrows) through every page | All controls reachable; focus ring clearly visible. |
| D25 | Narrator on | Pages, buttons, sliders and status changes are announced with meaningful names. |
| D26 | Light theme | Readable everywhere. |
| D27 | Resource use: Task Manager after 10 min idle with filter on | Record CPU (expected ≈0 %) and memory. |

## 6. Manual checklist — Minecraft: Java Edition

Use a world with access to the Nether (Creative mode is fine).

| # | Step | Expected |
|---|---|---|
| M1 | Gaming page: automatic switching on, "Also turn the filter on while playing" on | — |
| M2 | Start Minecraft Java (windowed) | Within ~1–2 s the profile switches to *Minecraft — Nether Visibility*; Gaming page shows "Detected: Minecraft: Java Edition". |
| M3 | Enter the Nether; compare netherrack, crimson nylium, nether wart blocks, soul sand/soil, basalt, blackstone, nether bricks, magma, ancient debris | Note which pairs are now distinguishable that were not before (toggle with `Ctrl+Alt+Shift+F`). |
| M4 | Press F11 (fullscreen) | Note whether the filter still applies. If not, F11 back. |
| M5 | Alt-Tab to the desktop and back within 1 s | No flicker/switch. |
| M6 | Alt-Tab to the desktop for > 2 s | Previous profile restored. |
| M7 | Quit Minecraft | Previous profile and filter state restored. |
| M8 | In-game shortcuts (toggle, identifier) | Work without leaving the game; the game keeps focus; no popups while playing (unless enabled). |
| M9 | FPS before/after (F3 screen), 2 minutes each | Record values. Expected: no measurable change (the filter is part of desktop composition). |
| M10 | Multiplayer server with anti-cheat | Optional; record whether anything is flagged (nothing is injected). |
| M11 | Load a Nether screenshot in Calibration step 4 | Previews of your profile and the Minecraft preset look as on screen. |
| M12 | Minecraft Bedrock (if available) | Repeat M2–M7. |

Please report: Windows version/build, GPU and driver, monitor(s), HDR on/off, Minecraft edition/version,
windowed/fullscreen, and the diagnostics export.
