// original mod ai: PRIVATE QA ONLY – two genuine original-engine snapshots, detached
// canonical MOD-AI offer evaluation. No gameplay owner publication, no goods
// mutation, no SaveData, no original-CPU diplomatic chore, no human consent.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using System.Threading;
using CrusaderDE;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private CanonicalTradeReducer _detached;
        private long _firstActualTick = -1;
        private float _firstFrame = -1f;
        private long _goodsWritesBefore;
        private int _originalAiCount;
        private int _actualNativeLordCount;
        private bool _ownedPause;
        private bool _originalNativePause;
        private bool _originalDirectorPause;
        private long _maximumGenuineTickSeen = -1;
        private int _originalMonitorBusySamples;
        private bool _directorFlagRestoreAttempted;
        private bool _directorFlagRestored;

        // Owner-lane recovery is NOT a native inventory mutation. The actual
        // original managed Director.paused flag is restored on the Unity lane
        // without acquiring the engine monitor, so an already-busy engine
        // cannot permanently prevent even the managed-pause restoration.
        // Native pause restoration remains an independent, bounded attempt.
        private static bool RestoreOriginalDirectorPauseWithoutEngine(
            DiplomacyFeature owner)
        {
            if (!owner._ownedPause)
                return true;
            if (owner._directorFlagRestoreAttempted)
                return owner._directorFlagRestored;
            owner._directorFlagRestoreAttempted = true;
            try
            {
                var director = Director.instance;
                if (director == null) return false;
                var field = typeof(Director).GetField(
                    "paused", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool))
                    return false;
                field.SetValue(director, owner._originalDirectorPause);
                owner._directorFlagRestored =
                    (bool)field.GetValue(director) ==
                    owner._originalDirectorPause;
                return owner._directorFlagRestored;
            }
            catch (Exception) { return false; }
        }

        // Original game pause is a test-only control, not a goods write.
        // Always attempt to restore BOTH original pause states before
        // completing or refusing this disposable-game experiment.
        // original map generation: obtain the actual active owner generation without taking
        // the native engine monitor. No cached preflight survives a map load.
        internal static bool TryObservedMapGeneration(out long generation)
        {
            generation = -1;
            if (Environment.GetEnvironmentVariable(
                "DIPLOMACY_QA_ORIGINAL_MOD_AI_DRYRUN") != "1")
                return false;
            DiplomacyFeature owner = QaOwner();
            if (owner == null || !Monitor.TryEnter(owner._gate))
                return false;
            try
            {
                if (!owner._mapActive || owner._mapDisabled ||
                    owner._mapBootstrapPending || owner._sessionGeneration <= 0)
                    return false;
                generation = owner._sessionGeneration;
                return true;
            }
            finally { Monitor.Exit(owner._gate); }
        }

        private static bool RestoreOriginalTestPause(DiplomacyFeature owner)
        {
            if(!owner._ownedPause) return true;
            try
            {
                bool ignored;
                if(!SetSoloTestPaused(owner._originalNativePause,out ignored))
                    return false;
                if(Director.instance==null)
                    return false;
                Director.instance.SetPausedState(
                    owner._originalDirectorPause);
                owner._ownedPause=false;
                return true;
            }
            catch(Exception) { return false; }
        }

        internal static bool TryReadOnlyOriginalModAiDraft(
            out bool finished, out string note)
        {
            finished=false;
            note=null;
            if (Environment.GetEnvironmentVariable(
                "DIPLOMACY_QA_ORIGINAL_MOD_AI_DRYRUN")!="1" ||
                Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST")!="1")
                return false;
            string scopeReason;
            if (!DisposableGameScope.IsEnabled(out scopeReason))
            {
                finished=true;
                note="status=dryrun_refused|reason=not_disposable_original_game";
                return true;
            }
            DiplomacyFeature owner=QaOwner();
            if (owner==null || !Monitor.TryEnter(owner._gate))
                return false;
            try
            {
                if (!owner._mapActive || owner._mapBootstrapPending)
                    return false;
                if (owner._mapDisabled || owner._tradeFaulted ||
                    owner._goodsTransfersUnavailable ||
                    owner._pendingPackage!=null || owner._nativeTransferProbe!=null)
                {
                    finished=true;
                    note="status=dryrun_refused|reason=original_game_quarantined";
                    return true;
                }
                if (owner._firstFrame<0f)
                    owner._firstFrame=UnityEngine.Time.realtimeSinceStartup;
                bool pastBudget=UnityEngine.Time.realtimeSinceStartup-
                    owner._firstFrame>=36f;
                // The engine thread's genuine original tick counter can be
                // sampled lock-free. An advanced counter is evidence of time,
                // NOT of a second safe native inventory snapshot.
                long readOnlyTick=owner.ReadSimulationTick();
                if (readOnlyTick>owner._maximumGenuineTickSeen)
                    owner._maximumGenuineTickSeen=readOnlyTick;
                IDisposable nativeLease;
                if (!TryEnterNativeInventorySection(out nativeLease))
                {
                    owner._originalMonitorBusySamples++;
                    if(pastBudget)
                    {
                        // Restore the original DIRECTOR pause state even when
                        // the native monitor cannot be acquired. Do not claim
                        // the native pause was restored if TryEnter fails.
                        bool directorRestored =
                            RestoreOriginalDirectorPauseWithoutEngine(owner);
                        bool nativePauseRestored=
                            RestoreOriginalTestPause(owner);
                        if (!nativePauseRestored &&
                            UnityEngine.Time.realtimeSinceStartup -
                                owner._firstFrame < 53f)
                            return false;
                        finished=true;
                        note="status=dryrun_incomplete" +
                             "|reason=original_engine_monitor_busy" +
                             "|first_original_native_tick="+owner._firstActualTick +
                             "|max_volatile_original_tick="+
                                 owner._maximumGenuineTickSeen +
                             "|real_second_native_snapshot_captured=false" +
                             "|engine_monitor_busy_samples="+
                                 owner._originalMonitorBusySamples +
                             "|director_pause_restored="+directorRestored +
                             "|native_pause_restored="+nativePauseRestored +
                             "|native_goods_writes=0" +
                             "|actual_human_ui_consent=false";
                        return true;
                    }
                    return false;
                }
                using(nativeLease)
                {
                    try
                    {
                        string sessionReason;
                        if (!IsSoloEngineSession(out sessionReason) ||
                            !owner.NativeAuthorized())
                        {
                            if(pastBudget)
                            {
                                finished=true;
                                note="status=dryrun_incomplete|reason=private_native_scope_not_ready";
                                return true;
                            }
                            return false;
                        }
                        // A detached reducer may NEVER replace the active one.
                        if(owner._canonical!=null || owner._canonicalSaved!=null ||
                           owner._canonicalTransition!=null)
                        {
                            // existing canonical owner: optional private-only READ of the existing
                            // original saved canonical owner. This path MUST
                            // NOT construct a second canonical Reducer, run
                            // ScheduleAutonomous or publish a new SaveData.
                            // Absent this opt-in the old denial is unchanged.
                            if(Environment.GetEnvironmentVariable(
                                    "DIPLOMACY_QA_EXISTING_CANONICAL_READONLY")=="1")
                            {
                                string existingOwnerReadback;
                                bool validated=owner.
                                    ObserveExistingCanonicalOwnerReadOnly(
                                        out existingOwnerReadback);
                                finished=true;
                                note=existingOwnerReadback;
                                if(!validated && note==null)
                                    note="status=existing_owner_readonly_refused|reason=unspecified";
                                return true;
                            }
                            finished=true;
                            note="status=dryrun_refused|reason=real_canonical_owner_already_active";
                            return true;
                        }
                        long nativeWrites=Interlocked.Read(ref _nativeWriteCalls);
                        if(owner._detached==null)
                        {
                            long tick=owner.ReadSimulationTick();
                            if(tick<1) return false;
                            CanonicalTradeSnapshot first=owner.CaptureCanonicalWorld(tick);
                            owner._originalAiCount=first.Players().Count(
                                x=>!x.Human && x.Alive);
                            owner._actualNativeLordCount=first.Players().Count(
                                x=> { CanonicalOriginalAiEconomy native;
                                     return !x.Human && x.Alive &&
                                         first.TryOriginalAiEconomy(x.Slot,out native) &&
                                         native.OriginalLordId>=1 &&
                                         native.OriginalLordId<=37; });
                            if(owner._originalAiCount<1 ||
                               owner._actualNativeLordCount!=
                                   owner._originalAiCount ||
                               first.LocalOfflineHumanSlot!=owner._localPlayer ||
                               first.MapGeneration<1)
                                throw new InvalidOperationException(
                                    "Original AIC players or solo human not proven");
                            owner._detached=new CanonicalTradeReducer(
                                first,MapGuid(owner._mapFingerprint,
                                owner._rosterFingerprint),SinglePlayerTerm);
                            owner._firstActualTick=tick;
                            owner._goodsWritesBefore=nativeWrites;
                            var director=Director.instance;
                            if(director==null)
                                throw new InvalidOperationException("Original Director not loaded");
                            var f=typeof(Director).GetField(
                                "paused",System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic);
                            if(f==null || f.FieldType!=typeof(bool))
                                throw new InvalidOperationException("Original Director pause ABI changed");
                            bool priorDirector=(bool)f.GetValue(director);
                            bool priorNative;
                            if(!SetSoloTestPaused(false,out priorNative))
                                throw new InvalidOperationException("Original private simulation unpause unavailable");
                            owner._originalNativePause=priorNative;
                            owner._originalDirectorPause=priorDirector;
                            owner._ownedPause=true;
                            director.SetPausedState(false);
                            return false;
                        }
                        if(nativeWrites!=owner._goodsWritesBefore)
                        {
                            finished=true;
                            note="status=dryrun_refused|reason=unexpected_native_goods_write";
                            return true;
                        }
                        long actualTick=owner.ReadSimulationTick();
                        if(actualTick<=owner._firstActualTick)
                        {
                            if(pastBudget)
                            {
                                bool restored=RestoreOriginalTestPause(owner);
                                finished=true;
                                note="status=dryrun_incomplete|reason=no_second_original_simulation_tick" +
                                     "|aic_slots="+owner._actualNativeLordCount+
                                     "|original_pause_restored="+restored+
                                     "|no_native_goods_writes=true";
                            }
                            return finished;
                        }
                        CanonicalTradeSnapshot second=owner.CaptureCanonicalWorld(actualTick);
                        // This is the existing genuine mod ai offer MOD-AI scheduler,
                        // called with two ACTUAL original game snapshots, but
                        // on a detached, non-saveable reducer. Its optional
                        // prepared transition is discarded, NEVER settled.
                        var transition=owner._detached.AdvanceSharedTick(
                            actualTick,
                            new AuthenticatedTradeTick(
                                new SignedTradeActivation(1),
                                Array.Empty<CanonicalTrustedHumanInput>()),
                            new CanonicalTradeSnapshotSource {World=second});
                        var offers=owner._detached.HumanOffersFor(
                            (byte)owner._localPlayer);
                        int genuineModCandidates=offers.Count(x=>
                            x.AiGenerated && x.RootId==0 &&
                            x.ParentId==0 && x.From!=owner._localPlayer &&
                            x.To==owner._localPlayer &&
                            x.State==CanonicalHumanConsentState.Pending);
                        // original offer blocker: one deterministic, detached-only explanation
                        // of why real original AIC/diplomacy/stock facts did or
                        // did not yield a MOD-AI offer. No new gameplay owner.
                        string originBlockerAudit=owner._detached.
                            ReadOnlyOriginalOfferBlockerAudit(second);
                        if (!originBlockerAudit.StartsWith(
                                "audit=original_readonly_success",
                                StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                "Original AI blocker audit missing trusted snapshot");
                        if(Interlocked.Read(ref _nativeWriteCalls)!=nativeWrites)
                            throw new InvalidOperationException(
                                "Unexpected live native goods mutation");
                        bool pauseWasRestored=RestoreOriginalTestPause(owner);
                        finished=true;
                        note="status=genuine_original_snapshot_mod_ai_dryrun" +
                            "|original_pause_restored="+pauseWasRestored +
                            "|original_private_simulation_resumed_for_test=true" +
                            "|first_actual_native_tick="+owner._firstActualTick +
                            "|second_actual_native_tick="+actualTick +
                            "|engine_monitor_busy_samples="+
                                owner._originalMonitorBusySamples +
                            "|max_volatile_original_tick="+
                                owner._maximumGenuineTickSeen +
                            "|real_ai_aic_slots="+owner._actualNativeLordCount +
                            "|mod_generated_pending_human_offers="+genuineModCandidates +
                            "|detached_only=true|gameplay_offer_committed=false" +
                            "|actual_human_ui_consent=false" +
                            "|native_goods_writes=0" +
                            "|canonical_gameplay_owner_unchanged=true" +
                            "|original_cpu_diplomatic_chore_proven=false" +
                            "|native_goods_authorized=false" +
                            "|mp_authorized=false" +
                            "|detached_transition_discarded="+(transition!=null) +
                            "|" + originBlockerAudit;
                        return true;
                    }
                    catch(Exception e)
                    {
                        bool pauseWasRestored=RestoreOriginalTestPause(owner);
                        finished=true;
                        note="status=dryrun_incomplete|reason="+e.GetType().Name +
                            "|original_pause_restored="+pauseWasRestored +
                            "|native_goods_authorized=false|mp_authorized=false";
                        return true;
                    }
                }
            }
            finally { Monitor.Exit(owner._gate); }
        }
    }
}
#endif
