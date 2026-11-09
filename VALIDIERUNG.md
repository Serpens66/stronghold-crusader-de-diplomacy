# Prüfnachweise

## Regulärer Entwicklungsstand mit APIShared 0.43.0

Der reguläre Build wurde mit APIShared 0.43.0 geprüft. In einem Einzelspieler-
Gefechtsstand wurde eine aktive Waffenruhe über den normalen Save-Pfad
gespeichert und in einem frischen Prozess wiederhergestellt. Mit
BugfixesAndQoL, SerpsMods und Fixes löste ein Rechtsklick auf eine von
Vanilla erkannte Zieleinheit genau einen Vertragsbruch aus. Der native
Originalaufruf und der Angriffsbefehl der ausgewählten Einheit bestätigten
dieselbe Zieleinheit.

BugfixesAndQoL wurde zusätzlich mit natürlichen Raubtiergruppen geprüft.
Während der globalen Friedenszeit blieb der beobachtete Nahkampfschaden aus.
Nach Ablauf bestätigte der native Schadenspfad einen Treffer mit
Gesundheitsverlust. Beide Ergebnisse wurden mit und ohne Fixes beobachtet.

## Historische Prüfnachweise zur Vorabversion

## Build

Beide Profile wurden offline aus 196 C#-Dateien mit null Fehlern und null Warnungen gebaut. Der Starter verwendet den privaten Einzelspieler-Build mit gepinnter APIShared-Entwicklungsfassung. Originalspielstände und APIShared-Einstellungen wurden im isolierten Profil nachgewiesen.

Die Veröffentlichung `2.3.11-preview.1` enthält dieselbe geprüfte private DLL.
Veröffentlichbare Zusammenfassungen der Warenhandels-, Hilfs- und offenen
Militärnachweise liegen unter `evidence`. Originalspielstände, persönliche
Profile und rohe lokale Laufzeitprotokolle sind nicht Teil des Downloads.

| Merkmal | Wert |
| --- | --- |
| Quellenindex SHA-256 | `377f21483de32884191a1727c66d0e72890aae0609a2edfd6a3125ff89cd6fe7` |
| Geprüfte private DLL SHA-256 | `4f0bacef7f634c5986b005c3f83296445b35a52f86f90572c56c993d70d5b4a2` |
| Modulkennung | `8709ce84-96f3-4a47-8172-3afcd523fa4d` |
| Release-DLL SHA-256 | `24e0bd4adc6e31d66e618cd4b83aecc3c4b8b24aed081b4a2ea4ac1f851122fb` |

## Referenzlauf für Warenhandel und KI-Hilfe

Der kombinierte Lauf am Vorgängerstand verwendete vier vollständig beendete Originalprozesse mit derselben privaten DLL `9cfd3e5baa0ddde1c2c5850ca506729141e62c24c00a563871347f1ba5e15160`. Die Prüfung lief per Unity-Batchmodus und Code. Der Spiellauf injizierte keine Eingabeereignisse.

| Vorgang | Native Bestände vor und nach der Buchung | Bestände beim Kaltladen | Prozessfolge | Erneute Mod-Buchungen |
| --- | --- | --- | --- | --- |
| Warenhandel | 89/0/187/31 → 84/5/183/35 | 84/5/183/35 | 11960 → 24332 | 0 |
| KI-Hilfe | 72/30 → 69/33 | 69/33 | 24580 → 25780 | 0 |

Beim KI-Hilfefall wurden nach Wiederaufnahme der Originalsimulation zusätzlich Tickfortschritt, Buchungszähler, authentifizierter Verlauf und Gefälligkeiten geprüft. Die danach beobachteten Bestände waren 74/32. Der verwendete archivierte Gefechtsstand hat einen byteidentischen nativen Spielteil. Für diesen Testfall waren ältere Mod-Einträge in der Archivkopie bereits bereinigt.

Die Spielstandsprüfsummen und die Prüfsummen der Rohprotokolle liegen in `runtime/checks/runs/verified/run_result.json`.

## Kaltladen von KI-Hilfe im aktuellen Build

KI 2 lieferte drei Waren an KI 3. Die nativen Bestände änderten sich bei der Buchung von 77/30 auf 74/33. Beim Originalspeichern und beim ersten Lesen im neuen Prozess betrugen sie 74/32. Nach 350 weiteren Originalticks standen sie bei 73/32. Der authentifizierte Verlauf blieb erhalten. Die Mod führte nach dem Laden null erneute Buchungen aus. Die getrennten Prozesse hatten die PIDs 25596 und 24152.

Der ursprüngliche Auswerter verglich den ersten Kaltladebestand mit dem Stand direkt nach der Buchung. Der korrigierte Auswerter bindet den Kaltladevergleich an die versiegelte Erwartung zum Originalspielstand. Die erneute Auswertung verwendet dieselben Spielbelege. Sie steht in `runtime/checks/runs/request-schema-cold/reaudit.json` mit SHA-256 `82074efcedd153f6c57eb5bc09fde921b0d04641c67e0b49dff15d171ed63a6f`.

## Fürstenchat

Das Original-HUD hat den Chat im laufenden Gefecht gebunden. 35 isolierte C#-Fälle prüfen Direktadressen, Teamzugehörigkeit, ausgeschiedene Fürsten, Sprachvarianten und Antworten aus dem erfassten Diplomatieverlauf. `@team` prüft die bestätigte Spielteamzuordnung gegen den aktuellen Spielzustand. Die Antwortstimmung berücksichtigt zusätzlich Bündnis und Beziehung.

## Freigaben und Aufräumen

Schema 24 speichert policyseitige Ablehnungen nativer Warenanfragen mit unbekanntem Erzeuger. Der Mod prüft die KI-Rolle im Originalregister und sperrt den Originaleffekt im bestätigten Einzelspieler vor der Warenbuchung. Die 11 isolierten C#-Prüfungen decken unter anderem Deduplizierung, das Lesen des Verlaufs, Ablehnung vor Weiterleitung und das Verwerfen gefälschter Annahme ab.

Der native Warenhandler und der militärische Chore-Handler sind für die gepinnte Originalfassung nachgewiesen. Die untersuchten Originalpfade für das Anlegen der Anfragen gehen vom aktiven Originalspieler aus. Ein unabhängiger automatischer Original-KI-Erzeuger ist noch nicht belegt. Der Mod kennzeichnet eigene Anfragen und Beobachtungen mit unbekannter Herkunft separat.

Der private Militärpfad benötigt eine bestätigte Einzelspielerbesetzung, eine frische KI-Notlage, eine geeignete verbündete KI, ausreichende Reserve und freie Originalpriorität. Der Original-Evaluator entscheidet auf dem verifizierten Simulationsthread. Der erste Lauf nach der Threadkorrektur beobachtete 1.571 Simulationsticks ohne geeigneten autonomen Versand. Ein separater, explizit ausgelöster Transportlauf erreichte den Originalhandler einmal von KI 2 zu KI 3. Dabei blieb die native Annahme aus. Diese Läufe liegen in `runtime/checks/runs/autonomous-military-bound` und `runtime/checks/runs/military-transport-and-cold`.

Der aktuelle Build bestätigte denselben Original-Thread und die native Warteschlange erneut. In 4.212 Simulationsticks erfüllte kein Spender die Bedingungen des kontrollierten positiven Transportfalls. Der Bericht liegt in `runtime/checks/runs/military-native-readiness-cold/result.json` mit SHA-256 `f3d13a41c06f1a1ef9ed2a4ac9fab0d93b05a3f52b50c143b2fbecff2c9b2332`.

Die normale Militärfreigabe, der Endnachweis für einen natürlich ausgelösten KI-Versand und die Zweispielerprüfung auf Steam stehen aus.

## Waffenruhe

Der zusätzliche native Zielwahlfilter meldete im aktuellen Originalspiellauf
`visible_truce|status=unavailable|reason=image_unverified`. Seine gespeicherte
Engine-Prüfsumme stimmt nicht mit der aktuellen Engine überein. Die übrigen
Diplomatie- und Kampfschutzregeln erteilen diesem Filter keine Ersatzfreigabe.
Eine vollständige Waffenruhe über sämtliche originalen Kampf- und Bewegungswege
ist damit noch nicht abgenommen.

4 geprüfte leere Altordner wurden entfernt. Frühere Stände mit eigenen Belegen, Quellen, Spielständen oder aktiven Worktrees sind erhalten. Der Löschbeleg liegt in `runtime/cleanup-receipt.json`.
