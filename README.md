# Stronghold Crusader Diplomatie

Einzelspieler-Vorabversion **2.3.11-preview.1** für Stronghold Crusader Definitive Edition, Stand 8. November 2026.

[Release und Downloads](https://github.com/strongholdoriginsmod-rgb/stronghold-crusader-de-diplomacy/releases/tag/v2.3.11-preview.1) · [Funktionen, Installation und Grenzen](RELEASE_NOTES.md) · [Prüfnachweise](VALIDIERUNG.md)

Die Mod verbindet Diplomatie, Warenhandel mit 19 Warenarten, automatische Warenhilfe zwischen verbündeten KI-Fürsten, manuelle Verbündetenbefehle und einen lokalen Fürstenchat. Die Vorabversion ist noch keine vollständig abgenommene finale Mod. Der autonome KI-Militärsender ist beim normalen Start ausgeschaltet; nativer Mehrspielerhandel ist nicht freigegeben. Die bekannten Grenzen einschließlich Originalanfragen und Waffenruhe stehen in den Release-Notizen.

## Downloads

- **Windows.zip:** Vorgebaute Mod mit Einrichtung einer getrennten Spielkopie, Starter, Prüfsummen und Nachweisen. Die Einrichtung benötigt eine eigene kompatible Spielinstallation samt exakt gepinnten Entwicklungsabhängigkeiten.
- **Source.zip:** Vollständiger aktueller Quellcode mit 196 C#-Dateien, Oberflächen-Patches, Build-Werkzeugen, Abhängigkeitsprüfsummen und veröffentlichbaren Nachweisen. Dieses ausdrücklich benannte Source-Archiv enthält den vollständigen Entwicklungsstand. Die automatisch von GitHub erzeugten Source-code-Archive beziehen sich auf die Dokumentation dieses Repository-Snapshots.
- **SHA256SUMS.txt:** Prüfsummen der beiden Downloads.
- **RELEASE_MANIFEST.json:** Modulkennung, DLL-Prüfsumme, Quellstand und Freigabegrenzen.

## Einrichtung

Windows-Archiv außerhalb der vorhandenen Spielinstallation entpacken. Python 3.11 oder neuer wird benötigt. Anschließend im entpackten Ordner ausführen:

```powershell
py -3 -B tools/preview.py prepare --game-source "<eigener kompatibler Spielordner>"
```

Danach `Diplomatie starten.cmd` verwenden und ein lokales Gefecht starten. Die Quelle wird nur gelesen; Spielkopie und eigenes Profil entstehen unter `runtime/game` und `runtime/profile`. Originalspiel, fremde Mod-Binärdateien, persönliche Profile und Spielstände werden nicht mitverteilt.

Erforderlich sind SHCDE-SE 2.14.3, APIShared 0.25.0 im gepinnten Entwicklungsstand und Bugfixes and QoL 1.0.192. Die exakten Dateiprüfsummen stehen in `references/preview-runtime.json` innerhalb des Downloads. Bei einer abweichenden Abhängigkeit bricht die Einrichtung vor dem Kopieren ab.
