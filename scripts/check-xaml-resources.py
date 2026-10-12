"""Static check that every {StaticResource}/{DynamicResource} key used in XAML (and in C# via
FindResource/SetResourceReference) is defined somewhere. Missing keys would only fail at runtime
(XamlParseException), so this catches them at build time.

Usage: python scripts/check-xaml-resources.py
"""
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent / "src" / "Akeno.ColorVision.App"
defined, used = set(), []
for path in root.rglob("*.xaml"):
    text = path.read_text(encoding="utf-8")
    defined.update(re.findall(r'x:Key="([^"]+)"', text))
    for kind, key in re.findall(r'\{(StaticResource|DynamicResource)\s+([^}\s]+)\}', text):
        used.append((path.name, kind, key))
for path in root.rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    for key in re.findall(r'(?:FindResource|SetResourceReference\([^,]+,)\s*\(?\s*"([^"]+)"', text):
        used.append((path.name, "code", key))

missing = [(f, k, key) for f, k, key in used if not key.startswith("{x:Type") and key not in defined]
print(f"{len(defined)} keys defined, {len(used)} references checked.")
for f, k, key in missing:
    print(f"MISSING: {key} ({k}) in {f}")
sys.exit(1 if missing else 0)
