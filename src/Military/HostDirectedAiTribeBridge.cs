// Mod-authored solo AI defense requests, evaluated and carried out by the original game.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        internal static readonly bool PrivateMilitarySupportRequested =
            Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") == "1" &&
            Environment.GetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_MILITARY") == "1";
        internal static readonly bool VerifyMilitaryTransport =
            PrivateMilitarySupportRequested &&
            Environment.GetEnvironmentVariable("DIPLOMACY_VERIFY_MILITARY_TRANSPORT") == "1";

        private bool _originalMilitaryRunVerified, _militaryBindingAttempted, _militaryOwnerObserved;
        [ThreadStatic] private static bool _insideAutonomousDefenceEvaluator;
        private PinnedOriginalDefendConsumer _originalMilitaryConsumer;
        private OriginalAllyChoreQueue _originalMilitaryQueue;
        private HostDirectedAiTribeCandidate _pendingMilitarySupport;
        private long _lastMilitaryReviewTick = -1L;
        private int _militaryNativeAttempts, _militaryOriginalApprovals;
        private readonly bool[] _militarySupportSentThisEmergency = new bool[9];

        private sealed class HostDirectedAiTribeCandidate
        {
            internal byte Donor, Recipient;
            internal bool TransportVerification;
            internal long Generation, Epoch, Tick, FreshCancellationSerial;
            internal string MapIdentity;
        }

        private long _hostDirectedFreshAfterTick = long.MaxValue;
        private long _hostDirectedCancellationSerial;
        private bool _hostDirectedColdNeedsBaseline;
        private bool _hostDirectedRequireFreshReliefEdge;
        private readonly bool[] _hostDirectedPreviousEmergency = new bool[9];
        private readonly bool[] _hostDirectedEmergencyObservationKnown = new bool[9];
        private readonly bool[] _hostDirectedFreshReliefEdges = new bool[9];

        // Existing map lifecycle owns this state. The offer does NOT survive
        // SaveData, reload, map unload or another generation. No own save journal.
        private void ResetHostDirectedAiTribeForMap()
        {
            _pendingMilitarySupport = null;
            _lastMilitaryReviewTick = -1L;
            _militaryOwnerObserved = false;
            _militaryNativeAttempts = 0;
            _militaryOriginalApprovals = 0;
            _hostDirectedFreshAfterTick = long.MaxValue;
            _hostDirectedCancellationSerial++;
            _hostDirectedColdNeedsBaseline = true;
            _hostDirectedRequireFreshReliefEdge = false;
            Array.Clear(_hostDirectedPreviousEmergency, 0, 9);
            Array.Clear(_hostDirectedEmergencyObservationKnown, 0, 9);
            Array.Clear(_hostDirectedFreshReliefEdges, 0, 9);
            Array.Clear(_militarySupportSentThisEmergency, 0, 9);
        }

        // Unity owner samples the live strategic facts. The managed original
        // run postfix is the only path that can submit the proposed request.
        private void PumpHostDirectedAiTribe()
        {
            if (!PrivateMilitarySupportRequested || !_originalMilitaryRunVerified)
                return;
            if (!_militaryBindingAttempted)
            {
                _militaryBindingAttempted = true;
                string reason;
                OriginalAllyChoreQueue queue;
                PinnedOriginalDefendConsumer consumer;
                if (!OriginalAllyChoreQueue.TryBind(out queue, out reason) ||
                    !PinnedOriginalDefendConsumer.TryBindLoadedOriginal(out consumer, out reason))
                {
                    Log("autonomous_military", "stage=binding_refused|reason=" + Safe(reason));
                    return;
                }
                _originalMilitaryQueue = queue;
                _originalMilitaryConsumer = consumer;
                Log("autonomous_military", "stage=original_consumer_and_chore_queue_bound");
            }
            if (_originalMilitaryConsumer == null || _pendingMilitarySupport != null ||
                !MilitarySupportWorldReady())
                return;

            long tick = ReadSimulationTick();
            if (tick <= _hostDirectedFreshAfterTick || tick == _lastMilitaryReviewTick)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                tick = ReadSimulationTick();
                if (tick <= _hostDirectedFreshAfterTick || tick == _lastMilitaryReviewTick)
                    return;
                _lastMilitaryReviewTick = tick;
                try
                {
                    HostDirectedAiTribeCandidate candidate;
                    bool proposed = VerifyMilitaryTransport
                        ? TrySelectTransportVerificationPair(tick, out candidate)
                        : TryCollectFreshHostDirectedAiTribe(tick, out candidate);
                    if (proposed)
                        _pendingMilitarySupport = candidate;
                }
                catch (Exception error)
                {
                    Log("autonomous_military", "stage=proposal_deferred|reason=" + Safe(error.GetType().Name));
                }
            }
        }

        internal static bool TryReadAutonomousMilitaryObservation(out string observation,
            out int nativeAttempts, out int nativeApprovals)
        {
            observation = null;
            nativeAttempts = 0;
            nativeApprovals = 0;
            var owner = Volatile.Read(ref _activeOwner);
            if (owner == null)
                return false;
            bool entered = false;
            try
            {
                Monitor.TryEnter(owner._gate, 0, ref entered);
                if (!entered)
                    return false;
                nativeAttempts = owner._militaryNativeAttempts;
                nativeApprovals = owner._militaryOriginalApprovals;
                int approvalsInHistory = owner._requestHistory == null ? 0 :
                    owner._requestHistory.Records.Count(request => request.Kind == DiplomaticRequestKind.Military &&
                        request.Origin == DiplomaticRequestOrigin.ModAuthored && request.OriginalApprovalObserved);
                observation = "enabled=" + BoolText(PrivateMilitarySupportRequested) +
                    "|transport_verification=" + BoolText(VerifyMilitaryTransport) +
                    "|run_contract=" + BoolText(owner._originalMilitaryRunVerified) +
                    "|native_bound=" + BoolText(owner._originalMilitaryConsumer != null) +
                    "|original_simulation_owner=" + BoolText(owner._militaryOwnerObserved) +
                    "|native_attempts=" + owner._militaryNativeAttempts +
                    "|original_mode_acceptances=" + nativeApprovals +
                    "|recorded_original_approvals=" + approvalsInHistory +
                    "|sim_tick=" + owner.ReadSimulationTick();
                return true;
            }
            finally
            {
                if (entered)
                    Monitor.Exit(owner._gate);
            }
        }

        private bool MilitarySupportWorldReady()
        {
            return RequestHistoryOwnerReady() && _mapIdentity != null &&
                !_treatyStandoffActive && !_inventoryRestorePending &&
                _pendingSavePayload == null && _pendingLegacySavePayload == null &&
                _activeSave == null && !_saveSuppressed && _pendingPackage == null &&
                _nativeTransferProbe == null && _pendingTeamTransition == null &&
                _executingIntent == null;
        }

        // A native tick witnessed inside this very managed EngineInterface.run
        // distinguishes it from ReceiveChore, paused frames and stale threads.
        // Keep the original engine monitor and only try the model gate.
        private void ServiceAutonomousMilitaryAtOriginalTick(OrderedCall call)
        {
            if (!IsNativeDiplomacySectionHeld ||
                ReadSimulationTick() <= call.MilitaryTickBefore ||
                _simulationThreadId != Environment.CurrentManagedThreadId ||
                _simulationNativeThreadId == 0 || _simulationNativeThreadId != SafeNativeThreadId())
                return;

            bool entered = false;
            try
            {
                Monitor.TryEnter(_gate, 0, ref entered);
                if (!entered || _originalMilitaryQueue == null)
                    return;
                if (!_militaryOwnerObserved && MilitarySupportWorldReady())
                {
                    bool pending;
                    if (_originalMilitaryQueue.TryReadPendingMilitaryOrder(out pending))
                    {
                        _militaryOwnerObserved = true;
                        Log("autonomous_military", "stage=original_simulation_owner_observed|tick=" +
                            ReadSimulationTick() + "|thread=" + _simulationThreadId +
                            "|pending_original_military=" + BoolText(pending));
                    }
                }
                if (_pendingMilitarySupport == null)
                    return;
                var candidate = _pendingMilitarySupport;
                _pendingMilitarySupport = null;
                long tick = ReadSimulationTick();
                if (!MilitarySupportWorldReady() ||
                    call.Generation != _sessionGeneration || call.Epoch != _mapEpoch ||
                    candidate.Generation != _sessionGeneration || candidate.Epoch != _mapEpoch ||
                    candidate.FreshCancellationSerial != _hostDirectedCancellationSerial ||
                    !String.Equals(candidate.MapIdentity, _mapFingerprint, StringComparison.Ordinal) ||
                    tick <= candidate.Tick || tick - candidate.Tick > 2L)
                    return;

                // Verify the current original force and reserve again after
                // the native tick. The earlier proposal is only a wake-up.
                HostDirectedAiTribeCandidate current;
                if (candidate.TransportVerification)
                    DispatchOriginalMilitarySupport(candidate, tick);
                else if (TryCollectFreshHostDirectedAiTribe(tick, out current) &&
                    current.Donor == candidate.Donor && current.Recipient == candidate.Recipient)
                    DispatchOriginalMilitarySupport(current, tick);
            }
            catch (Exception error)
            {
                Log("autonomous_military", "stage=original_run_deferred|reason=" + Safe(error.GetType().Name));
            }
            finally
            {
                if (entered)
                    Monitor.Exit(_gate);
            }
        }

        private unsafe void DispatchOriginalMilitarySupport(HostDirectedAiTribeCandidate candidate, long tick)
        {
            int donor = candidate.Donor, defended = candidate.Recipient;
            string reason;
            bool pendingOriginalMilitary;
            var api = GamePlayerManagerAPI.Instance;
            var save = TradeSaveBoundary();
            if (save.SaveActive || save.MutationActive || save.Quarantined ||
                _militarySupportSentThisEmergency[defended] || !IsSoloEngineSession(out reason) ||
                !OriginalNativeAiPair(api, donor, defended) ||
                !IsAlivePlayer(donor) || !IsAlivePlayer(defended) ||
                !CanTradePair(donor, defended, out reason) ||
                !_originalMilitaryQueue.TryReadPendingMilitaryOrder(out pendingOriginalMilitary) ||
                pendingOriginalMilitary)
                return;

            GamePlayerResources* donorResources;
            GamePlayerResources* defendedResources;
            if (!api.TryGetPlayerResourcesById(donor, out donorResources) || donorResources == null ||
                !api.TryGetPlayerResourcesById(defended, out defendedResources) || defendedResources == null ||
                donorResources->r_AIAllyOrderMode != 0 ||
                (!candidate.TransportVerification &&
                 (defendedResources->r_AILastStandPhase < 1U || defendedResources->r_AILastStandPhase > 8U)))
                return;

            // Linearize with the real HUD capture. A later click remains in the
            // human queue, whose dispatcher also needs the original monitor.
            bool entered = false;
            try
            {
                Monitor.TryEnter(_humanCaptureGate, 0, ref entered);
                if (!entered || Volatile.Read(ref _allyOrders[donor]) != null ||
                    Volatile.Read(ref _allyInFlight[donor]) != null ||
                    Volatile.Read(ref _originalSupportNativeClaims[donor]) != null)
                    return;
            }
            finally
            {
                if (entered)
                    Monitor.Exit(_humanCaptureGate);
            }

            ulong requestId;
            if (!TryCaptureDiplomaticRequest(DiplomaticRequestKind.Military,
                    DiplomaticRequestOrigin.ModAuthored, defended, donor, 0, 0, 0, 11, out requestId))
                return;
            if (!TryDecideDiplomaticRequest(requestId, true, DiplomaticRequestDecisionEvidence.ModPolicy) ||
                !TryNoteDiplomaticRequestForwarded(requestId))
            {
                TryNoteDiplomaticRequestCancelled(requestId, false);
                return;
            }

            // Claim before crossing the native ABI. A throw could follow a
            // real application and must never trigger a second request.
            _militarySupportSentThisEmergency[defended] = true;
            _militaryNativeAttempts++;
            Log("autonomous_military", "stage=native_begin|request=" + requestId +
                "|donor=" + donor + "|defended=" + defended + "|tick=" + tick +
                "|transport_verification=" + BoolText(candidate.TransportVerification));
            OriginalSupportHookSpan span = OriginalAllySynchronousEvaluatorCapture.Begin(donor, defended);
            bool? evaluated;
            try
            {
                _insideAutonomousDefenceEvaluator = true;
                _originalMilitaryConsumer.RequestDefend(0, donor, defended, 11);
            }
            catch (Exception error)
            {
                Log("autonomous_military", "stage=invocation_outcome_unknown|request=" + requestId +
                    "|reason=" + Safe(error.GetType().Name));
                return;
            }
            finally
            {
                _insideAutonomousDefenceEvaluator = false;
                evaluated = OriginalAllySynchronousEvaluatorCapture.EndObservedDecision(span);
            }
            bool accepted = donorResources->r_AIAllyOrderMode == 2 &&
                donorResources->r_AIAllyOrderRequesterPlayerId == (uint)defended;
            if (accepted)
            {
                _militaryOriginalApprovals++;
                TryNoteDiplomaticRequestObservedApproval(requestId);
            }
            else if (evaluated == false)
                TryNoteDiplomaticRequestRefused(requestId);
            Log("autonomous_military", "stage=native_return|request=" + requestId +
                "|donor=" + donor + "|defended=" + defended + "|tick=" + tick +
                "|accepted_tuple=" + BoolText(accepted) +
                "|original_evaluator_post=" + (evaluated.HasValue ? BoolText(evaluated.Value) : "unbound") +
                "|military_success=false");
        }

        // Explicit headless transport exercise. A real original ally pair
        // can validate the dispatcher even during calm; this never labels the
        // test as an emergency or an original CPU-authored request.
        private unsafe bool TrySelectTransportVerificationPair(long tick,
            out HostDirectedAiTribeCandidate candidate)
        {
            candidate = null;
            string reason;
            if (_militaryNativeAttempts != 0 || !IsSoloEngineSession(out reason))
                return false;
            var players = GamePlayerManagerAPI.Instance;
            if (players == null)
                return false;
            int[] alive = players.GetAlivePlayerIds();
            if (alive == null)
                return false;
            long largestArmy = 0;
            foreach (int donor in alive.OrderBy(slot => slot))
            {
                GamePlayerResources* resources;
                if (donor == _localPlayer || !players.IsAIPlayer(donor) ||
                    !players.TryGetPlayerResourcesById(donor, out resources) || resources == null ||
                    resources->r_AIAllyOrderMode != 0)
                    continue;
                // Exact fields read by the original rescue evaluator. This is
                // a read-only choice of a positive test case; native still decides.
                int profile = unchecked((int)resources->r_AILordMinusOne) - 1;
                int rescueArmy = unchecked((int)resources->N00004015);
                int rescueThreshold;
                if (resources->N000040FB != 0 || rescueArmy <= largestArmy ||
                    !_originalMilitaryConsumer.TryReadOriginalRescueThreshold(profile, out rescueThreshold) ||
                    rescueArmy < rescueThreshold)
                    continue;
                foreach (int defended in alive.OrderBy(slot => slot))
                {
                    if (defended == _localPlayer || !OriginalNativeAiPair(players, donor, defended) ||
                        !CanTradePair(donor, defended, out reason))
                        continue;
                    largestArmy = rescueArmy;
                    candidate = new HostDirectedAiTribeCandidate
                    {
                        Donor = (byte)donor, Recipient = (byte)defended,
                        TransportVerification = true, Tick = tick,
                        Generation = _sessionGeneration, Epoch = _mapEpoch,
                        FreshCancellationSerial = _hostDirectedCancellationSerial,
                        MapIdentity = _mapFingerprint
                    };
                    break;
                }
            }
            return candidate != null;
        }

        // The mod asks an original AI donor for help when an allied AI has
        // a verified last stand and is outmatched at its keep.
        private unsafe bool TryCollectFreshHostDirectedAiTribe(
            long tick, out HostDirectedAiTribeCandidate candidate)
        {
            candidate = null;
            // Exact ORIGINAL loaded managed ABI before any SDK-facing read.
            // Physical native/managed SHA pins require independent
            // canary prerequisite, not an implicit authority promotion.
            if (typeof(GameTribeManagerAPI).Assembly.ManifestModule.ModuleVersionId !=
                    new Guid("aab7af1d-dabf-4f55-a7a6-dd67c8f06853") ||
                typeof(EngineInterface).Assembly.ManifestModule.ModuleVersionId !=
                    new Guid("29ade4ef-f587-422f-a1eb-f81dff066e0a"))
                return false;
            if (!IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) ||
                tick <= _hostDirectedFreshAfterTick || _lastProcessedDay < 1 ||
                _treatyStandoffActive)
                return false;
            var save = TradeSaveBoundary();
            if (_activeSave != null || _saveSuppressed || _inventoryRestorePending ||
                _pendingSavePayload != null || _pendingLegacySavePayload != null ||
                _pendingPackage != null || _nativeTransferProbe != null ||
                _pendingTeamTransition != null || _executingIntent != null ||
                save.SaveActive || save.MutationActive || save.Quarantined)
                return false;
            string rosterReason;
            // The original solo roster is the authority. The Steam-host
            // flag may be false in a genuine cold-loaded one-human game.
            bool solo = IsSoloEngineSession(out rosterReason);
            if (!solo)
                return false;
            var data = GameData.Instance;
            var state = data == null ? null : data.lastGameState;
            var players = GamePlayerManagerAPI.Instance;
            var tribes = GameTribeManagerAPI.Instance;
            var units = GameUnitManagerAPI.Instance;
            var tiles = GameTileManagerAPI.Instance;
            if (state == null || players == null || tribes == null ||
                units == null || tiles == null || _mapIdentity == null)
                return false;
            int local = players.GetLocalPlayerId();
            int[] alive = players.GetAlivePlayerIds();
            if (local < 1 || local > 8 || alive == null || !alive.Contains(local))
                return false;
            // Sender-only complete original alive unit census. Actual group
            // ownership comes solely from GameTribe.r_PlayerIdOwner. The
            // controllable-for-player unit API is not authoritative ownership.
            List<int> allTribes = null;
            HostDirectedOriginalOwnerLedger nativeOwnership = null;
            Func<int, int, bool> originalAlliedTo =
                delegate(int a, int b) { return players.IsPlayerAlliedTo(a, b); };

            for (int recipient = 1; recipient <= 8; recipient++)
            {
                if (recipient == local || !alive.Contains(recipient) ||
                    state.is_valid_player(recipient) ||
                    !state.is_skirmish_player(recipient) || !players.IsAIPlayer(recipient))
                    continue;
                GamePlayerResources* recipientResources;
                if (!players.TryGetPlayerResourcesById(recipient, out recipientResources) ||
                    recipientResources == null)
                    continue;
                // EXACT named SHCDESE 2.14.3 native field, not a fabricated
                // LastStandVerified=true synthetic planner switch.
                uint actualLastStandPhase = recipientResources->r_AILastStandPhase;
                if (actualLastStandPhase < 1U || actualLastStandPhase > 8U)
                {
                    // A measured non-emergency is evidence; a missing native
                    // read is not evidence of a prior safe state.
                    _hostDirectedPreviousEmergency[recipient] = false;
                    _hostDirectedEmergencyObservationKnown[recipient] = true;
                    _hostDirectedFreshReliefEdges[recipient] = false;
                    _militarySupportSentThisEmergency[recipient] = false;
                    continue;
                }
                WorldForceUnit recipientKeep;
                if (!TryGetFixedKeepCenter(recipient, out recipientKeep))
                    continue;
                if (nativeOwnership == null)
                {
                    allTribes = new List<int>();
                    tribes.GetAllTribes(allTribes, null, null, null);
                    if (!TryCollectHostDirectedOriginalOwners(tribes, units, allTribes, out nativeOwnership))
                        return false;
                }
                long recipientTotal = nativeOwnership.KnownGroupedStrength(recipient);
                if (recipientTotal > Int32.MaxValue)
                    continue;
                long originalLocalThreat;
                string originalOwnerReason;
                if (!nativeOwnership.TryThreatAroundKeep(
                    recipient, recipientKeep.X, recipientKeep.Y, originalAlliedTo,
                    out originalLocalThreat, out originalOwnerReason))
                    return false; // Unknown nearby owner is not observed calm.
                if (originalLocalThreat <= recipientTotal ||
                    originalLocalThreat > Int32.MaxValue)
                {
                    // A measured non-emergency is evidence; a missing native
                    // read is not evidence of a prior safe state.
                    _hostDirectedPreviousEmergency[recipient] = false;
                    _hostDirectedEmergencyObservationKnown[recipient] = true;
                    _hostDirectedFreshReliefEdges[recipient] = false;
                    _militarySupportSentThisEmergency[recipient] = false;
                    continue;
                }
                if (_hostDirectedColdNeedsBaseline)
                {
                    // The first valid post-restore observation establishes only
                    // a baseline. An unseen earlier state cannot supply an edge.
                    _hostDirectedPreviousEmergency[recipient] = true;
                    _hostDirectedEmergencyObservationKnown[recipient] = true;
                    continue;
                }
                if (_hostDirectedRequireFreshReliefEdge)
                {
                    bool previouslyObserved = _hostDirectedEmergencyObservationKnown[recipient];
                    bool previouslyEmergency = _hostDirectedPreviousEmergency[recipient];
                    bool freshRelief = HostDirectedAiTribePolicy.FreshReliefPermitted(
                        true, false, previouslyObserved, previouslyEmergency,
                        _hostDirectedFreshReliefEdges[recipient], true);
                    if (previouslyObserved && !previouslyEmergency)
                    {
                        _hostDirectedFreshReliefEdges[recipient] = true;
                    }
                    _hostDirectedPreviousEmergency[recipient] = true;
                    _hostDirectedEmergencyObservationKnown[recipient] = true;
                    if (!freshRelief)
                        continue;
                }
                else
                {
                    _hostDirectedPreviousEmergency[recipient] = true;
                    _hostDirectedEmergencyObservationKnown[recipient] = true;
                }
                if (_militarySupportSentThisEmergency[recipient])
                    continue;
                UnmanagedVector2<int> targetDoor = players.GetPlayerKeepDoorPosition(recipient);
                if (!tiles.IsTileInsideMapBounds(targetDoor.X, targetDoor.Y))
                    continue;

                for (int donor = 1; donor <= 8; donor++)
                {
                    string policyReason;
                    if (donor == local || donor == recipient ||
                        !alive.Contains(donor) || state.is_valid_player(donor) ||
                        !state.is_skirmish_player(donor) ||
                        !OriginalNativeAiPair(players, donor, recipient) ||
                        !CanTradePair(donor, recipient, out policyReason))
                        continue;
                    long donorTotal = nativeOwnership.KnownGroupedStrength(donor);
                    if (donorTotal <= 0 || donorTotal > Int32.MaxValue)
                        continue;
                    WorldForceUnit donorKeep;
                    if (!TryGetFixedKeepCenter(donor, out donorKeep))
                        continue;
                    long nearbyHome, defendingThreat;
                    string originalHomeReason, originalThreatReason;
                    if (!nativeOwnership.TryHomeStrength(
                            donor, donorKeep.X, donorKeep.Y,
                            out nearbyHome, out originalHomeReason) ||
                        !nativeOwnership.TryThreatAroundKeep(
                            donor, donorKeep.X, donorKeep.Y, originalAlliedTo,
                            out defendingThreat, out originalThreatReason))
                        return false; // Unknown combat owner near home blocks.
                    // Explicit MOD safety policy: reserve >= one-third of real
                    // observed total AND all observed forces in home envelope.
                    // Neither amount is mislabeled as an original AI request.
                    long policyReserve = Math.Max((donorTotal + 2L) / 3L, nearbyHome);
                    if (policyReserve > Int32.MaxValue ||
                        defendingThreat > Int32.MaxValue)
                        continue;
                    long maximumDetachment;
                    if (!TryMeasureOriginalDefenceDetachment(players, tribes, units,
                            nativeOwnership, donor, out maximumDetachment) ||
                        maximumDetachment < 1 || maximumDetachment > donorTotal)
                        continue;
                    var f = new HostDirectedAiTribeAuthorizationFacts
                    {
                        CandidateGeneration = _sessionGeneration,
                        CurrentGeneration = _sessionGeneration,
                        CandidateEpoch = _mapEpoch, CurrentEpoch = _mapEpoch,
                        CandidateTick = tick, CurrentTick = tick,
                        ExpiryTick = tick,
                        OriginalLocalHuman = local,
                        DonorAiSlot = donor, RecipientAiSlot = recipient,
                        DonorMilitaryStrength = (int)donorTotal,
                        CastleDefenceReserve = (int)policyReserve,
                        ThreatStrength = (int)defendingThreat,
                        TribePower = (int)maximumDetachment,
                        SoloRosterVerified = solo,
                        SoloHostVerified = solo,
                        EngineMonitorHeld = IsNativeDiplomacySectionHeld,
                        CpuDonorVerified = !state.is_valid_player(donor) && state.is_skirmish_player(donor) && players.IsAIPlayer(donor),
                        CpuRecipientVerified = !state.is_valid_player(recipient) && state.is_skirmish_player(recipient) && players.IsAIPlayer(recipient),
                        OriginalTribeOwnerVerified = true,
                        MemberGlobalIdentitiesVerified = true,
                        ReciprocalNativeAllianceVerified =
                            players.IsPlayerAlliedTo(donor, recipient) &&
                            players.IsPlayerAlliedTo(recipient, donor),
                        OriginalSaveFenceClear = _activeSave == null && !_inventoryRestorePending && !save.SaveActive && !save.MutationActive && !save.Quarantined,
                        // LastCommand/UnitStop/cooldown/patrol describe
                        // a snapshot, NOT the native AI pending-order queue.
                        // Remain unknown until an exact original queue
                        // owner + reader + producer/writer are established.
                        OriginalOrderSlotUncontested = null,
                        NativeTargetVerified = tiles.IsTileInsideMapBounds(
                            targetDoor.X, targetDoor.Y),
                        MapActive = _mapActive, MapDisabled = _mapDisabled,
                        BootstrapPending = _mapBootstrapPending,
                        HasCompletedOriginalIdentity = _mapIdentity != null &&
                            _mapFingerprint != null,
                        HistoricalReplayFromSave = tick <= _hostDirectedFreshAfterTick,
                        RepeatOrReplay = _militarySupportSentThisEmergency[recipient],
                        MapWidthTiles = GameTileManagerAPI.MAX_WIDTH,
                        MapHeightTiles = GameTileManagerAPI.MAX_WIDTH,
                        TileX = targetDoor.X, TileY = targetDoor.Y,
                        IsBuildingTarget = false,
                        OriginalRecipientLastStandVerified = actualLastStandPhase >= 1U && actualLastStandPhase <= 8U,
                        ObservedRecipientReliefThreatVerified =
                            originalLocalThreat > recipientTotal
                    };
                    if (HostDirectedAiTribePolicy.Evaluate(f, originalEvaluatorRequest: true) !=
                        "mod_authored_original_evaluator_candidate")
                        continue;
                    candidate = new HostDirectedAiTribeCandidate
                    {
                        Donor = (byte)donor, Recipient = (byte)recipient,
                        Generation = _sessionGeneration, Epoch = _mapEpoch,
                        Tick = tick,
                        FreshCancellationSerial = _hostDirectedCancellationSerial,
                        MapIdentity = _mapFingerprint
                    };
                    return true;
                }
            }
            if (_hostDirectedColdNeedsBaseline)
                _hostDirectedColdNeedsBaseline = false;
            return false;
        }

        // Original 0x3D020 uses these exact role ranges for ally defence.
        // Measure all groups that the original could send, including multiple
        // groups and their live member identities, before checking the reserve.
        private static readonly int[] DefenceStorageRoles =
        {
            11, 12, 13, 14, 15, 16, 17, 18,
            186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199
        };

        private unsafe bool TryMeasureOriginalDefenceDetachment(GamePlayerManagerAPI players,
            GameTribeManagerAPI tribes, GameUnitManagerAPI units,
            HostDirectedOriginalOwnerLedger ownership, int donor, out long power)
        {
            power = 0;
            GamePlayerResources* resources;
            if (!players.TryGetPlayerResourcesById(donor, out resources) || resources == null ||
                resources->r_AIAllyOrderMode != 0)
                return false;

            var selected = new HashSet<int>();
            foreach (int role in DefenceStorageRoles)
            {
                int id = resources->r_AITribeIdsByStorageRole[role];
                if (id == 0)
                    continue;

                GameTribe* group;
                if (id > Int16.MaxValue || !tribes.IsValid(id) ||
                    !tribes.TryGetTribeById(id, out group) || group == null)
                    return false;
                if (group->r_GlobalId != resources->r_AITribeGlobalIdsByStorageRole[role])
                    continue; // The original selector skips a recycled ID.
                if (group->r_GlobalId == 0 || group->r_AliveState != AliveState.IsAlive ||
                    group->r_PlayerIdOwner != donor)
                    return false;
                if (!selected.Add(id))
                    continue;

                var members = new List<int>();
                if (!tribes.GetUnits(id, members) || members.Count < 1 || members.Count > 10000)
                    return false;
                var ids = members.ToArray();
                var globals = new uint[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    GameUnit* unit;
                    if (ids[i] < 1 || !units.IsValid(ids[i]) ||
                        !units.TryGetUnitById(ids[i], out unit) || unit == null ||
                        unit->r_AliveState != AliveState.IsAlive || unit->r_TribeId != id)
                        return false;
                    globals[i] = unit->r_GlobalId;
                    eChimps type = units.GetType(ids[i]);
                    if (type != eChimps.CHIMP_TYPE_LORD && IsCombatUnit(type))
                    {
                        long strength = DiplomacyMilitaryValue(units, ids[i], type);
                        if (strength <= 0 || power > Int32.MaxValue - strength)
                            return false;
                        power += strength;
                    }
                }
                if (!ownership.ExactMemberList(id, ids, globals))
                    return false;
            }
            return true;
        }

        // Collect ALL original alive units under the existing engine monitor.
        // SHCDESE.GetAllAliveUnits uses its IsAlive query (0x06000D6E);
        // GetAllTribes is unfiltered (0x06000D3B). Only the ORIGINAL
        // GameTribe.r_PlayerIdOwner supplies authoritative group ownership.
        private unsafe bool TryCollectHostDirectedOriginalOwners(
            GameTribeManagerAPI tribes, GameUnitManagerAPI units,
            IList<int> originalTribeIds,
            out HostDirectedOriginalOwnerLedger ledger)
        {
            ledger = null;
            if (!IsNativeDiplomacySectionHeld || tribes == null || units == null ||
                originalTribeIds == null || originalTribeIds.Count > 10000)
                return false;
            var groups = new List<HostDirectedOriginalTribeFact>();
            var groupIds = new HashSet<int>();
            foreach (int id in originalTribeIds)
            {
                GameTribe* group;
                if (id < 1 || !groupIds.Add(id) ||
                    !tribes.TryGetTribeById(id, out group) || group == null)
                    return false;
                if (group->r_AliveState != AliveState.IsAlive)
                    continue;
                groups.Add(new HostDirectedOriginalTribeFact(
                    id, group->r_PlayerIdOwner, group->r_GlobalId));
            }

            int[] aliveIds = units.GetAllAliveUnits();
            if (aliveIds == null || aliveIds.Length == 0 || aliveIds.Length > 50000)
                return false;
            var live = new List<HostDirectedOriginalUnitFact>(aliveIds.Length);
            foreach (int unitId in aliveIds)
            {
                GameUnit* original;
                if (unitId < 1 || !units.IsValid(unitId) ||
                    !units.TryGetUnitById(unitId, out original) || original == null ||
                    original->r_AliveState != AliveState.IsAlive)
                    return false;
                eChimps type = units.GetType(unitId);
                bool combat = type != eChimps.CHIMP_TYPE_LORD && IsCombatUnit(type);
                long value = 0;
                int worldX = 0, worldY = 0;
                if (combat)
                {
                    value = DiplomacyMilitaryValue(units, unitId, type);
                    // 0x06000D90: current local tile -> original world
                    // coordinates via ToWorldPosition; matches keep center.
                    UnmanagedVector2<ushort> point =
                        units.GetCurrentWorldTilePosition(unitId);
                    worldX = point.X;
                    worldY = point.Y;
                }
                live.Add(new HostDirectedOriginalUnitFact(
                    unitId, original->r_GlobalId, original->r_TribeId,
                    combat, value, worldX, worldY));
            }
            string reason;
            return HostDirectedOriginalOwnerLedger.TryBuild(
                groups, live, out ledger, out reason);
        }

    }
}
#endif
