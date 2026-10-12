# Entwickler-Zusammenfassung (Deutsch)

Kurzüberblick über Akeno ColorVision v0.1.0 für den Entwickler. Die App selbst, die Installations- und
Bedienungsanleitungen sind bewusst auf Englisch (für die Nutzerin in den USA).

## Was gebaut wurde

* **Echter systemweiter Farbfilter** über die Windows Magnification API (`MagSetFullscreenColorEffect`).
  Kein transparentes Overlay: Windows (DWM) wendet eine 5×5-Farbmatrix auf den komplett zusammengesetzten
  Desktop an — Browser, Videos, Discord, Spiele im Fenster-/Randlos-Modus.
* **Farbwissenschaft** (eigene Bibliothek, plattformunabhängig, getestet): Machado-2009-Simulationsmodell
  (Tabellen gegen colour-science gegengeprüft), Daltonisierung nach Fidaner 2005, Rot-Grün-Gegenfarb-Transfer
  auf Blau/Gelb und Helligkeit, Primärfarben-Hue-Remapping, Sättigung/Kontrast/Helligkeit, CIEDE2000 (mit den
  Sharma-Testdaten verifiziert), OKLab-Farbbenennung, Sicherheitsvalidator.
* **Presets** (Red-Green, Protan, Deutan, Strong Separation, Brightness & Contrast, Minecraft Nether) wurden mit
  einem eigenen Simulations-Tool (`tools/Akeno.ColorVision.PresetLab`) für protan/deutan-Beobachter getunt.
  Beispiel: Rot-Grün-Assistenz erhöht die simulierte mittlere Farbdifferenz der Verwechslungspaare von 10,1 auf
  22,8 ΔE00; harte Paare 9 → 2 von 18. Das sind Modellwerte, keine Messung an einer echten Person.
* **WPF-App** mit Home, Color Filters (echte Regler), Calibration-Wizard (5 Schritte, keine Diagnose),
  Gaming (Minecraft-Autoerkennung nur über Exe-Name + Fenstertitel, keine Injection), Color Identifier
  (Hotkey, Live-Modus), Profile (Import/Export, versioniertes JSON mit Migration), Settings, About/Diagnose,
  Tray-Icon, Dark/Light-Theme, globale Hotkeys mit Konflikterkennung.
* **Sicherheit/Recovery:** Notfall-Hotkey `Strg+Alt+Umschalt+Rücktaste` auf eigenem Thread (funktioniert auch,
  wenn die UI hängt), „Reset colors“-Button, separates `AkenoColorVision-EmergencyReset.exe`, Crash-Watchdog,
  Safe Mode nach unsauberem Beenden, 15-Sekunden-„Farben behalten?“-Dialog bei importierten Profilen,
  Validator lehnt gefährliche Matrizen ab (z. B. alles schwarz).
* **Build/Release:** GitHub Actions auf `windows-latest`: Build, Unit- und Integrationstests, portables ZIP
  (self-contained, keine .NET-Installation nötig) und Inno-Setup-Installer (pro Benutzer, ohne Admin-Rechte).
  Releases per Tag `v*` oder manuellem Workflow-Start.

## Was verifiziert ist (und was nicht)

| Stufe | Status |
|---|---|
| Kompiliert | ✅ Linux (Cross-Build) und Windows-CI |
| Unit-Tests | ✅ 269 bestanden |
| Integrationstests auf echtem Windows (CI-Runner) | ✅ 18 bestanden — inkl. Magnification-API-Roundtrip und **Nachweis, dass Windows unsere Matrix pixelgenau auf das zusammengesetzte Desktopbild anwendet** (Testfenster #C83C32 → #323CC8 mit Rot/Blau-Tausch, nach Reset wieder #C83C32) |
| Visuell auf einem echten Windows-11-Monitor geprüft | ❌ noch nicht |
| In Minecraft getestet | ❌ noch nicht |
| Code-signiert | ❌ kein Zertifikat → SmartScreen-Warnung beim ersten Start |

Durch die Windows-CI wurden zwei echte Bugs gefunden und behoben: Exceptions im Message-Pump-Thread kamen als
`AggregateException` an, und `Toggle()` war nicht atomar (gleichzeitiges Umschalten per Hotkey und Tray hätte
beide „einschalten“ lassen).

## Bewusste Entscheidungen

* **Nur das Magnification-Backend wird ausgeliefert.** Das GPU-Capture+Shader-Backend wurde evaluiert
  (Latenz ≥ 1 Frame, Klick-durch-Overlay, hilft bei Exclusive-Fullscreen auch nicht, ohne Hardware nicht
  validierbar) und erscheint in der Diagnose ehrlich als „Unsupported — not included in this version“.
* **Nur affine Matrizen:** Mehr kann die API nicht (keine LUTs, keine Kurven, ein Filter für alle Monitore).
  Jeder Regler entspricht einer echten Matrixoperation.
* **Fremde Farbeffekte** (z. B. Lupe mit invertierten Farben) werden erkannt und gemeldet, aber beim Beenden
  nicht blind wiederhergestellt, damit nie ein „verwaister“ verzerrter Desktop zurückbleibt.
* **Keine Diagnose:** Die Kalibrierung speichert nur die eigenen Antworten der Nutzerin und ihre Wahl.

## Bekannte Grenzen

* Exclusive-Fullscreen-Spiele können den Filter umgehen → Fenster-/Randlos-Modus (Minecraft Java: F11).
* Ein Filter für alle Monitore; HDR kann Farben anders wirken lassen; Windows-eigene Farbfilter addieren sich.
* Sehr dunkle Szenen (Nether) bleiben mit einer linearen Matrix schwieriger; das Minecraft-Preset nutzt daher
  zusätzlich Kontrast/Helligkeit.
* Was Windows mit dem Effekt bei einem harten Prozessabbruch macht, ist nicht dokumentiert — Watchdog und
  Reset-Tool decken beide Fälle ab; ein Neustart setzt immer zurück.

## Nächste Schritte (empfohlen)

1. Manuelle Checklisten in `docs/TESTING.md` auf ihrem PC durchgehen (Desktop D1–D27, Minecraft M1–M12) und
   eine Diagnose-Datei exportieren.
2. Kalibrierung mit ihr gemeinsam machen; Feedback zu den Presets sammeln (besonders Nether).
3. Release taggen (`v0.1.0`), sobald die Checkliste grün ist; optional Code-Signing (z. B. Azure Trusted Signing).
4. Danach: GPU-Shader-Backend mit LUT für nichtlineare Korrekturen (nur im Fenster-/Randlos-Modus sinnvoll),
   Pro-Spiel-Profile, ARM64-Build.

## Wichtige Pfade

* Code: `src/` (ColorScience → Core → Windows → Rendering → App; Recovery eigenständig)
* Tests: `tests/` · Preset-Tool: `tools/Akeno.ColorVision.PresetLab`
* Doku: `README.md`, `docs/USER-GUIDE.md`, `docs/TECHNICAL-ARCHITECTURE.md`, `docs/COLOR-SCIENCE.md`,
  `docs/COMPATIBILITY.md`, `docs/TESTING.md`, `docs/RELEASE.md`
* Nutzerdaten: `%LOCALAPPDATA%\AkenoColorVision` (Profile, Einstellungen, Logs)
