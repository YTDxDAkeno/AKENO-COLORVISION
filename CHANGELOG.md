# Changelog

All notable changes to Akeno ColorVision are documented here. The project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] — first usable release (unreleased)

### Added
- System-wide color filter using the Windows Magnification API
  (`MagSetFullscreenColorEffect`) on a dedicated thread, with read-back verification,
  smooth profile transitions and automatic re-apply after sleep, unlock and display changes.
- Correction engine: red-green opponent transfer (to blue/yellow and lightness),
  protan- and deutan-oriented daltonization (Machado et al. 2009 + Fidaner et al. 2005),
  primary hue remapping, saturation, contrast and brightness — all as validated affine matrices.
- Built-in presets: Red-Green Assistance, Protan Assistance, Deutan Assistance,
  Strong Separation, Brightness & Contrast, Minecraft — Nether Visibility. Tuned with a
  simulation-based evaluation tool (PresetLab).
- Guided calibration wizard (self-description, pair ratings, comparison rounds,
  Nether-inspired test scene / own screenshot, save) that never claims a diagnosis.
- Custom profiles with real sliders, import/export, versioned JSON format with migration.
- Minecraft gaming mode with optional automatic profile switching (Java and Bedrock)
  based only on executable name and window title.
- Color identifier (shortcut, 3-second sample, live mode) with a runtime self-check that
  measures whether screen sampling returns original or filtered colors.
- Safety: transform validator, emergency reset shortcut on an independent thread,
  stand-alone emergency reset tool, crash watchdog, safe mode after unclean exits,
  15-second "keep these colors?" confirmation for imported profiles.
- System tray, global shortcuts with conflict detection, start with Windows (opt-in),
  dark and light themes, local-only logging and diagnostics export.
- Portable self-contained Windows x64 ZIP and optional per-user installer (Inno Setup),
  built by GitHub Actions.

### Known limitations
- Real-world visual behaviour, in-game behaviour (Minecraft Java/Bedrock) and the UI have
  not yet been verified on physical Windows 11 hardware for this version.
- One color matrix for all monitors; no non-linear (LUT) corrections; exclusive-fullscreen
  games may bypass the filter. The GPU capture backend was evaluated but is not included.
- Executables are not code-signed (SmartScreen warning on first start).
