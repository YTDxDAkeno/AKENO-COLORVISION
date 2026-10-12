#!/usr/bin/env bash
# Builds the portable Windows x64 release from Linux/macOS (cross-publish).
# Usage: scripts/package.sh [version]
set -euo pipefail

VERSION="${1:-0.1.0}"
RUNTIME="win-x64"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIST="$ROOT/dist"
STAGE="$DIST/Akeno-ColorVision"
ZIP_NAME="Akeno-ColorVision-v$VERSION-$RUNTIME.zip"

rm -rf "$DIST"
mkdir -p "$STAGE"

dotnet publish "$ROOT/src/Akeno.ColorVision.App/Akeno.ColorVision.App.csproj" \
  -c Release -r "$RUNTIME" --self-contained true -p:Version="$VERSION" -o "$STAGE"
dotnet publish "$ROOT/src/Akeno.ColorVision.Recovery/Akeno.ColorVision.Recovery.csproj" \
  -c Release -r "$RUNTIME" --self-contained true -p:Version="$VERSION" -o "$STAGE"

find "$STAGE" -maxdepth 1 -type f \( -name '*.pdb' -o -name '*.xml' -o -name '*.json' \) -delete
cp "$ROOT/docs/QUICK-START.txt" "$STAGE/READ ME FIRST.txt"
cp "$ROOT/LICENSE" "$STAGE/LICENSE.txt"
cp "$ROOT/CHANGELOG.md" "$STAGE/CHANGELOG.md"

for f in AkenoColorVision.exe AkenoColorVision-EmergencyReset.exe; do
  [ -f "$STAGE/$f" ] || { echo "Missing $f" >&2; exit 1; }
done

(cd "$DIST" && python3 -c "import shutil; shutil.make_archive('${ZIP_NAME%.zip}', 'zip', '.', 'Akeno-ColorVision')")
(cd "$DIST" && sha256sum "$ZIP_NAME" > "$ZIP_NAME.sha256")
ls -la "$STAGE" "$DIST"
