# Release process

## What gets built

| File | Contents |
|---|---|
| `Akeno-ColorVision-v<version>-win-x64.zip` | Portable folder `Akeno-ColorVision/` with `AkenoColorVision.exe` (self-contained single file, ~70 MB), `AkenoColorVision-EmergencyReset.exe` (trimmed single file, ~11 MB), `READ ME FIRST.txt`, `LICENSE.txt`, `CHANGELOG.md`. |
| `…zip.sha256` | SHA-256 checksum of the ZIP. |
| `Akeno-ColorVision-Setup-v<version>.exe` | Optional per-user installer (Inno Setup, no admin rights): Start-menu shortcuts for the app, *Emergency Reset* and *Safe mode*; optional desktop shortcuts; uninstall runs the reset tool first. |

The recipient needs nothing else installed — no .NET runtime, Visual Studio, Python or Node.js.

## Continuous integration

`.github/workflows/build.yml` runs on `windows-latest` for every push to `main` and `claude/**`, for pull
requests, for `v*` tags and on manual dispatch:

1. restore → Release build of the whole solution (version from the tag, otherwise `<VersionPrefix>-ci.<run>`)
2. XAML resource check (`scripts/check-xaml-resources.py`)
3. unit tests, then integration tests against the real Windows APIs (TRX results uploaded)
4. preset evaluation table (PresetLab)
5. `scripts/package.ps1` → portable ZIP + checksum
6. Inno Setup installer (best effort; installed via Chocolatey if missing; a failure here does not block the ZIP)
7. artifacts uploaded: ZIP, installer, test results

CI artifacts can be downloaded from the run page by anyone with access to the repository (GitHub login
required; artifacts expire after the repository's retention period, 90 days by default).

## Making a release

1. Update `CHANGELOG.md` and, if needed, `<VersionPrefix>` in `Directory.Build.props`.
2. Merge to `main`.
3. Tag and push: `git tag v0.1.0 && git push origin v0.1.0`.
4. The workflow builds, tests and publishes a GitHub Release with the ZIP, checksum and installer, using
   `docs/RELEASE-NOTES.md` as the description (versions with a `-suffix` are marked as pre-release).

Alternatively run the workflow manually (**Actions → Build and test (Windows) → Run workflow**) with
*"Also publish this build as a GitHub pre-release"* checked.

## Local builds

```
pwsh scripts/package.ps1 -Version 0.1.0          # on Windows
./scripts/package.sh 0.1.0                       # cross-build from Linux/macOS (.NET 10 SDK)
iscc /DAppVersion=0.1.0 installer\AkenoColorVision.iss   # optional installer (Windows, Inno Setup 6)
```

## Code signing and SmartScreen

The executables are **not code-signed** (no certificate is available to this project). Consequences:

* Windows SmartScreen shows *"Windows protected your PC"* on first start. Users click **More info → Run
  anyway**. This is expected and the release notes say so.
* Some antivirus products are more suspicious of unsigned new executables. The app does nothing that should
  trigger heuristics (no injection, no drivers, no network), but false positives are possible.
* Never ask users to disable Defender or SmartScreen. Instead publish the SHA-256 checksum so downloads can
  be verified.

To sign in the future: obtain an OV/EV code-signing certificate (or use a service such as Azure Trusted
Signing), sign both executables and the installer with `signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256`
after `package.ps1`, and store the credentials as GitHub secrets. Signed builds build SmartScreen reputation
over time.

## Versioning

Semantic versioning. `0.x` while real-hardware verification is pending; `1.0.0` once the manual test
checklists in `TESTING.md` (desktop and Minecraft) have been completed on Windows 11 hardware.
