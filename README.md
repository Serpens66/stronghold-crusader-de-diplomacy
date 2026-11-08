# Stronghold Crusader Diplomatie

Dieser Entwicklungsstand bündelt Diplomatie, Warenhandel, KI-Hilfe, Befehle an Verbündete und den Fürstenchat für Stronghold Crusader Definitive Edition.

Der vollständige Quellcode liegt direkt im Zweig `main`. Der Ordner `src`
enthält 196 C#-Dateien; `assets`, `references`, `tools` und `evidence` ergänzen
Oberflächen-Patches, Abhängigkeitsprüfsummen, Werkzeuge und Spielnachweise.
Der Einstieg für Beiträge steht in [CONTRIBUTING.md](CONTRIBUTING.md).

```powershell
git clone https://github.com/strongholdoriginsmod-rgb/stronghold-crusader-de-diplomacy.git
cd stronghold-crusader-de-diplomacy
```

Die veröffentlichte Fassung `2.3.11-preview.1` ist eine Einzelspieler-Vorabversion.
Funktionsumfang, tatsächliche Spielnachweise, offene Freigaben und die Einrichtung
des vorgebauten Downloads stehen in [RELEASE_NOTES.md](RELEASE_NOTES.md).
Der autonome KI-Militärsender ist beim normalen Spielstart ausgeschaltet.
Vor dem ersten Start des Downloads muss die getrennte Spielkopie gemäß der
Anleitung in den Release-Notizen eingerichtet werden.

## Spielen

Doppelklick auf `Diplomatie starten.cmd`. Der Starter prüft Spielkopie, Abhängigkeiten und Mod-Dateien. Spiel und Spielstände liegen in `runtime/game` und `runtime/profile`. Starte im Spiel ein lokales Gefecht.

Handelsangebote und registrierte Warenanfragen laufen über die Diplomatieoberfläche. Der Fürstenchat verwendet das Chatfeld links unten. Mit `@2 Nachricht` oder `@Fürstenname Nachricht` sprichst du einen KI-Fürsten direkt an. `@team Nachricht` erreicht die lebenden KI-Fürsten im eigenen bestätigten Spielteam. Ein diplomatisches Bündnis beeinflusst die Antworten. Verträge, Warenanfragen und militärische Befehle werden über die Spieloberfläche erteilt.

Der lokale Spielbuild verwendet den geprüften APIShared-Entwicklungsstand. Der autonome KI-Verteidigungsversand ist im isolierten Headless-Prüfmodus verfügbar. Der native Mehrspielerhandel steht zur gesonderten Zweispielerprüfung an.

## Entwickeln

| Bereich | Aufgabe |
| --- | --- |
| `src/Diplomacy`, `src/Ui` | Beziehungen, Diplomatieoberfläche, Anfragen und Fürstenchat |
| `src/Trade`, `src/Economy`, `src/Aid` | Handel, Bestandsbuchungen und Hilfe zwischen Verbündeten |
| `src/Engine`, `src/World`, `src/Military` | Spielschnittstellen, Spielerrollen und militärische Befehle |
| `src/Persistence`, `src/Networking` | Original-Spielstände, Zustimmungsprotokoll und Host-Autorität |
| `src/Testing`, `src/Qa` | Isolierte, codegesteuerte Prüfung mit dem Originalspiel |

`tools/local_game.py` verwaltet die lokale Spielkopie. `prepare --help` zeigt die benötigten Eingaben für eine neue Kopie. Der Ordner `references` enthält die Prüfsummen der referenzierten Binärdateien.

```powershell
py -3 -B tools/local_game.py index
py -3 -B tools/local_game.py build
py -3 -B tools/local_game.py install
py -3 -B tools/local_game.py check
```

`index` aktualisiert nach einer Quellcodeänderung die Liste der C#-Dateien samt Prüfsummen. Der Offline-Build verwendet diese Liste und die beiden Abhängigkeitssätze aus `references`. `install` aktualisiert den lokalen Spielbuild. `tools/acceptance.py --help` beschreibt den headless Test mit separat gestarteten Originalprozessen.

Der aktuelle Mod-Spielstand verwendet Schema 24. Der Leser unterstützt die bisherigen Spielstände ab Schema 2. Lokale Spiel-, Profil-, Build- und Prüfdateien liegen in den ignorierten Ordnern `runtime` und `build`.

Die verifizierten Spielabläufe und die offenen Freigaben stehen in [VALIDIERUNG.md](VALIDIERUNG.md).
