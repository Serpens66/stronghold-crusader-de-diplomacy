// Private read-only original tribe order entry telemetry. No original AI
// requester, pending queue, caller RVA, acceptance or ACK is inferred here.
// The synchronous SHCDESE Pre/Post notifications provide only the arguments
// and phase of the original native order *entry*, regardless of its caller.
#if DIPLOMACY_PRIVATE_TESTS && ORIGINAL_CPU_TRIBE_ORDER_PROBE
using System;
using System.Collections.Generic;
using System.Threading;
using CrusaderDE;
using R3;
using SHCDESE.API;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Tribes;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private const int OriginalCpuTribeRecordBudget = 64;
        private const int OriginalCpuTribeMaximumLogLines = 64;

        // Data published exclusively by the subscribed managed event callback.
        // All slots are allocated BEFORE native game code can reach the hook.
        private sealed class OriginalCpuTribeEntry
        {
            internal int Published, Kind, Phase, TribeId;
            // Read the ORIGINAL SDK detour fields only; not an ACK.
            internal int OriginalSkipFlag;
            internal long OriginalReturnValue;
            internal int Argument1, Argument2, Argument3, Argument4, Argument5;
            internal int ManagedThreadId, SimulationThreadId;
            internal long OriginalTick, Generation, Epoch;
            internal int HeldOriginalMonitor;
            // A later original-monitor identity sample is NEVER caller proof.
            internal int LaterNativeOwner, LaterMemberCount;
            internal uint LaterNativeTribeGlobalId;
            internal ulong LaterMemberPairsDigest;
            internal long LaterNativeReadTick;
            internal int LaterIdentityState;
        }

        private sealed class OriginalCpuTribeRun
        {
            internal readonly OriginalCpuTribeEntry[] Entries =
                new OriginalCpuTribeEntry[OriginalCpuTribeRecordBudget];
            internal int Enabled, Reserved, Dropped;
            internal IDisposable MoveSubscription, TargetSubscription;
            internal OriginalCpuTribeRun()
            {
                for (int i = 0; i < Entries.Length; i++)
                    Entries[i] = new OriginalCpuTribeEntry();
            }
        }

        private static OriginalCpuTribeRun _originalCpuTribeRun;

        internal static bool TryStartOriginalCpuTribeReadOnly(out string reason)
        {
            reason = "private_readonly_scope_unavailable";
            try
            {
                string scope;
                if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" ||
                    Environment.GetEnvironmentVariable("DIPLOMACY_ORIGINAL_CPU_TRIBE_OBSERVER") != "1" ||
                    !DisposableGameScope.IsEnabled(out scope) ||
                    _originalCpuTribeRun != null)
                    return false;
                // Hard-pin exact original SDK managed body identity before
                // subscribing. Normal builds do not compile this entire file.
                if (typeof(TribeR3EventHooks).Assembly.ManifestModule.ModuleVersionId !=
                        new Guid("aab7af1d-dabf-4f55-a7a6-dd67c8f06853") ||
                    typeof(GameData).Assembly.ManifestModule.ModuleVersionId !=
                        new Guid("29ade4ef-f587-422f-a1eb-f81dff066e0a"))
                {
                    reason = "original_managed_ABI_identity_mismatch";
                    return false;
                }
                var run = new OriginalCpuTribeRun();
                try
                {
                    // Exact original R3 callbacks, not replacement native hooks.
                    // No event arguments or native return values are assigned.
                    run.MoveSubscription = ObservableSubscribeExtensions.Subscribe(
                        TribeR3EventHooks.OnTribeIssueOrderMoveHere.Observable,
                        OriginalCpuTribeMoveEvent);
                    run.TargetSubscription = ObservableSubscribeExtensions.Subscribe(
                        TribeR3EventHooks.OnTribeIssueOrderWithTarget.Observable,
                        OriginalCpuTribeTargetEvent);
                    _originalCpuTribeRun = run;
                    Volatile.Write(ref run.Enabled, 1);
                    reason = "original_entry_callbacks_subscribed_no_caller_proof";
                    return true;
                }
                catch (Exception)
                {
                    Volatile.Write(ref run.Enabled, 0);
                    DisposeOriginalCpuTribeSubscription(run.TargetSubscription);
                    DisposeOriginalCpuTribeSubscription(run.MoveSubscription);
                    reason = "original_entry_subscription_failed";
                    return false;
                }
            }
            catch (Exception)
            {
                reason = "private_readonly_subscription_unavailable";
                return false;
            }
        }

        // Both handlers run synchronously INSIDE the original SHCDESE detour.
        // Never acquire native/threadLock, call SDK/game methods, log,
        // allocate a DTO, block, or mutate args. Reading the managed event's
        // supplied detour skip/return fields is NOT a native ABI call or ACK.
        // A fault cannot be permitted to propagate through this callback.
        private static void OriginalCpuTribeMoveEvent(TribeIssueOrderMoveHereEventArgs e)
        {
            try
            {
                if (e != null)
                    ObserveOriginalCpuTribeEntry(1, (int)e.Phase,
                        e.TribeId, e.TileX, e.TileY,
                        (int)e.IsPatrolPath, e.IsNewOrder ? 1 : 0, (int)e.MoveType,
                        e.SkipOriginalFunction ? 1 : 0, e.ReturnValue);
            }
            catch (Exception)
            {
                // Diagnostic only; native processing is not replaced.
            }
        }

        private static void OriginalCpuTribeTargetEvent(TribeIssueOrderWithTargetEventArgs e)
        {
            try
            {
                if (e != null)
                    ObserveOriginalCpuTribeEntry(2, (int)e.Phase,
                        e.TribeId, (int)e.AICommand, e.TargetValue1,
                        e.TargetValue2, e.a6, 0,
                        e.SkipOriginalFunction ? 1 : 0, e.ReturnValue);
            }
            catch (Exception)
            {
                // No exception must escape an original native invocation.
            }
        }

        private static void ObserveOriginalCpuTribeEntry(
            int kind, int phase, int tribeId, int first, int second,
            int third, int fourth, int fifth,
            int originalSkip, long originalResult)
        {
            // Only primitive managed stamps: no native calls, allocations,
            // stack unwinding, dynamic lookup, or original event mutators.
            OriginalCpuTribeRun run = Volatile.Read(ref _originalCpuTribeRun);
            if (run == null || Volatile.Read(ref run.Enabled) != 1)
                return;
            int index = Interlocked.Increment(ref run.Reserved) - 1;
            if (index < 0 || index >= run.Entries.Length)
            {
                Interlocked.Increment(ref run.Dropped);
                return;
            }
            OriginalCpuTribeEntry entry = run.Entries[index];
            entry.Kind = kind;
            entry.Phase = phase;
            entry.TribeId = tribeId;
            entry.Argument1 = first;
            entry.Argument2 = second;
            entry.Argument3 = third;
            entry.Argument4 = fourth;
            entry.Argument5 = fifth;
            entry.OriginalSkipFlag = originalSkip;
            entry.OriginalReturnValue = originalResult;
            entry.ManagedThreadId = Environment.CurrentManagedThreadId;
            entry.HeldOriginalMonitor = IsNativeDiplomacySectionHeld ? 1 : 0;
            DiplomacyFeature owner = Volatile.Read(ref _activeOwner);
            if (owner == null)
            {
                entry.OriginalTick = -1L;
                entry.Generation = -1L;
                entry.Epoch = -1L;
                entry.SimulationThreadId = 0;
            }
            else
            {
                entry.OriginalTick = owner.ReadSimulationTick();
                entry.Generation = Volatile.Read(ref owner._sessionGeneration);
                entry.Epoch = Volatile.Read(ref owner._mapEpoch);
                entry.SimulationThreadId = Volatile.Read(ref owner._simulationThreadId);
            }
            // Publish last; an incomplete callback never becomes a sample.
            Volatile.Write(ref entry.Published, 1);
        }

        private static void DisposeOriginalCpuTribeSubscription(IDisposable sub)
        {
            if (sub == null)
                return;
            try { sub.Dispose(); }
            catch (Exception) { } // Never change original dispatch.
        }

        // Drain only on the already running Unity/headless QA lane.
        // Read-only later identity is NOT same-callback native CPU provenance.
        internal static void StopOriginalCpuTribeReadOnly(string reason)
        {
            OriginalCpuTribeRun run =
                Interlocked.Exchange(ref _originalCpuTribeRun, null);
            if (run == null)
                return;
            Volatile.Write(ref run.Enabled, 0);
            DisposeOriginalCpuTribeSubscription(run.TargetSubscription);
            DisposeOriginalCpuTribeSubscription(run.MoveSubscription);
            run.TargetSubscription = null;
            run.MoveSubscription = null;

            int observed = 0, laterAi = 0, notPublished = 0;
            int available = Math.Min(Math.Max(0, Volatile.Read(ref run.Reserved)),
                OriginalCpuTribeRecordBudget);
            // Every original callback sample is classified later. An unknown
            // native identity never acquires an AI-owner label.
            TryResolveLaterOriginalCpuTribeIdentities(run, available);
            for (int i = 0; i < available && i < OriginalCpuTribeMaximumLogLines; i++)
            {
                OriginalCpuTribeEntry sample = run.Entries[i];
                if (Volatile.Read(ref sample.Published) != 1)
                {
                    notPublished++;
                    continue;
                }
                observed++;
                if (sample.LaterIdentityState == 1)
                    laterAi++;
                // An original native order event is useful even when a
                // LATER owner snapshot cannot be safely resolved. Unknown
                // does not become actor identity, AI requester or ACK.
                try
                {
                    HeadlessQaDriver.Note(
                        "status=original_cpu_tribe_order_entry|entry=" + i +
                        "|kind=" + (sample.Kind == 1 ? "move_here" : "with_target") +
                        "|phase=" + (sample.Phase == (int)EventHookPhase.Pre ?
                            "pre" : sample.Phase == (int)EventHookPhase.Post ?
                            "post" : "unknown") +
                        "|tribe_id=" + sample.TribeId +
                        "|argument_1=" + sample.Argument1 +
                        "|argument_2=" + sample.Argument2 +
                        "|argument_3=" + sample.Argument3 +
                        "|argument_4=" + sample.Argument4 +
                        "|argument_5=" + sample.Argument5 +
                        "|original_detour_skip=" +
                            (sample.OriginalSkipFlag == 1) +
                        "|original_detour_return=" +
                            sample.OriginalReturnValue +
                        "|native_ack=false" +
                        "|argument_layout=" + (sample.Kind == 1 ?
                            "tileX_tileY_isPatrol_isNew_moveType" :
                            "AICommand_target1_target2_a6_unused") +
                        "|static_reference_native_entry_rva=" +
                            (sample.Kind == 1 ? "0x11B520" : "0x11E960") +
                        "|sampled_simulation_tick_counter=" + sample.OriginalTick +
                        "|tick_sample_atomic_with_command=false" +
                        "|managed_callback_thread=" + sample.ManagedThreadId +
                        "|native_sim_managed_thread_observed=" + sample.SimulationThreadId +
                        "|original_monitor_held_at_callback=" +
                            (sample.HeldOriginalMonitor == 1) +
                        "|callback_generation=" + sample.Generation +
                        "|callback_epoch=" + sample.Epoch +
                        "|later_original_AI_tribe_verified=" +
                            (sample.LaterIdentityState == 1) +
                        "|later_native_original_ai_owner=" +
                            (sample.LaterIdentityState == 1 ?
                                sample.LaterNativeOwner.ToString() : "unknown") +
                        "|later_original_tribe_global_id=" +
                            (sample.LaterIdentityState == 1 ?
                                sample.LaterNativeTribeGlobalId.ToString() : "unknown") +
                        "|later_native_identity_tick=" +
                            (sample.LaterIdentityState == 1 ?
                                sample.LaterNativeReadTick.ToString() : "unavailable") +
                        "|later_original_group_members=" +
                            (sample.LaterIdentityState == 1 ?
                                sample.LaterMemberCount.ToString() : "unavailable") +
                        "|later_original_member_pairs_fnv64=" +
                            (sample.LaterIdentityState == 1 ?
                                sample.LaterMemberPairsDigest.ToString("X16") : "unavailable") +
                        "|same_tick_identity_at_callback_proven=false" +
                        "|worldwide_unit_global_id_uniqueness_proven=false" +
                        "|event_arguments_can_be_modified_by_other_subscribers=true" +
                        "|actual_loaded_native_body_sha_measured=false" +
                        "|native_cpu_caller_RVA=unknown" +
                        "|native_raid_producer_path_to_entry_static_candidate=" +
                            (sample.Kind == 1 ||
                                (sample.Kind == 2 && sample.Argument1 == 9)) +
                        "|callback_belongs_to_specific_CPU_caller_unproven=true" +
                        "|event_phase_not_command_acceptance=true" +
                        "|native_CPU_requester_proven=false" +
                        "|pending_original_queue_proven=false" +
                        "|original_host_authority_proven=false" +
                        "|native_ACK=false");
                }
                catch (Exception) { }
            }
            try
            {
                HeadlessQaDriver.Note(
                    "status=original_cpu_tribe_observer_finished" +
                    "|scope=read_only_original_entry_only" +
                    "|sample_budget=64|native_callsite_captured=false" +
                    "|observer_records=" + observed +
                    "|later_original_AI_tribe_samples=" + laterAi +
                    "|records_without_safe_identity=" + (observed - laterAi) +
                    "|callbacks_dropped=" + Volatile.Read(ref run.Dropped) +
                    "|unpublished=" + notPublished +
                    "|reason=" + SafeOriginalCpuProbeToken(reason) +
                    "|original_caller_proven=false" +
                    "|original_pending_queue_proven=false" +
                    "|native_ack=false|AI_requester_proven=false" +
                    "|model_restore_verified=false" +
                    "|normal_MP_release=false");
            }
            catch (Exception) { }
        }

        private static string SafeOriginalCpuProbeToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 48)
                return "unspecified";
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') ||
                      (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '_'))
                    return "unspecified";
            return value;
        }

        // This runs ONLY after removing subscriptions, on the managed QA
        // frame. It cannot retroactively prove native caller/owner at the
        // earlier synchronous callback. No cached control aliases are used.
        private static unsafe void TryResolveLaterOriginalCpuTribeIdentities(
            OriginalCpuTribeRun run, int count)
        {
            if (count <= 0 || run == null)
                return;
            DiplomacyFeature owner = Volatile.Read(ref _activeOwner);
            if (owner == null || !Monitor.TryEnter(owner._gate))
                return;
            try
            {
                if (!owner._mapActive || owner._mapDisabled ||
                    owner._mapBootstrapPending || owner._inventoryRestorePending ||
                    owner._activeSave != null)
                    return;
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return;
                using (lease)
                {
                    GameData game = GameData.Instance;
                    var state = game == null ? null : game.lastGameState;
                    GameTribeManagerAPI tribes = GameTribeManagerAPI.Instance;
                    GameUnitManagerAPI units = GameUnitManagerAPI.Instance;
                    GamePlayerManagerAPI players = GamePlayerManagerAPI.Instance;
                    if (state == null || tribes == null || units == null ||
                        players == null || !IsNativeDiplomacySectionHeld)
                        return;
                    long nowTick = owner.ReadSimulationTick();
                    long generation = Volatile.Read(ref owner._sessionGeneration);
                    long epoch = Volatile.Read(ref owner._mapEpoch);
                    if (generation < 1 || epoch < 1 || nowTick < 0)
                        return;
                    // At most 64 observed command entries, then one
                    // original-monitor snapshot, no wait or dispatched action.
                    for (int i = 0; i < count; i++)
                    {
                        OriginalCpuTribeEntry entry = run.Entries[i];
                        if (Volatile.Read(ref entry.Published) != 1 ||
                            entry.TribeId < 1 ||
                            entry.Generation != generation ||
                            entry.Epoch != epoch ||
                            entry.OriginalTick < 0 ||
                            nowTick < entry.OriginalTick)
                            continue;
                        GameTribe* nativeTribe;
                        if (!tribes.TryGetTribeById(entry.TribeId, out nativeTribe) ||
                            nativeTribe == null ||
                            nativeTribe->r_AliveState != AliveState.IsAlive ||
                            nativeTribe->r_GlobalId == 0)
                            continue;
                        int actualOwner = nativeTribe->r_PlayerIdOwner;
                        if (actualOwner < 1 || actualOwner > 8 ||
                            state.is_valid_player(actualOwner) ||
                            !state.is_skirmish_player(actualOwner) ||
                            !players.IsAIPlayer(actualOwner))
                            continue;
                        // The original group-ID reader enumerates native
                        // bitfield members; verify each live unit's ORIGINAL
                        // r_TribeId and positive in-group global identity.
                        var members = new List<int>();
                        if (!tribes.GetUnits(entry.TribeId, members) ||
                            members.Count < 1 || members.Count > 512)
                            continue;
                        var unitIdSet = new HashSet<int>();
                        var globalIdSet = new HashSet<uint>();
                        bool valid = true;
                        ulong memberPairsDigest = 14695981039346656037UL;
                        foreach (int unitId in members)
                        {
                            GameUnit* nativeUnit;
                            if (unitId < 1 || !unitIdSet.Add(unitId) ||
                                !units.TryGetUnitById(unitId, out nativeUnit) ||
                                nativeUnit == null ||
                                nativeUnit->r_AliveState != AliveState.IsAlive ||
                                nativeUnit->r_TribeId != entry.TribeId ||
                                nativeUnit->r_GlobalId == 0 ||
                                !globalIdSet.Add(nativeUnit->r_GlobalId))
                            {
                                valid = false;
                                break;
                            }
                            // A bounded later-snapshot fingerprint only; this
                            // digest has no native caller/queue authority.
                            unchecked
                            {
                                memberPairsDigest ^= (uint)unitId;
                                memberPairsDigest *= 1099511628211UL;
                                memberPairsDigest ^= nativeUnit->r_GlobalId;
                                memberPairsDigest *= 1099511628211UL;
                            }
                        }
                        if (!valid)
                            continue;
                        entry.LaterNativeOwner = actualOwner;
                        entry.LaterMemberCount = members.Count;
                        entry.LaterMemberPairsDigest = memberPairsDigest;
                        entry.LaterNativeTribeGlobalId = nativeTribe->r_GlobalId;
                        entry.LaterNativeReadTick = nowTick;
                        entry.LaterIdentityState = 1;
                    }
                }
            }
            catch (Exception)
            {
                // Only managed QA frame. A failed later identity is unknown,
                // not a permission and not a gameplay failure.
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }
    }
}
#endif
