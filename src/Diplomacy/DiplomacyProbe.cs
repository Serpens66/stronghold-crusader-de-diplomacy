using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SHCDESE.API;
using SHCDESE.EventAPI.Tribes;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;

namespace StrongholdEuropeDE.Runtime
{
    /// <summary>
    /// Phase 0 des Waffenstillstands-Ausbaus: die fuenf offenen Fragen im
    /// laufenden Spiel beantworten, bevor der Entwurf darauf gebaut wird.
    /// </summary>
    /// <remarks>
    /// Gemessen werden soll (Auftrag vom ):
    /// <list type="number">
    /// <item>Welches Raster beschreibt "Territorium" brauchbar - der
    /// Mauerbesitz (<c>GetTilePlayerOwnerId</c>, seit 1.39) oder die
    /// KI-Bauanspruchsmaske (<c>GetAIVBlockData</c>, erst ab SE 1.45)?</item>
    /// <item>Bleibt eine geschriebene <c>TribeStance</c> stehen, oder
    /// ueberschreibt die native KI sie?</item>
    /// <item>Wirkt <c>AttackNearestUnit=false</c>, oder wird es
    /// zurueckgesetzt?</item>
    /// <item>Wie schnell erfasst ein Stamm nach dem Loeschen seines Ziels
    /// ein neues?</item>
    /// <item>Nimmt die KI einen UMGELENKTEN Marschbefehl an, oder wiederholt
    /// sie ihn? (Heute weisen wir ab; Umlenken waere "aus dem Weg gehen".)
    /// </item>
    /// </list>
    ///
    /// Die Sonde schreibt in nativen Zustand - anders sind 2 bis 5 nicht zu
    /// beantworten. Deshalb gilt hier dieselbe Strenge wie beim Teamwechsel:
    /// <list type="bullet">
    /// <item>ein Versuch zur Zeit, ein Stamm zur Zeit, festes Fenster;</item>
    /// <item>jeder Schreibvorgang steht vorher im Journal
    /// (<see cref = "ProbeSubject"/>) und wird daraus exakt
    /// zurueckgenommen;</item>
    /// <item>Ruecknahme auch bei Vertragsende, Kartenabbau, Abschaltung und
    /// Plugin-Ruecknahme - kein Stamm bleibt befriedet zurueck;</item>
    /// <item>die Sonde wirft nie; ein Fehler schaltet sie ab, nicht die
    /// Karte.</item>
    /// </list>
    ///
    /// Nach einem Durchlauf je Karte schaltet sie sich selbst ab
    /// (<see cref = "ProbeStage.Done"/>).
    /// </remarks>
    internal sealed partial class DiplomacyFeature
    {
        private enum ProbeStage
        {
            Idle = 0,
            Territory = 1,
            StanceHold = 2,
            NoAutoTarget = 3,
            ClearTarget = 4,
            RedirectMove = 5,
            Done = 6,
        }

        /// <summary>Was an einem Stamm veraendert wurde - die Rueckgabe.</summary>
        private sealed class ProbeSubject
        {
            internal int TribeId;
            internal int Owner;
            internal bool StanceWritten;
            internal TribeStance OriginalStance;
            internal bool AttackNearestWritten;
            internal bool OriginalAttackNearest;
            internal bool TargetCleared;
            internal ushort OriginalTargetUnitId;
            internal uint OriginalTargetOwner;
        }

        // Ein Versuch laeuft 192 Takte (rund sechs Sekunden bei der nativen
        // Taktrate) und wird bei 1, 2, 4, 8, 16, 32, 64, 128, 192 abgelesen.
        private const int ProbeWindowTicks = 192;
        private static readonly int[] ProbeSampleOffsets =
        {
            1,
            2,
            4,
            8,
            16,
            32,
            64,
            128,
            192,
        };
        private const int ProbeCooldownTicks = 64;
        private const int ProbeMaxLogLines = 120;
        private ProbeStage _probeStage = ProbeStage.Idle;
        private ProbeStage _probePendingStage = ProbeStage.Territory;
        private ProbeSubject _probeSubject;
        private long _probeStartedTick = long.MinValue;
        private long _probeNextStageTick = long.MinValue;
        private int _probeSampleIndex;
        private int _probeLogLines;
        private bool _probeDegraded;
        private bool _probeTerritoryLogged;
        // Wie oft die laufende Stufe nicht anwendbar war. Nach drei Versuchen
        // wird sie uebersprungen - ein Versuchsplan, der nicht endet, ist
        // keiner (96 Versuche an no_auto_target am selben Stamm).
        private int _probeStageAttempts;
        private const int ProbeMaxStageAttempts = 3;
        private string _probeSeries;
        // Umlenkversuch: was der Marschbefehlshaken beobachtet hat.
        private int _probeMoveOrdersSeen;
        private int _probeMoveRedirected;
        private int _probeMoveRepeated;
        private readonly Dictionary<int, long> _probeMoveLastTick = new Dictionary<int, long>();
        /// <summary>
        /// Schalter der Phase-0-Sonde.
        /// </summary>
        /// <remarks>
        /// Am abgeschaltet. Die Gebietsvermessung (Frage 1, rein
        /// lesend) hat geliefert: der KI-Bauanspruch gibt je Spieler eine
        /// zusammenhaengende Flaeche (724 bis 2220 Kacheln), der Mauerbesitz
        /// dagegen 0 bis 982 mit ausgefransten Grenzen - ein Spieler hatte
        /// gar keine. Fuer "Territorium" taugt also der AIV-Anspruch.
        ///
        /// Am Vormittag des abgeschaltet, weil der Absturz
        /// unmittelbar auf ihren ersten Schreibvorgang (stance_hold) folgte
        /// und die Startzeile fehlte - sie stand hinter dem Schreibvorgang.
        ///
        /// **Am Nachmittag des wieder eingeschaltet**, auf
        /// ausdruecklichen Auftrag des Benutzers und mit widerlegter
        /// Verdachtslage:
        /// <list type="bullet">
        /// <item>Die Absturzursache liegt im Extender - der gemeinsame Stub
        /// aller 76 Kontexthaken rechnet den Stapel seit 2.0.0 nicht mehr
        /// exakt zurueck. Nachgewiesen mit NUR dem Extender im Pluginordner,
        /// ohne jeden Fremdmod.</item>
        /// <item>Der Extender laeuft jetzt auf 1.45.0, wo das nicht
        /// auftritt; ein Lauf ueber siebzehn Minuten blieb stabil.</item>
        /// <item>Die Sonde lief damals im Simulationstakt, also im rohen
        /// Kontexthaken. Heute laeuft sie im Unity-Bild.</item>
        /// <item>Die Startzeile steht jetzt VOR dem Schreibvorgang
        /// (`status=about_to_write`).</item>
        /// </list>
        ///
        /// Sie beantwortet die offene Frage zu den Kampfanimationen unter
        /// Waffenruhe: welcher Schreibzugriff haelt die KI wirklich ab, und
        /// haelt er.
        /// </remarks>
         // Production audit this experiment rewrites native tribe
        // state and must not run automatically in ordinary treaty gameplay.
        // The read-only runtime counters remain available independently.
        private const bool ProbeEnabled = false;
        private bool ProbeRedirectActive
        {
            get
            {
                return _probeStage == ProbeStage.RedirectMove && !_probeDegraded;
            }
        }

        /// <summary>
        /// Je Takt. Tut nichts, solange kein Versuch laeuft; startet den
        /// naechsten erst, wenn ein Pakt steht und der vorige zurueckgenommen
        /// ist.
        /// </summary>
        private void TickProbe()
        {
            if (!ProbeEnabled || _probePendingStage == ProbeStage.Done || _probeDegraded)
            {
                return;
            }

            try
            {
                if (!StandoffEnforceable())
                {
                    // Ohne laufenden Pakt gibt es keinen Vertragspartner, an
                    // dem sich etwas messen liesse. Angefangenes zurueckgeben
                    // und den Versuch spaeter neu beginnen.
                    if (_probeStage != ProbeStage.Idle)
                    {
                        RestoreProbeSubject("no_treaty");
                        _probeStage = ProbeStage.Idle;
                        _probeNextStageTick = ReadSimulationTick() + ProbeCooldownTicks;
                    }

                    return;
                }

                if (_probeStage == ProbeStage.Idle)
                {
                    if (ReadSimulationTick() < _probeNextStageTick)
                    {
                        return;
                    }

                    BeginProbeStage(_probePendingStage);
                    return;
                }

                if (_probeStage == ProbeStage.Territory)
                {
                    // Rein lesend, in einem Zug erledigt.
                    ProbeTerritory();
                    FinishProbeStage();
                    return;
                }

                SampleProbeStage();
            }
            catch (Exception exception)
            {
                DegradeProbe("tick", exception);
            }
        }

        private void BeginProbeStage(ProbeStage stage)
        {
            _probeStage = stage;
            _probeStartedTick = ReadSimulationTick();
            _probeSampleIndex = 0;
            _probeSeries = null;
            if (stage == ProbeStage.Territory)
            {
                return;
            }

            if (stage == ProbeStage.RedirectMove)
            {
                _probeMoveOrdersSeen = 0;
                _probeMoveRedirected = 0;
                _probeMoveRepeated = 0;
                _probeMoveLastTick.Clear();
                LogProbe("stage=redirect_move|status=started");
                return;
            }

            ProbeSubject subject = SelectProbeSubject();
            if (subject == null)
            {
                // Kein geeigneter Stamm gerade - spaeter erneut versuchen,
                // ohne den Versuchsplan zu verbrauchen.
                _probeStage = ProbeStage.Idle;
                _probeNextStageTick = ReadSimulationTick() + ProbeCooldownTicks;
                return;
            }

            _probeSubject = subject;
            _probeStageAttempts++;
            // Diese Zeile steht VOR dem Schreibvorgang. Stirbt das Spiel
            // darin, benennt das Protokoll trotzdem die Stufe und den Stamm -
            // beim Versuch am Vormittag des fehlte genau das, weil
            // die Startzeile dahinter stand. Nur beim ERSTEN Versuch je Stufe:
            // sie ist eine Absturzmarke, keine Dauermeldung.
            if (_probeStageAttempts == 1)
            {
                LogProbe("stage=" + ProbeStageName(stage) + "|status=about_to_write|tribe=" + subject.TribeId.ToString(CultureInfo.InvariantCulture) + "|owner=" + subject.Owner.ToString(CultureInfo.InvariantCulture));
            }

            if (!ApplyProbeWrite(stage, subject))
            {
                RestoreProbeSubject("write_failed");
                if (_probeStageAttempts >= ProbeMaxStageAttempts)
                {
                    // Dreimal nicht anwendbar - das ist ein Ergebnis, kein
                    // Grund zum Weiterversuchen. Der Zustand, den diese Stufe
                    // aendern wollte, liegt offenbar gar nicht vor.
                    LogProbe("stage=" + ProbeStageName(stage) + "|status=skipped|reason=not_applicable|attempts=" + _probeStageAttempts.ToString(CultureInfo.InvariantCulture));
                    _probePendingStage = NextProbeStage(stage);
                    if (_probePendingStage == ProbeStage.Done)
                    {
                        LogProbe("stage=all|status=complete");
                    }

                    _probeStageAttempts = 0;
                }

                _probeStage = ProbeStage.Idle;
                _probeNextStageTick = ReadSimulationTick() + ProbeCooldownTicks;
                return;
            }

            _probeStageAttempts = 0;
            LogProbe("stage=" + ProbeStageName(stage) + "|status=started|tribe=" + subject.TribeId.ToString(CultureInfo.InvariantCulture) + "|owner=" + subject.Owner.ToString(CultureInfo.InvariantCulture) + "|stance=" + subject.OriginalStance + "|attack_nearest=" + BoolText(subject.OriginalAttackNearest) + "|target_unit=" + subject.OriginalTargetUnitId.ToString(CultureInfo.InvariantCulture) + "|target_owner=" + subject.OriginalTargetOwner.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Der eigentliche Schreibvorgang, nach dem Journal.</summary>
        private unsafe bool ApplyProbeWrite(ProbeStage stage, ProbeSubject subject)
        {
            GameTribeManagerAPI tribes = GameTribeManagerAPI.Instance;
            if (tribes == null || !tribes.IsValidId(subject.TribeId))
            {
                return false;
            }

            switch (stage)
            {
                case ProbeStage.StanceHold:
                    if (subject.OriginalStance == TribeStance.Hold)
                    {
                        // Ohne Aenderung misst der Versuch nichts.
                        return false;
                    }

                    if (!tribes.SetStance(subject.TribeId, TribeStance.Hold))
                    {
                        return false;
                    }

                    subject.StanceWritten = true;
                    return true;
                case ProbeStage.NoAutoTarget:
                    if (!subject.OriginalAttackNearest)
                    {
                        return false;
                    }

                    if (!tribes.SetAttackNearestUnit(subject.TribeId, false))
                    {
                        return false;
                    }

                    subject.AttackNearestWritten = true;
                    return true;
                case ProbeStage.ClearTarget:
                    if (subject.OriginalTargetUnitId == 0 && subject.OriginalTargetOwner == 0)
                    {
                        return false;
                    }

                    GameTribe* tribe;
                    if (!tribes.TryGetTribeById(subject.TribeId, out tribe) || tribe == null)
                    {
                        return false;
                    }

                    tribe->r_AttackTargetUnitId = 0;
                    tribe->r_AttackTargetOwnerPlayerId = 0u;
                    subject.TargetCleared = true;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Liest den geschriebenen Wert an den geplanten Stellen zurueck und
        /// haengt ihn an die Messreihe. Am Fensterende: zurueckgeben.
        /// </summary>
        private unsafe void SampleProbeStage()
        {
            long elapsed = ReadSimulationTick() - _probeStartedTick;
            if (_probeStage == ProbeStage.RedirectMove)
            {
                // Dieser Versuch hat keinen einzelnen Stamm als Gegenstand -
                // er zaehlt, was der Marschbefehlshaken im Fenster sieht.
                if (elapsed >= ProbeWindowTicks)
                {
                    FinishProbeStage();
                }

                return;
            }

            ProbeSubject subject = _probeSubject;
            if (subject == null)
            {
                FinishProbeStage();
                return;
            }

            GameTribeManagerAPI tribes = GameTribeManagerAPI.Instance;
            if (tribes == null || !tribes.IsValidId(subject.TribeId))
            {
                // Der Stamm ist gefallen oder aufgeloest - nichts mehr
                // zurueckzunehmen, aber die Reihe endet hier.
                AppendProbeSample(elapsed, "gone");
                subject.StanceWritten = false;
                subject.AttackNearestWritten = false;
                subject.TargetCleared = false;
                FinishProbeStage();
                return;
            }

            while (_probeSampleIndex < ProbeSampleOffsets.Length && elapsed >= ProbeSampleOffsets[_probeSampleIndex])
            {
                int offset = ProbeSampleOffsets[_probeSampleIndex];
                _probeSampleIndex++;
                string value;
                switch (_probeStage)
                {
                    case ProbeStage.StanceHold:
                        value = tribes.GetStance(subject.TribeId).ToString();
                        break;
                    case ProbeStage.NoAutoTarget:
                        value = BoolText(tribes.GetAttackNearestUnit(subject.TribeId));
                        break;
                    case ProbeStage.ClearTarget:
                        GameTribe* tribe;
                        if (tribes.TryGetTribeById(subject.TribeId, out tribe) && tribe != null)
                        {
                            value = tribe->r_AttackTargetUnitId.ToString(CultureInfo.InvariantCulture) + "/" + tribe->r_AttackTargetOwnerPlayerId.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            value = "gone";
                        }

                        break;
                    default:
                        value = "?";
                        break;
                }

                AppendProbeSample(offset, value);
            }

            if (elapsed >= ProbeWindowTicks)
            {
                FinishProbeStage();
            }
        }

        private void AppendProbeSample(long offset, string value)
        {
            string entry = offset.ToString(CultureInfo.InvariantCulture) + ":" + value;
            _probeSeries = string.IsNullOrEmpty(_probeSeries) ? entry : _probeSeries + "," + entry;
        }

        /// <summary>Versuch beenden: Ergebnis melden, zurueckgeben, weiter.</summary>
        private void FinishProbeStage()
        {
            ProbeStage stage = _probeStage;
            if (stage == ProbeStage.RedirectMove)
            {
                LogProbe("stage=redirect_move|status=done|orders=" + _probeMoveOrdersSeen.ToString(CultureInfo.InvariantCulture) + "|redirected=" + _probeMoveRedirected.ToString(CultureInfo.InvariantCulture) + "|repeated=" + _probeMoveRepeated.ToString(CultureInfo.InvariantCulture));
            }
            else if (stage != ProbeStage.Territory && _probeSubject != null)
            {
                LogProbe("stage=" + ProbeStageName(stage) + "|status=done|tribe=" + _probeSubject.TribeId.ToString(CultureInfo.InvariantCulture) + "|series=" + (_probeSeries ?? "none"));
            }

            RestoreProbeSubject("window_end");
            _probeSeries = null;
            // Der naechste Versuch beginnt erst nach der Abkuehlzeit; bis
            // dahin ist an keinem Stamm etwas geschrieben.
            _probePendingStage = NextProbeStage(stage);
            _probeStage = ProbeStage.Idle;
            _probeNextStageTick = ReadSimulationTick() + ProbeCooldownTicks;
            if (_probePendingStage == ProbeStage.Done)
            {
                LogProbe("stage=all|status=complete");
            }
        }

        private static ProbeStage NextProbeStage(ProbeStage stage)
        {
            switch (stage)
            {
                case ProbeStage.Territory:
                    return ProbeStage.StanceHold;
                case ProbeStage.StanceHold:
                    return ProbeStage.NoAutoTarget;
                case ProbeStage.NoAutoTarget:
                    return ProbeStage.ClearTarget;
                case ProbeStage.ClearTarget:
                    return ProbeStage.RedirectMove;
                default:
                    return ProbeStage.Done;
            }
        }

        private static string ProbeStageName(ProbeStage stage)
        {
            switch (stage)
            {
                case ProbeStage.Territory:
                    return "territory";
                case ProbeStage.StanceHold:
                    return "stance_hold";
                case ProbeStage.NoAutoTarget:
                    return "no_auto_target";
                case ProbeStage.ClearTarget:
                    return "clear_target";
                case ProbeStage.RedirectMove:
                    return "redirect_move";
                case ProbeStage.Done:
                    return "done";
                default:
                    return "idle";
            }
        }

        /// <summary>
        /// Nimmt jeden Schreibvorgang exakt zurueck. Wird auch bei
        /// Vertragsende, Kartenabbau und Abschaltung gerufen - deshalb muss
        /// sie mehrfach aufrufbar und ohne laufenden Versuch harmlos sein.
        /// </summary>
        private unsafe void RestoreProbeSubject(string reason)
        {
            ProbeSubject subject = _probeSubject;
            _probeSubject = null;
            if (subject == null)
            {
                return;
            }

            int restored = 0;
            try
            {
                GameTribeManagerAPI tribes = GameTribeManagerAPI.Instance;
                if (tribes != null && tribes.IsValidId(subject.TribeId))
                {
                    if (subject.StanceWritten && tribes.SetStance(subject.TribeId, subject.OriginalStance))
                    {
                        restored++;
                    }

                    if (subject.AttackNearestWritten && tribes.SetAttackNearestUnit(subject.TribeId, subject.OriginalAttackNearest))
                    {
                        restored++;
                    }

                    if (subject.TargetCleared)
                    {
                        GameTribe* tribe;
                        if (tribes.TryGetTribeById(subject.TribeId, out tribe) && tribe != null)
                        {
                            // Nur zuruecksetzen, wenn die KI in der
                            // Zwischenzeit nichts Eigenes eingetragen hat -
                            // ein neues Ziel ist ihr Zustand, nicht unserer.
                            if (tribe->r_AttackTargetUnitId == 0 && tribe->r_AttackTargetOwnerPlayerId == 0u)
                            {
                                tribe->r_AttackTargetUnitId = subject.OriginalTargetUnitId;
                                tribe->r_AttackTargetOwnerPlayerId = subject.OriginalTargetOwner;
                            }

                            restored++;
                        }
                    }
                }

                bool wroteAnything = subject.StanceWritten || subject.AttackNearestWritten || subject.TargetCleared;
                if (wroteAnything)
                {
                    LogProbe("stage=restore|tribe=" + subject.TribeId.ToString(CultureInfo.InvariantCulture) + "|restored=" + restored.ToString(CultureInfo.InvariantCulture) + "|reason=" + Safe(reason));
                }
            }
            catch (Exception exception)
            {
                DegradeProbe("restore", exception);
            }
        }

        /// <summary>
        /// Ein Stamm des Vertragspartners mit Kampffuehrer. Bevorzugt einer,
        /// der in der Zone des Spielerblocks steht - dort entscheidet sich,
        /// ob das Abstandsgebot spaeter greift.
        /// </summary>
        private unsafe ProbeSubject SelectProbeSubject()
        {
            GameTribeManagerAPI tribes = GameTribeManagerAPI.Instance;
            GameUnitManagerAPI units = GameUnitManagerAPI.Instance;
            if (tribes == null || units == null)
            {
                return null;
            }

            var tribeIds = new List<int>();
            tribes.GetAllTribes(tribeIds, AliveState.IsAlive);
            tribeIds.Sort();
            ProbeSubject fallback = null;
            foreach (int tribeId in tribeIds)
            {
                int owner = TribeOwner(tribeId);
                bool localBloc;
                if (owner <= 0 || !TryGetTreatySide(owner, out localBloc) || localBloc)
                {
                    continue;
                }

                int leader = tribes.GetLeaderUnitId(tribeId);
                if (leader <= 0 || !units.IsValidId(leader) || !IsCombatUnit(units.GetType(leader)))
                {
                    continue;
                }

                GameTribe* tribe;
                if (!tribes.TryGetTribeById(tribeId, out tribe) || tribe == null)
                {
                    continue;
                }

                var candidate = new ProbeSubject
                {
                    TribeId = tribeId,
                    Owner = owner,
                    OriginalStance = tribes.GetStance(tribeId),
                    OriginalAttackNearest = tribes.GetAttackNearestUnit(tribeId),
                    OriginalTargetUnitId = tribe->r_AttackTargetUnitId,
                    OriginalTargetOwner = tribe->r_AttackTargetOwnerPlayerId,
                };
                UnmanagedVector2<ushort> position = units.GetCurrentWorldTilePosition(leader);
                if (FindOpposingTreatyZone(localBloc, position.X, position.Y) != null)
                {
                    return candidate;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }
            }

            return fallback;
        }

        /// <summary>
        /// Frage 1, rein lesend: welches Raster beschreibt Territorium? Es
        /// wird jede zweite Kachel abgetastet - fuer Deckungsgrad und
        /// Ausdehnung genau genug, und ein Viertel der Arbeit.
        /// </summary>
        private void ProbeTerritory()
        {
            if (_probeTerritoryLogged)
            {
                return;
            }

            _probeTerritoryLogged = true;
            GameTileManagerAPI tiles = GameTileManagerAPI.Instance;
            int mapSize = GameMap.tilemapSize;
            if (tiles == null || mapSize <= 0)
            {
                LogProbe("stage=territory|status=unavailable");
                return;
            }

            bool aivAvailable = ProbeAivReady();
            var wallCounts = new Dictionary<int, int>();
            var aivCounts = new Dictionary<int, int>();
            var wallBounds = new Dictionary<int, int[]>();
            int sampled = 0;
            for (int tileY = 0; tileY < mapSize; tileY += 2)
            {
                for (int tileX = 0; tileX < mapSize; tileX += 2)
                {
                    int tileId = tiles.GetTileId(tileX, tileY);
                    if (tileId < 0)
                    {
                        continue;
                    }

                    sampled++;
                    int wallOwner = tiles.GetTilePlayerOwnerId(tileId);
                    if (wallOwner > 0 && wallOwner <= 8)
                    {
                        int previous;
                        wallCounts.TryGetValue(wallOwner, out previous);
                        wallCounts[wallOwner] = previous + 1;
                        int[] box;
                        if (!wallBounds.TryGetValue(wallOwner, out box))
                        {
                            box = new[]
                            {
                                tileX,
                                tileY,
                                tileX,
                                tileY
                            };
                            wallBounds[wallOwner] = box;
                        }

                        box[0] = Math.Min(box[0], tileX);
                        box[1] = Math.Min(box[1], tileY);
                        box[2] = Math.Max(box[2], tileX);
                        box[3] = Math.Max(box[3], tileY);
                    }

                    if (!aivAvailable)
                    {
                        continue;
                    }

                    int mask = ProbeAivMask(tileId);
                    for (int player = 0; player < 8; player++)
                    {
                        if ((mask & (1 << player)) == 0)
                        {
                            continue;
                        }

                        int previous;
                        aivCounts.TryGetValue(player, out previous);
                        aivCounts[player] = previous + 1;
                    }
                }
            }

            LogProbe("stage=territory|status=scanned|map=" + mapSize.ToString(CultureInfo.InvariantCulture) + "|sampled=" + sampled.ToString(CultureInfo.InvariantCulture) + "|aiv_api=" + BoolText(aivAvailable));
            foreach (int player in _roster.OrderBy(value => value))
            {
                int wall;
                wallCounts.TryGetValue(player, out wall);
                int aiv;
                aivCounts.TryGetValue(player, out aiv);
                int[] box;
                string bounds = wallBounds.TryGetValue(player, out box) ? box[0].ToString(CultureInfo.InvariantCulture) + "," + box[1].ToString(CultureInfo.InvariantCulture) + ".." + box[2].ToString(CultureInfo.InvariantCulture) + "," + box[3].ToString(CultureInfo.InvariantCulture) : "none";
                LogProbe("stage=territory|player=" + player.ToString(CultureInfo.InvariantCulture) + "|wall_tiles=" + wall.ToString(CultureInfo.InvariantCulture) + "|wall_bounds=" + bounds + "|aiv_tiles=" + aiv.ToString(CultureInfo.InvariantCulture) + "|alive=" + BoolText(IsAlivePlayer(player)));
            }
        }

        // GetAIVBlockData gibt es erst ab SE 1.45. Die Diplomatie laeuft ab
        // 1.39 - deshalb ueber Reflexion, und ohne den Zugang faellt nur
        // dieser Teil der Messung aus.
        private static bool _probeAivResolved;
        private static MethodInfo _probeAivMethod;
        private bool ProbeAivReady()
        {
            if (!_probeAivResolved)
            {
                _probeAivResolved = true;
                try
                {
                    _probeAivMethod = typeof(GameTileManagerAPI).GetMethod("GetAIVBlockData", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                }
                catch (Exception)
                {
                    _probeAivMethod = null;
                }
            }

            return _probeAivMethod != null;
        }

        private int ProbeAivMask(int tileId)
        {
            try
            {
                object value = _probeAivMethod.Invoke(GameTileManagerAPI.Instance, new object[] { tileId });
                return value == null ? 0 : Convert.ToInt32(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Frage 5: Der Marschbefehlshaken meldet der Sonde, was er sieht.
        /// Waehrend des Umlenkversuchs wird der Befehl nicht abgewiesen,
        /// sondern auf einen Punkt ausserhalb der Zone umgeschrieben.
        /// </summary>
        private bool ProbeRedirectMoveOrder(TribeIssueOrderMoveHereEventArgs e, TreatyZone zone, bool localBloc)
        {
            if (!ProbeEnabled || !ProbeRedirectActive)
            {
                return false;
            }

            try
            {
                _probeMoveOrdersSeen++;
                long lastTick;
                if (_probeMoveLastTick.TryGetValue(e.TribeId, out lastTick) && ReadSimulationTick() - lastTick <= ProbeWindowTicks)
                {
                    _probeMoveRepeated++;
                }

                _probeMoveLastTick[e.TribeId] = ReadSimulationTick();
                int scale = DiplomacyCombatRules.WorldPositionsPerLocalTile;
                int radius = DiplomacyCombatRules.IntimateZoneRadiusLocalTiles;
                int centerX = zone.X / scale;
                int centerY = zone.Y / scale;
                int deltaX = e.TileX - centerX;
                int deltaY = e.TileY - centerY;
                if (deltaX == 0 && deltaY == 0)
                {
                    deltaX = 1;
                }

                // Auf den Zonenrand hinausschieben, Richtung beibehalten -
                // der Stamm bekommt ein erreichbares Ziel statt einer Absage.
                double length = Math.Sqrt((double)deltaX * deltaX + (double)deltaY * deltaY);
                double factor = (radius + 3) / Math.Max(1.0, length);
                int targetX = centerX + (int)Math.Round(deltaX * factor);
                int targetY = centerY + (int)Math.Round(deltaY * factor);
                // Native tile coordinates occupy the fixed 800-grid diamond,
                // including on small maps. Never clamp to the map's width.
                if (!GameTileManagerAPI.Instance.IsTileInsideMapBounds(targetX, targetY))
                    return false;
                if (FindOpposingTreatyZone(localBloc, DiplomacyCombatRules.TileCenterWorld(targetX), DiplomacyCombatRules.TileCenterWorld(targetY)) != null)
                {
                    // Der Rand liegt selbst noch in einer Zone - dann bleibt
                    // es bei der Absage des Abstandsgebots.
                    return false;
                }

                if (_probeLogLines < ProbeMaxLogLines)
                {
                    LogProbe("stage=redirect_move|tribe=" + e.TribeId.ToString(CultureInfo.InvariantCulture) + "|from=" + e.TileX.ToString(CultureInfo.InvariantCulture) + "," + e.TileY.ToString(CultureInfo.InvariantCulture) + "|to=" + targetX.ToString(CultureInfo.InvariantCulture) + "," + targetY.ToString(CultureInfo.InvariantCulture));
                }

                e.TileX = targetX;
                e.TileY = targetY;
                _probeMoveRedirected++;
                return true;
            }
            catch (Exception exception)
            {
                DegradeProbe("redirect", exception);
                return false;
            }
        }

        private void LogProbe(string fields)
        {
            if (_probeLogLines >= ProbeMaxLogLines)
            {
                return;
            }

            _probeLogLines++;
            Log("treaty_probe", fields);
        }

        private void DegradeProbe(string stage, Exception exception)
        {
            _probeDegraded = true;
            try
            {
                RestoreProbeSubject("degraded");
            }
            catch (Exception)
            {
            // Die Ruecknahme selbst darf nichts weiterreichen.
            }

            _probeStage = ProbeStage.Done;
            Log("treaty_probe", "stage=" + Safe(stage) + "|status=degraded|reason=" + Safe(exception.GetType().Name + ":" + exception.Message));
        }

        /// <summary>Kartenwechsel: alles zurueck und von vorn.</summary>
        private void ResetProbeState()
        {
            RestoreProbeSubject("map_reset");
            _probeStage = ProbeStage.Idle;
            _probePendingStage = ProbeStage.Territory;
            _probeStartedTick = long.MinValue;
            _probeNextStageTick = long.MinValue;
            _probeSampleIndex = 0;
            _probeLogLines = 0;
            _probeDegraded = false;
            _probeTerritoryLogged = false;
            _probeSeries = null;
            _probeMoveOrdersSeen = 0;
            _probeMoveRedirected = 0;
            _probeMoveRepeated = 0;
            _probeMoveLastTick.Clear();
        }
    }
}
