# Entwickeln und beitragen

Der Zweig `main` enthält den vollständigen Quellstand der Vorabversion
2.3.11-preview.1. Die C#-Dateien, Oberflächen-Patches und Build-Werkzeuge
stimmen mit dem veröffentlichten Source-Archiv überein. README und diese
Entwickleranleitung ergänzen den direkten Einstieg in das Repository.

## Orientierung

| Verzeichnis | Inhalt |
| --- | --- |
| `src/Diplomacy`, `src/Ui` | Beziehungen, Verträge, Anfragen, Diplomatiefenster und Fürstenchat |
| `src/Trade`, `src/Economy`, `src/Aid` | Warenhandel, Bedarfsplanung und KI-Warenhilfe |
| `src/Engine`, `src/Host`, `src/World` | Spielschnittstellen, Rollen und Autorität |
| `src/Military` | Menschliche Verbündetenbefehle und begrenzter autonomer Prüfpfad |
| `src/Persistence`, `src/Networking` | Speichern, Wiederherstellen und Zustimmung |
| `src/Testing`, `src/Qa`, `src/Diagnostics` | Codegesteuerte Prüfung und Diagnose |
| `assets` | Mod-Metadaten, Konfiguration und XAML-Patches |
| `references` | Abhängigkeitsprüfsummen; keine fremden Binärdateien |
| `evidence` | Veröffentlichbare Zusammenfassungen vorhandener Spielnachweise |

## Lokale Entwicklungsumgebung

Python 3.11 oder neuer, ein passendes .NET-SDK, .NET-Framework-4.8.1-
Referenzassemblies und eine eigene kompatible Spielinstallation werden benötigt.
Die exakten Abhängigkeiten sind in `references/qa.json`, `references/release.json`
und `references/preview-runtime.json` festgehalten. APIShared muss zum gewählten
Build-Profil passen; die private Spielintegration verwendet den gepinnten
Entwicklungsstand.

```powershell
py -3 -B tools/local_game.py prepare --help
py -3 -B build.py --help
```

`prepare` verlangt ausdrücklich die Vorlage der Spielkopie, die Entwicklungs-
und öffentliche APIShared-Fassung, das SDK und die Framework-Referenzen. Die
vorhandene Spielinstallation wird dabei nur gelesen. Nach Einrichtung der
getrennten Kopie erfolgt der Build mit:

```powershell
py -3 -B tools/local_game.py index
py -3 -B tools/local_game.py build
py -3 -B tools/local_game.py install
py -3 -B tools/local_game.py check
```

`source-index.json` legt die ausgewählten C#-Dateien und deren Prüfsummen fest.
Nach Änderungen wird der Index vor dem Build erneuert. `build.py` baut mit
expliziten Abhängigkeiten und installiert oder startet selbst kein Spiel.

## Änderungen prüfen

Spielprüfungen erfolgen headless und per Code. `tools/acceptance.py --help`
beschreibt die vorhandenen Abläufe mit getrennten Originalprozessen. Bei
Warenbuchungen gehören reale Bestände, Originalspeichern, vollständiges
Prozessende, Kaltladen und der Ausschluss einer erneuten Buchung zum Nachweis.

Eine bestandene statische Prüfung ersetzt keinen Spielnachweis. Normale native
Freigaben und Mehrspielerautorität dürfen nicht durch UI-Zustimmung, Testflags
oder erfundene Spielerrollen ersetzt werden. Der autonome KI-Militärsender
bleibt beim normalen Start ausgeschaltet. Offene Punkte stehen in
[RELEASE_NOTES.md](RELEASE_NOTES.md) und [VALIDIERUNG.md](VALIDIERUNG.md).

Spielinstallationen, Spielstände, private Profile, Laufzeitprotokolle,
Zugangsdaten und absolute persönliche Pfade gehören nicht in Beiträge.
`runtime`, `build`, Python-Caches und lokale Build-Dateien sind entsprechend
von der veröffentlichten Quellstruktur getrennt.
