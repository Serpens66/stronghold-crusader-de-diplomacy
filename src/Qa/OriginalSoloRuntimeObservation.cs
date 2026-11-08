// original runtime observation: QA-only truthful original-game runtime evidence reader.
// NO native mutation, no packet, no save call, no rights elevation.
// Only call from explicit, isolated, file-commanded original-game LOAD_PROBE.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using System.Threading;
using HarmonyLib;
using SHCDESE.API;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // This is a diagnostic snapshot only, NOT a native goods authorization.
        // False and unknown states remain false. A sampled warehouse is not a
        // production quote, a prior save is not an atomic commit receipt.
        internal static bool TryObserveOriginalReadOnly(out string report)
        {
            report = null;
            if (Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_RUNTIME_READONLY") != "1" ||
                Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1")
                return false;

            string scope;
            if (!DisposableGameScope.IsEnabled(out scope))
            {
                report = "status=refused|reason=private_scope_unavailable|native_writes=0";
                return true;
            }

            DiplomacyFeature owner = QaOwner();
            if (owner == null)
                return false;

            // Nonblocking lock order. Never wait for the original engine
            // worker or a SaveData callback while holding the feature gate.
            if (!Monitor.TryEnter(owner._gate))
                return false;
            try
            {
                if (!owner._mapActive || owner._mapBootstrapPending || owner._mapDisabled)
                    return false;
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return false;
                using (lease)
                {
                    try
                    {
                        string nativeWhy, managedWhy, rawraWhy, apiWhy, soloWhy, abiWhy, warehouseWhy;
                        bool native = SerpsRawraCompat.AttestedLoadedNativeEngine(out nativeWhy);
                        bool gameManaged = VerifiedManagedSurface.VerifyGameAssembly(out managedWhy);
                        bool rawraManaged = VerifiedManagedSurface.VerifyRawraAssembly(out rawraWhy);
                        bool api = SerpsRawraCompat.CheckLoadedBepInExDependencies(out apiWhy);
                        bool solo = IsSoloEngineSession(out soloWhy);
                        bool abi = NativeInventoryContract.TryValidateUnderLease(out abiWhy);
                        bool warehouse = OriginalSoloWarehouseCapacityReader.ProvenanceReady(out warehouseWhy);
                        bool saveHooks = false;
                        if (owner._saveBridgeInstalled && owner._saveHandlerRegistered &&
                            owner._saveTarget != null && owner._harmony != null)
                        {
                            var hooks = Harmony.GetPatchInfo(owner._saveTarget);
                            saveHooks = hooks != null &&
                                hooks.Prefixes.Any(x => x.owner == owner._harmony.Id && x.PatchMethod.Name == "SavePrefix") &&
                                hooks.Postfixes.Any(x => x.owner == owner._harmony.Id && x.PatchMethod.Name == "SavePostfix") &&
                                hooks.Finalizers.Any(x => x.owner == owner._harmony.Id && x.PatchMethod.Name == "SaveFinalizer");
                        }
                        bool currentSave = owner._coherentSaveGeneration == owner._sessionGeneration &&
                            owner._sessionGeneration > 0 && !owner._saveSuppressed &&
                            owner._saveFailurePending == null;
                        bool clean = !owner._unattestedSavedNativeTrade && !owner._tradeFaulted &&
                            !owner._inventoryRestorePending && !owner._goodsTransfersUnavailable &&
                            owner._nativeTransferProbe == null &&
                            owner._pendingPackage == null && owner._pendingTeamTransition == null;
                        bool atomicIdle = owner._atomicBoundary != null &&
                            !owner._atomicBoundary.Quarantined &&
                            !owner._atomicBoundary.SaveActive &&
                            !owner._atomicBoundary.MutationActive;
                        bool mapIdentified = !String.IsNullOrEmpty(owner._mapFingerprint) &&
                            !String.IsNullOrEmpty(owner._rosterFingerprint);
                        bool sampled = false;
                        string sampleReason = "not_checked";
                        int[] originalBefore, capacities;
                        if (native && gameManaged && rawraManaged && api && solo && abi &&
                            warehouse && mapIdentified && owner._localPlayer == 1)
                        {
                            // Fixed read-only example for the known pinned
                            // Kratersee solo baseline. NOT a chosen user quote.
                            sampled = owner.TryCaptureFourNativeCells(2, 1, 10, 13,
                                out originalBefore, out capacities, out sampleReason);
                        }
                        bool ownerAuthority = owner.ObservedPrivateNativeAuthority();
                        // Exercise the EXACT release-compiled original solo release observation
                        // method against the same real original-engine lease.
                        // Private proof does NOT authorize normal-release writes.
                        string soloReleaseReason;
                        bool soloReleasePrerequisites =
                            owner.ObserveNormalSoloAdmission(out soloReleaseReason);
                        var facts = new SoloFacts();
                        facts.NetworkTransportReported = GameNetworkAPI.IsNetworkedEnvironment();
                        facts.SessionGeneration = owner._sessionGeneration;
                        facts.OriginalSaveGeneration = owner._coherentSaveGeneration;
                        facts.LocalHumanSlot = owner._localPlayer;
                        // No actual human-approved quote exists in a passive load.
                        facts.RequesterSlot = 0;
                        facts.RecipientSlot = 0;
                        facts.OfferedGood = 0;
                        facts.RequestedGood = 0;
                        facts.OfferedAmount = 0;
                        facts.RequestedAmount = 0;
                        facts.TradeId = 0;
                        facts.ExactOriginalExeAndLoadedNativeFile = native;
                        facts.LoadedNativeRuntimeBytesVerified = abi;
                        facts.ManagedGameAndRawraMethodPinsVerified = gameManaged && rawraManaged;
                        facts.CorrectManagedApiDependenciesLoaded = api;
                        facts.UniqueOriginalEngineMonitorOwned = IsNativeDiplomacySectionHeld;
                        // A caller-thread assumption alone is not an independent
                        // authorization-grade game-thread attestation.
                        facts.VerifiedGameThread = false;
                        facts.ExactlyOneLocalHuman = solo;
                        facts.NoOtherHumanOrRemoteSteamPeer = solo;
                        facts.AtLeastOneLiveAiOpponent = solo;
                        facts.LiveRosterAndNativePlayerIdsAgree = solo;
                        facts.ActiveMapGenerationUnchanged = mapIdentified && owner._mapActive;
                        // A loaded baseline without a current SAVE receipt is not
                        // proof of current durable ownership or a committed quote.
                        facts.SaveBelongsToSameMapAndRoster = false;
                        facts.AllThreeOwnedSaveHooksInstalled = saveHooks;
                        facts.SameGenerationOriginalSaveReceiptObserved = currentSave;
                        facts.SaveIsNotSuppressedOrFailed =
                            !owner._saveSuppressed && owner._saveFailurePending == null;
                        facts.NoUnattestedPreparedOrReplayableTrade = clean;
                        facts.NoOtherPendingNativeMutation = clean;
                        facts.OriginalAtomicBoundaryIdleAndNotQuarantined = atomicIdle;
                        // An unrelated 4-cell sample is not a live quote check.
                        facts.AllFourNativeCellsAndCapacitiesRecheckedUnderLease = false;
                        facts.OriginalMarketTermsAndTradePairRechecked = false;
                        facts.HumanConsentIsOriginalProductionDecision = false;
                        facts.TradeIdNotPreviouslyCommittedOrReserved = false;
                        string denial;
                        bool criteria = SoloPreflight.CheckPrerequisitesOnly(facts, out denial);
                        bool commit = SoloPreflight.AuthorizeRealGoodsMutation(facts);
                        // Even if all environmental observations succeed, this
                        // path must NEVER produce a trading capability.
                        if (criteria || commit)
                        {
                            report = "status=refused|reason=unexpected_synthetic_authority|native_writes=0";
                            return true;
                        }
                        // one shot admission is the EXACT normal-release-compiled one-shot
                        // read-only classifier. This passive original game
                        // scenario has NO authentic quoted original decision.
                        string oneShotAdmissionReason;
                        bool oneShotCandidate=owner.ObserveUnapprovedOneShotReadOnly(
                            out oneShotAdmissionReason);
                        if (oneShotCandidate ||
                            OriginalOneShotAdmission.AuthorizeNativeGoodsCommit(null) ||
                            OriginalOneShotAdmission.AuthorizeSteamOrMultiplayer(null))
                        {
                            report = "status=refused|reason=unexpected_trade_permission|native_writes=0";
                            return true;
                        }
                        report = "status=original_runtime_observed" +
                            "|readonly_one_shot_grant=false" +
                            "|reason=" + oneShotAdmissionReason +
                            "|private_scope=true" +
                            "|native_loaded_sha=" + native +
                            "|managed_game_pins=" + gameManaged +
                            "|managed_rawra_pins=" + rawraManaged +
                            "|api_dependencies=" + api +
                            "|original_engine_monitor_owned=" + IsNativeDiplomacySectionHeld +
                            "|solo_registered_human=" + solo +
                            "|original_native_runtime_abi=" + abi +
                            "|original_warehouse_reader=" + warehouse +
                            "|four_cell_readonly_example=" + sampled +
                            "|save_hooks_present=" + saveHooks +
                            "|current_generation_save_receipt=" + currentSave +
                            "|atomic_boundary_idle=" + atomicIdle +
                            "|quarantine_clear=" + clean +
                            "|map_roster_present=" + mapIdentified +
                            "|private_authority_for_actual_trade=" + ownerAuthority +
                            "|release_readiness=" + soloReleasePrerequisites +
                            "|release_denial_reason=" + soloReleaseReason +
                            "|native_full_function_layout_reviewed_in_original=true" +
                            "|normal_original_callback_archive_readback=" +
                                owner.HasPassiveOriginalSaveReadback() +
                            "|synthetic_prerequisites_passed=false" +
                            "|normal_SP_write_authority=false" +
                            "|consent_or_quote_observed=false" +
                            "|save_durable_commit_for_this_probe=false" +
                            "|original_game_only_read_operations=true" +
                            "|native_writes=0";
                        return true;
                    }
                    catch (Exception e)
                    {
                        report = "status=refused|reason=runtime_observer_" +
                            e.GetType().Name + "|native_writes=0";
                        return true;
                    }
                }
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }
    }
}
#endif
