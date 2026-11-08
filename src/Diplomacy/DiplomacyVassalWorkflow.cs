using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // Presentation is session-local; the saved request date never restarts.
        // A restored decision cannot time out invisibly behind another popup.
        private readonly Dictionary<string, int> _equalityPresentedDays = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _submissionPresentedDays = new Dictionary<string, int>(StringComparer.Ordinal);
        private sealed class PendingFealtyCommand
        {
            internal long Generation;
            internal string Fingerprint;
            internal object Panel;
            internal int Player, Selection;
            internal bool Overview;
            internal Action Execute;
            internal Func<bool> StillCurrent;
            internal string DecisionKey;
            internal DiplomacyPanelCommandLifetime Lifetime;
            internal long QueuedTick;
        }

        // Ein Knopfdruck wartet hoechstens so lange auf einen freien Takt.
        // laenger warten heisst in der Praxis "fuer immer", denn
        // solange einer liegt, wird jeder weitere Druck verworfen.
        private const long FealtyCommandExpiryTicks = 600;
        private PendingFealtyCommand _pendingFealtyCommand;
        private readonly Queue<string> _fealtyAnswerOrder = new Queue<string>();
        private readonly Dictionary<string, PendingFealtyCommand> _fealtyAnswers = new Dictionary<string, PendingFealtyCommand>(StringComparer.Ordinal);
        private void CancelFealtyCommand()
        {
            PendingFealtyCommand command = _pendingFealtyCommand;
            _pendingFealtyCommand = null;
            if (command != null && command.Lifetime != null)
                command.Lifetime.Dispose();
        }

        private void QueueFealtyCommand(object panel, int player, Action execute, string decisionKey = null, Func<bool> stillCurrent = null)
        {
            // An explicit answer supersedes an unexecuted panel request. Repeated
            // panel clicks neither replace an intent nor generate another roll.
            if (panel != null && _pendingFealtyCommand != null)
                return;
            var command = new PendingFealtyCommand
            {
                Generation = _sessionGeneration,
                Fingerprint = _mapFingerprint,
                Panel = panel,
                Player = player,
                Overview = _overviewVisible,
                Selection = panel == null ? 0 : SelectedPlayer(panel),
                Execute = execute,
                DecisionKey = decisionKey,
                StillCurrent = stillCurrent,
                QueuedTick = _featureTicks,
                Lifetime = panel == null ? null : new DiplomacyPanelCommandLifetime(MainViewModel.Instance),
            };
            if (panel == null)
            {
                // One saved offer per coalition, at most seven counterparts.
                // Different rulers' accepted answers must never deduplicate each
                // other. A newer quote for the same ruler supersedes a stale one.
                if (string.IsNullOrEmpty(decisionKey))
                    throw new ArgumentException("decisionKey");
                CancelFealtyCommand();
                if (!_fealtyAnswers.ContainsKey(decisionKey))
                    _fealtyAnswerOrder.Enqueue(decisionKey);
                _fealtyAnswers[decisionKey] = command;
            }
            else
                _pendingFealtyCommand = command;
            PumpFealtyCommand();
        }

        private void RemoveFealtyCommand(PendingFealtyCommand command)
        {
            if (command.Panel != null)
                CancelFealtyCommand();
            else
            {
                _fealtyAnswerOrder.Dequeue();
                _fealtyAnswers.Remove(command.DecisionKey);
            }
        }

        private void PumpFealtyCommand()
        {
            PendingFealtyCommand command = _fealtyAnswerOrder.Count > 0 ? _fealtyAnswers[_fealtyAnswerOrder.Peek()] : _pendingFealtyCommand;
            if (command == null)
                return;
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || command.Generation != _sessionGeneration || !string.Equals(command.Fingerprint, _mapFingerprint, StringComparison.Ordinal) || command.StillCurrent != null && !command.StillCurrent() || command.Panel != null && (command.Lifetime.Cancelled || !ReferenceEquals(command.Panel, CurrentAlliesPanel()) || command.Overview != _overviewVisible || SelectedPlayer(command.Panel) != command.Selection || command.Overview && _allianceSelectedPlayer != command.Player))
            {
                RemoveFealtyCommand(command);
                return;
            }

            // Ein Knopfdruck, der auf einen belegten Motor trifft, blieb bis
            // zum unbegrenzt liegen und blockierte dabei jeden
            // weiteren Druck - im Spiellauf war "Unterwerfung fordern" nach
            // einer haengengebliebenen Guetersendung fuer den Rest der Partie
            // stumm. Eine gegebene Antwort (Panel == null) verfaellt NICHT:
            // eine Zusage bleibt gueltig (Benutzervorgabe ).
            if (command.Panel != null && _featureTicks - command.QueuedTick > FealtyCommandExpiryTicks)
            {
                Log("vassalage", "status=command_expired|player=" + command.Player.ToString(CultureInfo.InvariantCulture));
                RemoveFealtyCommand(command);
                return;
            }

            if (_inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                if (command.Lifetime != null && !command.Lifetime.TryClaim())
                {
                    RemoveFealtyCommand(command);
                    return;
                }

                RemoveFealtyCommand(command);
                // Model and native reads are protected; ShowMessage/Confirmation
                // only enqueue while this monitor is held. The frame presents later.
                command.Execute();
            }
        }

        private int TributeQuoteForPlayer(int vassalPlayer)
        {
            return DiplomacyVassalage.TributeAmountForEconomy(EconomicStrengthForPlayer(vassalPlayer));
        }

        private long EconomicStrengthForPlayer(int vassalPlayer)
        {
            RequireNativeDiplomacySection();
            GamePlayerManagerAPI players = GamePlayerManagerAPI.Instance;
            long economy = Math.Max(0, players.GetPlayerGold(vassalPlayer));
            foreach (int good in SendableGoods)
            {
                if (good == DiplomacyTribute.GoldGoodId)
                    continue;
                economy = SaturatingAdd(economy, SaturatingMultiply(Math.Max(0, players.GetGoodAmount(vassalPlayer, (eGoods)good)), _frozenPrices[good]));
            }

            economy = SaturatingAdd(economy, ProductionEconomy(new HashSet<int> { vassalPlayer }));
            return economy;
        }

        private void PumpTributeReassessments()
        {
            if (!_mapActive || _mapDisabled || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            int day = Math.Max(0, _lastProcessedDay);
            foreach (RelationState relation in _relations.Values)
            {
                DiplomacyRelationState state = relation.ExtendedState;
                if (state == null || !state.TributeReassessmentPending || !DiplomacyTribute.IsActive(state) || day >= state.TributeNextDueDay)
                    continue;
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return;
                using (lease)
                {
                    int payer = DiplomacyVassalage.VassalOf(state);
                    int recipient = DiplomacyVassalage.OverlordOf(state);
                    if (!IsAlivePlayer(payer) || !IsAlivePlayer(recipient))
                        continue;
                    int previousAmount = state.TributeAmount;
                    long economicStrength = EconomicStrengthForPlayer(payer);
                    if (!DiplomacyTribute.TryReassessNextPeriod(state, day, economicStrength))
                        continue;
                    Log("tribute", "coalition=" + relation.CoalitionId + "|status=reassessed|payer=" + payer.ToString(CultureInfo.InvariantCulture) + "|economy=" + economicStrength.ToString(CultureInfo.InvariantCulture) + "|previous_amount=" + previousAmount.ToString(CultureInfo.InvariantCulture) + "|amount=" + state.TributeAmount.ToString(CultureInfo.InvariantCulture) + "|due=" + state.TributeNextDueDay.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private void ResetSubmissionPresentation()
        {
            _submissionPresentedDays.Clear();
            CancelFealtyCommand();
            _fealtyAnswerOrder.Clear();
            _fealtyAnswers.Clear();
        }

        private bool TryLordIdentity(int player, out int lord)
        {
            lord = 0;
            if (player < 1 || player > 8 || !IsAlivePlayer(player))
                return false;
            try
            {
                // GetAILord reads the AI lineup. Player slots and legacy
                // PrimaryLordId are not evidence of a ruler's identity.
                lord = checked((int)GamePlayerManagerAPI.Instance.GetAILord(player));
                return lord > 0;
            }
            catch
            {
                return false;
            }
        }

        // Caller owns the engine monitor. Identity and contract eligibility are
        // separate from the military conditions for creating a NEW offer.
        private bool SubmissionStructurallyEligible(CoalitionState coalition, RelationState relation, int day, out int player, out int lord)
        {
            player = 0;
            lord = 0;
            if (coalition == null || relation == null || relation.ExtendedState == null || coalition.Allied || coalition.MemberPlayerIds == null || coalition.MemberPlayerIds.Length != 1 || !IsAlivePlayer(_localPlayer))
                return false;
            player = coalition.MemberPlayerIds[0];
            if (player == _localPlayer || !TryLordIdentity(player, out lord) || DiplomacySubmission.EligibilityBlocker(relation, day, lord, coalition.MemberPlayerIds.Length, CanJoinAlliance(coalition.CoalitionId)).HasValue)
                return false;
            return true;
        }

        // Called under the engine lease, before presenting or accepting a quote.
        // False also covers an incomplete observation: no timer or roll is spent.
        private bool ReviewSubmissionOffer(CoalitionState coalition, RelationState relation, int day)
        {
            if (!DiplomacySubmission.HasOffer(relation.ExtendedState))
                return false;
            if (relation.ExtendedState.SubmissionOfferAccepted)
                return true; // journal already started
            int player, lord;
            if (!SubmissionStructurallyEligible(coalition, relation, day, out player, out lord) || relation.ExtendedState.SubmissionOfferPlayerId != player || relation.ExtendedState.SubmissionOfferLordId != lord)
            {
                WithdrawSubmissionOffer(coalition, relation, day, "structure_changed");
                return false;
            }

            // Zwei verschiedene Lagen, die frueher gleich behandelt wurden:
            // 1. Die Messung ist NICHT VERFUEGBAR (Burgscan noch nicht fertig,
            //    Engine belegt). Dann wird nichts entschieden und nichts
            //    verbraucht - es wird aufgeschoben. Eine Unterwerfung darf nie
            //    auf fehlenden Daten vollzogen werden.
            // 2. Die Messung ist verfuegbar und der Druck ist WEG. Das aendert
            //    am laufenden Angebot nichts mehr (Benutzervorgabe ):
            //    es steht seine sieben Spieltage, auch wenn die Belagerung
            //    zusammenbricht, und dasselbe gilt fuer eine bereits gegebene
            //    Zusage. Erst am Fristende entscheidet der Druck wieder, ob
            //    nachgebessert oder zurueckgezogen wird - ElapseResponseDay.
            // Bis zum stand hier zusaetzlich eine Ruecknahme bei zu
            // schwachem Band. Sie mass in JEDEM Takt neu, obwohl der Druck nur
            // einmal je Spieltag aufgebaut wird: eine einzige schwankende
            // Messung nahm ein eben gemachtes Angebot zurueck und loeschte den
            // ueber Tage aufgebauten Druck. Im Spiel belegt mit
            // submission_offered seq=294 und submission_withdrawn seq=296.
            int band;
            if (!TrySubmissionPressureBand(player, lord, day, out band))
                return false;
            _submissionVerifiedQuotes[coalition.CoalitionId] = relation.ExtendedState.SubmissionOfferCreatedDay;
            return true;
        }

        private void WithdrawSubmissionOffer(CoalitionState coalition, RelationState relation, int day, string grund)
        {
            bool wasPresented = _submissionPresentedDays.ContainsKey(coalition.CoalitionId);
            DiplomacySubmission.CloseOffer(relation.ExtendedState, day, false);
            DiplomacySubmission.LosePressure(relation.ExtendedState, day);
            CancelDiplomacyDialog("submission:" + coalition.CoalitionId);
            _submissionPresentedDays.Remove(coalition.CoalitionId);
            _submissionVerifiedQuotes.Remove(coalition.CoalitionId);
            Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=submission_withdrawn|reason=" + Safe(grund) + "|pressure_days=" + relation.ExtendedState.SubmissionPressureDays.ToString(CultureInfo.InvariantCulture));
            if (wasPresented)
                ShowMessage(Text("Das Unterwerfungsangebot ist durch die veränderte Lage gegenstandslos geworden. Es entstand keine Lehnspflicht.", "The submission offer is no longer valid because the situation has changed. No fealty was established."));
        }

        private bool SubmissionTransitionCurrent(CoalitionState coalition, RelationState relation, int day)
        {
            int player, lord;
            return DiplomacySubmission.CanCommitOffer(relation) && SubmissionStructurallyEligible(coalition, relation, day, out player, out lord) && relation.ExtendedState.SubmissionOfferPlayerId == player && relation.ExtendedState.SubmissionOfferLordId == lord;
        }

        private void PumpSubmissionAssessments()
        {
            if (!_mapActive || _mapDisabled || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            NormalizeVassalDemands();
            int day = Math.Max(0, _lastProcessedDay);
            long tick = ReadSimulationTick();
            bool reviewQuotes = _submissionQuoteReviewTick == long.MinValue || tick - _submissionQuoteReviewTick >= 32;
            if (reviewQuotes)
                _submissionQuoteReviewTick = tick;
            foreach (CoalitionState coalition in _coalitions.Values.OrderBy(c => c.CoalitionId, StringComparer.Ordinal).ToArray())
            {
                RelationState relation;
                if (!_relations.TryGetValue(coalition.CoalitionId, out relation) || relation.ExtendedState == null)
                    continue;
                DiplomacyRelationState state = relation.ExtendedState;
                if (state.SubmissionAcceptancePending)
                {
                    AnswerSubmissionOffer(coalition.CoalitionId, _sessionGeneration, _mapFingerprint, state.SubmissionOfferPlayerId, state.SubmissionOfferLordId, state.SubmissionOfferTributeAmount, state.SubmissionOfferCreatedDay, state.SubmissionOfferOrigin, true);
                    // An unknown castle/read can defer this answer without
                    // starting native work. It must not starve later saved Yes
                    // decisions or their daily observations after a load.
                    if (_pendingTeamTransition != null || _nativeTransferProbe != null || _pendingPackage != null || _inventoryRestorePending)
                        return;
                    continue;
                }

                if (state.SubmissionLastComparedDay < day || reviewQuotes && DiplomacySubmission.HasOffer(state))
                    ProcessSubmissionOffer(coalition, relation, day);
            }
        }

        private void ProcessSubmissionOffer(CoalitionState coalition, RelationState relation, int day)
        {
            // Capture UI ownership before entering the native monitor. This
            // producer runs from the frame pump, so a busy engine is retried
            // during the same day rather than losing a pressure observation.
            int presentedDay;
            bool presented = _submissionPresentedDays.TryGetValue(coalition.CoalitionId, out presentedDay) && day > presentedDay && IsDiplomacyDialogPresented("submission:" + coalition.CoalitionId);
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
                ProcessSubmissionOfferUnderLease(coalition, relation, day, presented);
        }

        private void ProcessSubmissionOfferUnderLease(CoalitionState coalition, RelationState relation, int day, bool presented)
        {
            NormalizeProudLegacyVassal(coalition, relation);
            DiplomacyRelationState state = relation.ExtendedState;
            if (state == null || state.SubmissionOfferAccepted)
                return;
            int player, lord;
            bool eligible = SubmissionStructurallyEligible(coalition, relation, day, out player, out lord);
            bool open = DiplomacySubmission.HasOffer(state);
            if (open && !ReviewSubmissionOffer(coalition, relation, day))
                return;
            if (open)
                AskSubmissionOffer(coalition, relation);
            if (day <= state.SubmissionLastComparedDay)
                return;
            int band = 0;
            if (eligible && !TrySubmissionPressureBand(player, lord, day, out band))
                return;
            bool ready = DiplomacySubmission.ObservePressure(state, day, lord, band, eligible);
            if (open)
            {
                DiplomacySubmission.DiplomacyOfferDayResult ablauf = DiplomacySubmission.ElapseResponseDay(state, day, presented, _mapFingerprint, coalition.CoalitionId);
                if (ablauf == DiplomacySubmission.DiplomacyOfferDayResult.Improved)
                {
                    // Der Druck hielt ueber die Frist hinaus an: der Fuerst
                    // bessert nach. Der alte Dialog zeigt den alten Betrag,
                    // also wird er verworfen und neu vorgelegt.
                    _submissionPresentedDays.Remove(coalition.CoalitionId);
                    CancelDiplomacyDialog("submission:" + coalition.CoalitionId);
                    _submissionVerifiedQuotes[coalition.CoalitionId] = state.SubmissionOfferCreatedDay;
                    Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=submission_improved|tribute_gold=" + state.SubmissionOfferTributeAmount.ToString(CultureInfo.InvariantCulture) + "|pressure_days=" + state.SubmissionPressureDays.ToString(CultureInfo.InvariantCulture));
                    ShowMessage(Text("Der anhaltende Druck wirkt: das Unterwerfungsangebot wurde nachgebessert.", "The sustained pressure is working: the submission offer has been improved."));
                    AskSubmissionOffer(coalition, relation);
                }
                else if (ablauf == DiplomacySubmission.DiplomacyOfferDayResult.Withdrawn)
                {
                    _submissionPresentedDays.Remove(coalition.CoalitionId);
                    _submissionVerifiedQuotes.Remove(coalition.CoalitionId);
                    CancelDiplomacyDialog("submission:" + coalition.CoalitionId);
                    Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=submission_timed_out");
                    ShowMessage(Text("Das Unterwerfungsangebot ist vom Tisch. Der Krieg geht weiter.", "The submission offer is off the table. The war continues."));
                }

                return;
            }

            if (!ready || DiplomacySubmission.TryAttempt(relation, player, lord, TributeQuoteForPlayer(player), day, _mapFingerprint, DiplomacySubmissionOfferOrigin.AiOffer, band) != DiplomacySubmissionAttemptResult.Offered)
                return;
            PresentNewSubmissionOffer(coalition, relation);
        }

        private void PresentNewSubmissionOffer(CoalitionState coalition, RelationState relation)
        {
            DiplomacyRelationState state = relation.ExtendedState;
            _submissionVerifiedQuotes[coalition.CoalitionId] = state.SubmissionOfferCreatedDay;
            int player = state.SubmissionOfferPlayerId, lord = state.SubmissionOfferLordId;
            int day = state.SubmissionOfferCreatedDay;
            Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=submission_offered|player=" + player.ToString(CultureInfo.InvariantCulture) + "|lord=" + lord.ToString(CultureInfo.InvariantCulture) + "|day=" + day.ToString(CultureInfo.InvariantCulture) + "|origin=" + state.SubmissionOfferOrigin.ToString());
            OnDiplomacyPresentationCommitted(DiplomacyPresentationEvent.Create(DiplomacyPresentationEventKind.SubmissionOffered, _sessionGeneration, day, player, lord, _localPlayer, coalition.CoalitionId, state.SubmissionOfferOrigin == DiplomacySubmissionOfferOrigin.PlayerDemand ? "player_demand" : "military_pressure", new DiplomacyPresentationReference("submission_offer", startedDay: day, goodId: DiplomacyTribute.GoldGoodId, amount: state.SubmissionOfferTributeAmount, intervalDays: DiplomacyTribute.DefaultIntervalDays, otherPlayerId: _localPlayer)));
            AskSubmissionOffer(coalition, relation);
        }

        private bool SubmissionDecisionCurrent(string coalitionId, long generation, string fingerprint, int player, int lord, int amount, int createdDay, DiplomacySubmissionOfferOrigin origin, out RelationState relation)
        {
            relation = null;
            CoalitionState coalition;
            // Popup validity runs on the UI thread WITHOUT the native monitor.
            // Only read managed quote/session data here. The command pump checks
            // live identity, survival and team eligibility under the monitor.
            return _mapActive && !_mapDisabled && generation == _sessionGeneration && string.Equals(fingerprint, _mapFingerprint, StringComparison.Ordinal) && _relations.TryGetValue(coalitionId, out relation) && _coalitions.TryGetValue(coalitionId, out coalition) && DiplomacySubmission.HasOffer(relation.ExtendedState) && !relation.ExtendedState.SubmissionOfferAccepted && relation.ExtendedState.SubmissionOfferPlayerId == player && relation.ExtendedState.SubmissionOfferLordId == lord && relation.ExtendedState.SubmissionOfferTributeAmount == amount && relation.ExtendedState.SubmissionOfferCreatedDay == createdDay && relation.ExtendedState.SubmissionOfferOrigin == origin && relation.ExtendedState.SubmissionResponseDaysRemaining > 0 && !relation.Allied && !coalition.Allied && coalition.MemberPlayerIds != null && coalition.MemberPlayerIds.Length == 1 && coalition.MemberPlayerIds[0] == player;
        }

        private void AskSubmissionOffer(CoalitionState coalition, RelationState relation)
        {
            DiplomacyRelationState state = relation.ExtendedState;
            int verifiedQuote;
            if (!DiplomacySubmission.HasOffer(state) || state.SubmissionOfferAccepted || state.SubmissionAcceptancePending || state.SubmissionPressureRuleVersion != DiplomacySubmission.CurrentPressureRuleVersion || !_submissionVerifiedQuotes.TryGetValue(coalition.CoalitionId, out verifiedQuote) || verifiedQuote != state.SubmissionOfferCreatedDay)
                return;
            string id = coalition.CoalitionId;
            long generation = _sessionGeneration;
            string fingerprint = _mapFingerprint;
            int player = state.SubmissionOfferPlayerId, lord = state.SubmissionOfferLordId;
            int amount = state.SubmissionOfferTributeAmount;
            int createdDay = state.SubmissionOfferCreatedDay;
            DiplomacySubmissionOfferOrigin origin = state.SubmissionOfferOrigin;
            ShowConfirmation(PlayerName(player) + " · " + Text("Unterwerfungsangebot", "Offer of submission"), delegate
            {
                AnswerSubmissionOffer(id, generation, fingerprint, player, lord, amount, createdDay, origin, true);
            }, delegate
            {
                AnswerSubmissionOffer(id, generation, fingerprint, player, lord, amount, createdDay, origin, false);
            }, (origin == DiplomacySubmissionOfferOrigin.PlayerDemand ? Text("Forderung angenommen.", "Demand accepted.") : Text("Unterwerfung angeboten.", "Submission offered.")) + "\n" + Text("Bei Annahme: Vasall und Bündnis. Bei Ablehnung: weiterhin Krieg.", "Accept: vassal and alliance. Refuse: war continues.") + "\n" + FealtyTerms(amount) + "\n" + string.Format(CultureInfo.InvariantCulture, Text("Antwortfrist: {0} Spieltage.", "Response time: {0} game days."), state.SubmissionResponseDaysRemaining), delegate
            {
                if (!_submissionPresentedDays.ContainsKey(id))
                    _submissionPresentedDays[id] = Math.Max(0, _lastProcessedDay);
            }, "submission:" + id, delegate
            {
                RelationState current;
                return SubmissionDecisionCurrent(id, generation, fingerprint, player, lord, amount, createdDay, origin, out current);
            });
        }

        private void AnswerSubmissionOffer(string id, long generation, string fingerprint, int player, int lord, int amount, int createdDay, DiplomacySubmissionOfferOrigin origin, bool accepted)
        {
            lock (_gate)
            {
                RelationState relation;
                if (!SubmissionDecisionCurrent(id, generation, fingerprint, player, lord, amount, createdDay, origin, out relation))
                    return;
                if (!accepted)
                {
                    DiplomacySubmission.CloseOffer(relation.ExtendedState, Math.Max(0, _lastProcessedDay), true);
                    _submissionPresentedDays.Remove(id);
                    Log("vassalage", "coalition=" + id + "|status=submission_rejected");
                    OnDiplomacyPresentationCommitted(DiplomacyPresentationEvent.Create(DiplomacyPresentationEventKind.SubmissionDeclined, _sessionGeneration, Math.Max(0, _lastProcessedDay), player, lord, _localPlayer, id, "local_rejection", new DiplomacyPresentationReference("submission_offer", goodId: DiplomacyTribute.GoldGoodId, amount: amount, intervalDays: DiplomacyTribute.DefaultIntervalDays, otherPlayerId: _localPlayer)));
                    return;
                }

                if (!DiplomacySubmission.TryQueueAcceptance(relation))
                    return;
                QueueFealtyCommand(null, player, delegate
                {
                    RelationState current;
                    if (!SubmissionDecisionCurrent(id, generation, fingerprint, player, lord, amount, createdDay, origin, out current))
                        return;
                    if (!ReviewSubmissionOffer(_coalitions[id], current, Math.Max(0, _lastProcessedDay)))
                        return;
                    if (!DiplomacySubmission.TryAcceptOffer(current))
                        return;
                    try
                    {
                        FormAlliance(_coalitions[id], "submission_offer");
                    }
                    finally
                    {
                        DiplomacySubmission.RestoreUncommittedOffer(current, _pendingTeamTransition);
                    }
                }, "submission:" + id, delegate
                {
                    RelationState current;
                    return SubmissionDecisionCurrent(id, generation, fingerprint, player, lord, amount, createdDay, origin, out current);
                });
            }
        }

        private bool CommitPendingSubmission(CoalitionState coalition, RelationState relation, int day)
        {
            DiplomacyRelationState state = relation.ExtendedState;
            if (!DiplomacySubmission.HasOffer(state) || !state.SubmissionOfferAccepted)
                return false;
            int player = state.SubmissionOfferPlayerId, amount = state.SubmissionOfferTributeAmount;
            if (!DiplomacyVassalage.Establish(relation, player, _localPlayer, _localPlayer, day) || !DiplomacyTribute.Arrange(state, DiplomacyTribute.GoldGoodId, amount, DiplomacyTribute.DefaultIntervalDays, day))
                throw new InvalidOperationException("accepted submission could not establish its quoted contract");
            _submissionPresentedDays.Remove(coalition.CoalitionId);
            PublishCombatProtectionSnapshot();
            Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=submission_accepted|vassal=" + player.ToString(CultureInfo.InvariantCulture) + "|tribute_gold=" + amount.ToString(CultureInfo.InvariantCulture));
            ShowMessage(Text("Euer neuer Verbündeter hat Euch Lehnstreue geschworen und entrichtet Euch fortan Tribut.", "Your new ally has sworn fealty to you and will pay you tribute from now on."));
            return true;
        }

        private void NormalizeProudLegacyVassal(CoalitionState coalition, RelationState relation)
        {
            // A saved tribute journal must settle against its original contract
            // before that contract is converted. A later ordinary day retries.
            if (_inventoryRestorePending || _nativeTransferProbe != null || _pendingTeamTransition != null)
                return;
            if (!relation.Allied || !DiplomacyVassalage.IsActive(relation.ExtendedState))
                return;
            int player = DiplomacyVassalage.VassalOf(relation.ExtendedState), lord;
            if (player == _localPlayer || !TryLordIdentity(player, out lord) || !DiplomacySubmission.NeverSubmits(lord))
                return;
            DiplomacyVassalage.ClearWithoutRelease(relation.ExtendedState);
            DiplomacyAgreementRules.ResumeAllianceMaintenance(relation.ExtendedState, Math.Max(0, _lastProcessedDay));
            Log("vassalage", "coalition=" + coalition.CoalitionId + "|status=legacy_role_normalized|reason=lord_never_submits|lord=" + lord.ToString(CultureInfo.InvariantCulture));
            ShowMessage(Text("Eine frühere Lehnspflicht widersprach dem Wesen dieses Herrschers. Ihr seid nun gleichrangige Verbündete; Tribut und Lehnspflichten enden ohne Vertragsbruch.", "An earlier fealty contract contradicted this ruler's character. You are now equal allies; tribute and fealty end without a treaty breach."));
        }

        private string FealtyTerms(int amount)
        {
            return string.Format(CultureInfo.InvariantCulture, Text("Tribut: {0} {1} in {2} Tagen; danach je Zeitraum an die Wirtschaft angepasst. Bei Goldmangel werden gleichwertige Waren angenommen.", "Tribute: {0} {1} in {2} days; adjusted to the economy each following period. Equivalent goods are accepted if gold runs short."), amount, GoodName(DiplomacyTribute.GoldGoodId, amount), DiplomacyTribute.DefaultIntervalDays);
        }

        private string TributeReminder(DiplomacyRelationState state, bool localPays)
        {
            return string.Format(CultureInfo.InvariantCulture, Text("Tribut bald fällig: {0} {1} in {2} Tagen ({3}).", "Tribute due soon: {0} {1} in {2} days ({3})."), state.TributeAmount, GoodName(state.TributeGoodId, state.TributeAmount), DiplomacyTribute.WarningLeadDays, localPays ? Text("Ihr zahlt", "You pay") : Text("Ihr erhaltet", "You receive"));
        }

        private string TributeGraceText(DiplomacyRelationState state, int day)
        {
            if (state.TributeMissedCycles > 0)
                return string.Format(CultureInfo.InvariantCulture, Text("Tribut im Rückstand: {0} {1}. Die Zahlung wird täglich erneut versucht; das Bündnis bleibt bestehen.", "Tribute in arrears: {0} {1}. Payment is retried daily; the alliance remains in force."), state.TributeAmount, GoodName(state.TributeGoodId, state.TributeAmount));
            return string.Format(CultureInfo.InvariantCulture, Text("Tribut noch offen: {0} {1}. Die Zahlung wird täglich erneut versucht; noch {2} Tage Schonfrist.", "Tribute remains unpaid: {0} {1}. Payment is retried daily; {2} days of grace remain."), state.TributeAmount, GoodName(state.TributeGoodId, state.TributeAmount), DiplomacyTribute.DaysUntilGraceDeadline(state, day));
        }

        private bool SubjugationCurrent(string coalitionId, long generation, string fingerprint, int overlord, int amount, int offerDay)
        {
            CoalitionState coalition;
            RelationState relation;
            return _mapActive && !_mapDisabled && generation == _sessionGeneration && string.Equals(fingerprint, _mapFingerprint, StringComparison.Ordinal) && _coalitions.TryGetValue(coalitionId, out coalition) && _relations.TryGetValue(coalitionId, out relation) && relation.Allied && coalition.Allied && relation.ExtendedState != null && !DiplomacyVassalage.IsActive(relation.ExtendedState) && (relation.Demand == null || relation.Demand.DemandPurposeDescription != DiplomacyDemandPurpose.Ultimatum) && coalition.MemberPlayerIds.Contains(overlord) && relation.ExtendedState.PendingVassalOverlordPlayerId == overlord && relation.ExtendedState.PendingVassalTributeAmount == amount && relation.ExtendedState.PendingVassalOfferDay == offerDay;
        }

        private static void ClearPendingSubjugation(DiplomacyRelationState state)
        {
            DiplomacyVassalage.ClearPendingOffer(state);
        }

        private void DeferTreatyStandoffRetry()
        {
            _treatyStandoffDegraded = true;
            _standoffRetryTick = unchecked(ReadSimulationTick() + IncursionScanIntervalTicks * 2);
            _standoffFaultsPending = Math.Min(1000, _standoffFaultsPending + 1);
        }

        private readonly Dictionary<string, int> _tributeTechnicalFaults = new Dictionary<string, int>(StringComparer.Ordinal);
        private void NoteTributeTechnicalFailure(string coalitionId, string reason)
        {
            int count;
            _tributeTechnicalFaults.TryGetValue(coalitionId, out count);
            count = Math.Min(4, count + 1);
            _tributeTechnicalFaults[coalitionId] = count;
            if (count <= 3)
            {
                Log("tribute", "coalition=" + Safe(coalitionId) + "|status=deferred|reason=" + Safe(reason) + "|faults=" + count.ToString(CultureInfo.InvariantCulture));
            }
        }

        private bool VassalDecisionCurrent(string coalitionId, long generation, string fingerprint, out RelationState relation)
        {
            relation = null;
            CoalitionState coalition;
            return _mapActive && !_mapDisabled && generation == _sessionGeneration && string.Equals(fingerprint, _mapFingerprint, StringComparison.Ordinal) && _coalitions.TryGetValue(coalitionId, out coalition) && _relations.TryGetValue(coalitionId, out relation) && relation.Allied && coalition.Allied && DiplomacyVassalage.IsActive(relation.ExtendedState);
        }

        private static bool VassalContractMatches(DiplomacyRelationState state, int vassal, int overlord, int sinceDay, int releaseCount)
        {
            return state != null && state.VassalPlayerId == vassal && state.VassalOverlordPlayerId == overlord && state.VassalSinceDay == sinceDay && state.VassalReleases == releaseCount;
        }

        private bool StageVassalAction(string id, long generation, string fingerprint, DiplomacyVassalActionKind kind, int vassal, int overlord, int sinceDay, int requestDay, int amount, int releaseCount)
        {
            lock (_gate)
            {
                RelationState relation;
                if (DiplomacyVassalActions.IsSubjugation(kind))
                {
                    if (vassal != _localPlayer || !SubjugationCurrent(id, generation, fingerprint, overlord, amount, requestDay))
                        return false;
                    relation = _relations[id];
                    if (relation.ExtendedState.VassalReleases != releaseCount)
                        return false;
                }
                else
                {
                    if (!VassalDecisionCurrent(id, generation, fingerprint, out relation) || !VassalContractMatches(relation.ExtendedState, vassal, overlord, sinceDay, releaseCount))
                        return false;
                    if (DiplomacyVassalActions.IsEquality(kind) && relation.ExtendedState.VassalEqualityRequestedDay != requestDay)
                        return false;
                    if (kind == DiplomacyVassalActionKind.ClaimEquality && !DiplomacyVassalage.CanRequestEquality(relation.ExtendedState, Math.Max(0, _lastProcessedDay)))
                        return false;
                }

                return DiplomacyVassalActions.Stage(relation, _localPlayer, kind);
            }
        }

        private void PumpVassalActions()
        {
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            NormalizeVassalDemands();
            foreach (string id in _relations.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray())
            {
                RelationState relation = _relations[id];
                DiplomacyRelationState state = relation.ExtendedState;
                if (!DiplomacyVassalActions.HasPending(state))
                    continue;
                if (!DiplomacyVassalActions.Matches(relation, _localPlayer))
                {
                    DiplomacyVassalActions.Clear(state);
                    continue;
                }

                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return;
                using (lease)
                {
                    DiplomacyManagedStateMemento before = null;
                    try
                    {
                        if (!IsAlivePlayer(state.PendingVassalActionVassalPlayerId) || !IsAlivePlayer(state.PendingVassalActionOverlordPlayerId))
                        {
                            DiplomacyVassalActions.Clear(state);
                            continue;
                        }

                        var kind = state.PendingVassalAction;
                        int day = Math.Max(0, _lastProcessedDay);
                        if (kind == DiplomacyVassalActionKind.ClaimEquality && !DiplomacyVassalage.CanRequestEquality(state, day))
                        {
                            DiplomacyVassalActions.Clear(state);
                            continue;
                        }

                        before = CaptureManagedStateMemento();
                        int vassal = state.PendingVassalActionVassalPlayerId;
                        int overlord = state.PendingVassalActionOverlordPlayerId;
                        int amount = state.PendingVassalActionQuoteAmount;
                        DiplomacyVassalActions.Clear(state);
                        if (kind == DiplomacyVassalActionKind.AcceptSubjugation)
                            EstablishVassalage(_coalitions[id], relation, vassal, overlord, day, "alliance_local_vassal", amount);
                        else if (kind == DiplomacyVassalActionKind.RefuseSubjugation)
                        {
                            ClearPendingSubjugation(state);
                            Log("vassalage", "coalition=" + id + "|status=subjugation_declined");
                            ShowMessage(Text("Ihr habt die Lehnstreue verweigert. Es bleibt ein gewöhnliches Bündnis.", "You refused fealty. It stays an ordinary alliance."));
                        }
                        else if (DiplomacyVassalActions.IsEquality(kind))
                            ExecuteEqualityDecision(_coalitions[id], relation, day, kind == DiplomacyVassalActionKind.GrantEquality);
                        else
                            ExecuteVassalRelease(id, relation, kind == DiplomacyVassalActionKind.ClaimEquality, vassal == _localPlayer ? overlord : vassal, day);
                    }
                    catch (Exception exception)
                    {
                        if (before != null)
                            RestoreManagedStateMemento(before);
                        NoteTributeTechnicalFailure(id, "vassal_action_" + exception.GetType().Name);
                    }
                    finally
                    {
                        PublishCombatProtectionSnapshot();
                    }
                }

                // A refusal can queue a native break. Preserve that journal's
                // exclusivity before considering another answered contract.
                if (_pendingTeamTransition != null)
                    return;
            }
        }

        private void ExecuteVassalRelease(string coalitionId, RelationState current, bool localVassal, int selectedPlayer, int day)
        {
            DiplomacyTribute.Clear(current.ExtendedState);
            DiplomacyVassalage.Release(current.ExtendedState);
            DiplomacyAgreementRules.RecordVassalReleased(current.ExtendedState);
            DiplomacyAgreementRules.ResumeAllianceMaintenance(current.ExtendedState, day);
            PublishCombatProtectionSnapshot();
            Log("vassalage", "coalition=" + coalitionId + "|status=equality_granted|actor=" + (localVassal ? "local_vassal" : "local_overlord"));
            OnDiplomacyPresentationCommitted(DiplomacyPresentationEvent.Create(localVassal ? DiplomacyPresentationEventKind.EqualityGranted : DiplomacyPresentationEventKind.FealtyReleased, _sessionGeneration, day, selectedPlayer, null, _localPlayer, coalitionId, localVassal ? "local_vassal" : "local_overlord", new DiplomacyPresentationReference("fealty", otherPlayerId: _localPlayer)));
            ShowMessage(Text("Ihr seid nun gleichberechtigte Verbündete. Lehnspflicht und Tribut enden; das Bündnis bleibt bestehen.", "You are now equal allies. Fealty and tribute end; the alliance remains intact."));
        }

        // Called after the restored map has become active. The FIFO re-presents
        // saved decisions without starting a second request or resetting its date.
        private void RestorePendingDecisions()
        {
            foreach (CoalitionState coalition in _coalitions.Values.OrderBy(value => value.CoalitionId, StringComparer.Ordinal))
            {
                RelationState relation;
                if (_relations.TryGetValue(coalition.CoalitionId, out relation))
                {
                    NormalizeProudLegacyVassal(coalition, relation);
                    if (DiplomacySubmission.HasOffer(relation.ExtendedState) && !relation.ExtendedState.SubmissionOfferAccepted)
                        AskSubmissionOffer(coalition, relation);
                }

                if (_relations.TryGetValue(coalition.CoalitionId, out relation) && relation.ExtendedState != null && relation.ExtendedState.PendingVassalOverlordPlayerId > 0)
                {
                    AskSubjugation(coalition, relation, relation.ExtendedState.PendingVassalOverlordPlayerId, relation.ExtendedState.PendingVassalOfferDay);
                }

                if (_relations.TryGetValue(coalition.CoalitionId, out relation) && relation.Allied && DiplomacyVassalage.CoalitionIsVassal(relation.ExtendedState) && DiplomacyVassalage.EqualityRequestOpen(relation.ExtendedState))
                {
                    AskEqualityRequest(coalition, relation);
                }
            }
        }

        // Legacy saves may group ordinary allies with one named fealty endpoint.
        // Fealty belongs to those two players, never every member of the coalition.
        private bool IsFealtyCounterpart(DiplomacyRelationState state, int player)
        {
            return player >= 1 && player <= 8 && player != _localPlayer && DiplomacyVassalage.IsActive(state) && ((DiplomacyVassalage.OverlordOf(state) == _localPlayer && DiplomacyVassalage.VassalOf(state) == player) || (DiplomacyVassalage.VassalOf(state) == _localPlayer && DiplomacyVassalage.OverlordOf(state) == player));
        }

        private bool TryHandleVassalPrimary(int selectedPlayer)
        {
            string coalitionId;
            RelationState relation;
            if (!_playerCoalition.TryGetValue(selectedPlayer, out coalitionId) || !_relations.TryGetValue(coalitionId, out relation) || !relation.Allied || !IsFealtyCounterpart(relation.ExtendedState, selectedPlayer))
            {
                return false;
            }

            if (DiplomacyVassalActions.HasPending(relation.ExtendedState))
                return true;
            bool localVassal = DiplomacyVassalage.VassalOf(relation.ExtendedState) == _localPlayer;
            bool localOverlord = DiplomacyVassalage.OverlordOf(relation.ExtendedState) == _localPlayer;
            if (!localVassal && !localOverlord)
            {
                return false;
            }

            int day = Math.Max(0, _lastProcessedDay);
            if (localVassal && !DiplomacyVassalage.CanRequestEquality(relation.ExtendedState, day))
            {
                ShowMessage(VassalPrimaryHint(relation.ExtendedState));
                return true;
            }

            if (localOverlord && DiplomacyVassalage.EqualityRequestOpen(relation.ExtendedState))
            {
                AskEqualityRequest(_coalitions[coalitionId], relation);
                return true;
            }

            long generation = _sessionGeneration;
            string fingerprint = _mapFingerprint;
            int vassal = relation.ExtendedState.VassalPlayerId, overlord = relation.ExtendedState.VassalOverlordPlayerId;
            int sinceDay = relation.ExtendedState.VassalSinceDay, releases = relation.ExtendedState.VassalReleases;
            ShowConfirmation(PlayerName(selectedPlayer) + " · " + (localVassal ? Text("Gleichstellung fordern", "Request equal standing") : Text("Vasallen freilassen", "Release vassal")), delegate
            {
                StageVassalAction(coalitionId, generation, fingerprint, localVassal ? DiplomacyVassalActionKind.ClaimEquality : DiplomacyVassalActionKind.Release, vassal, overlord, sinceDay, -1, 0, releases);
            }, delegate
            {
            }, localVassal ? Text("Ihr habt Euch lange genug bewährt und seid dauerhaft stärker als Euer Lehnsherr. Fordert Ihr Gleichstellung, enden Lehnspflicht und Tribut bei fortbestehendem Bündnis.", "You have served long enough and maintained a stronger army than your overlord. Claiming equal standing ends fealty and tribute while preserving the alliance.") : Text("Wollt Ihr Euren Vasallen freiwillig freilassen? Die Tributzahlungen enden; Ihr bleibt Verbündete.", "Will you voluntarily release your vassal? Tribute payments end; you remain allies."), null, "vassal-release:" + coalitionId, delegate
            {
                RelationState current;
                return VassalDecisionCurrent(coalitionId, generation, fingerprint, out current) && !DiplomacyVassalActions.HasPending(current.ExtendedState) && VassalContractMatches(current.ExtendedState, vassal, overlord, sinceDay, releases) && IsFealtyCounterpart(current.ExtendedState, selectedPlayer) && (localVassal ? DiplomacyVassalage.VassalOf(current.ExtendedState) == _localPlayer : DiplomacyVassalage.OverlordOf(current.ExtendedState) == _localPlayer);
            });
            return true;
        }

        private string VassalPrimaryHint(DiplomacyRelationState state)
        {
            if (DiplomacyVassalage.VassalOf(state) == _localPlayer)
            {
                return Text("Gleichstellung: mindestens ", "Equal standing: at least ") + DiplomacyVassalage.MinimumTermDays.ToString(CultureInfo.InvariantCulture) + Text(" Tage Lehnstreue und ", " days of fealty and ") + DiplomacyVassalage.RebellionEdgeDays.ToString(CultureInfo.InvariantCulture) + Text(" Tage in Folge militärisch überlegen.", " consecutive days of military superiority.");
            }

            return Text("Vasallen freilassen und das Bündnis bewahren.", "Release your vassal while preserving the alliance.");
        }

        private void RefreshExtraFealtyAction(object panel, RelationState relation)
        {
            object button = FindElement(panel, "AllianceDetailsFealty");
            object label = FindElement(panel, "AllianceDetailsFealtyLabel");
            DiplomacyRelationState state = relation.ExtendedState;
            bool counterpart = IsFealtyCounterpart(state, SelectedPlayer(panel));
            bool localVassal = counterpart && DiplomacyVassalage.VassalOf(state) == _localPlayer;
            bool localOverlord = counterpart && DiplomacyVassalage.OverlordOf(state) == _localPlayer;
            // Submission is an action between enemies. Fealty names an existing
            // contract only; equal allies have no fictional fealty to manage.
            bool show = state != null && (counterpart || !relation.Allied && !DiplomacyVassalage.IsActive(state));
            SetVisibilityIfPresent(button, show);
            SetVisibilityIfPresent(label, show);
            SetVisibilityIfPresent(FindElement(panel, "AllianceAgreement"), true);
            if (!show || button == null || label == null)
                return;
            bool accepted = state.SubmissionAcceptancePending || state.SubmissionOfferAccepted;
            bool pending = accepted || DiplomacyVassalActions.HasPending(state);
            // PrimaryLordId is an appearance/profile identifier, not a verified
            // GetAILord identity. The queued action checks that native identity
            // under the engine lease before any offer can be created.
            bool equalityReady = !localVassal || DiplomacyVassalage.CanRequestEquality(state, Math.Max(0, _lastProcessedDay));
            bool enabled = !pending && equalityReady;
            SetProperty(button, "IsEnabled", enabled);
            SetProperty(button, "ToolTip", counterpart ? VassalPrimaryHint(state) : accepted ? Text("Unterwerfung bestätigt", "Submission accepted") : DiplomacySubmission.HasOffer(state) ? Text("Unterwerfungsangebot prüfen", "Review submission offer") : Text("Unterwerfung fordern", "Demand submission"));
            SetProperty(label, "Text", localVassal ? Text("Gleichstellung fordern", "Request equal standing") : localOverlord ? Text("Vasallen freilassen", "Release vassal") : accepted ? Text("Unterwerfung bestätigt", "Submission accepted") : DiplomacySubmission.HasOffer(state) ? Text("Angebot prüfen", "Review offer") : Text("Unterwerfung fordern", "Demand submission"));
            SetNumericProperty(label, "Opacity", enabled ? 1.0 : 0.48);
        }

        private string VassalRoleText(DiplomacyRelationState state)
        {
            if (!DiplomacyVassalage.IsActive(state))
                return string.Empty;
            if (DiplomacyVassalage.VassalOf(state) == _localPlayer)
                return Text("Euer Lehnsherr", "Your overlord");
            if (DiplomacyVassalage.OverlordOf(state) == _localPlayer)
                return Text("Euer Vasall", "Your vassal");
            return Text("Lehnsverhältnis", "Fealty");
        }
    }
}
