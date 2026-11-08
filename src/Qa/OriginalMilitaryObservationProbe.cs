// Private, explicit headless QA only. Observation never grants game authority.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using CrusaderDE;
using HarmonyLib;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ObservedNativeAiAidReceipt
    {
        internal int SourcePid, Donor, Recipient, Good, Quantity, Day, DonorBefore, RecipientBefore, DonorAfter, RecipientAfter;
        internal long Generation, Tick, NativeWriteCalls;
        internal ulong Id;
        internal string Map, Roster;
    }


    // Read-only copied positions of ACTUAL current original AI combat units.
    // This is not an acknowledgment that any requested order CAUSED movement.
    internal sealed class NativeAiUnitPosition
    {
        internal uint GlobalId;
        internal int Owner, TribeId, WorldX, WorldY;
    }


    internal sealed class OriginalAiNativeTribeTarget
    {
        internal uint GlobalId;
        internal int Owner, AttackTargetOwner, ObjectiveX, ObjectiveY;
    }

    internal sealed class MilitaryQaOrderObservation
    {
        internal int Consulted, Opcode, Target, Requester, NativeKind, NativeTarget, NativeRequester, Reapplications;
        // Additional private measurement ONLY. Normalized native fields above
        // remain the sole inputs to existing military activity decisions.
        internal int RawNativeKind, RawNativeOrderPlayerField, RawNativeFromPlayerField;
        internal bool RawNativeAvailable;
        internal OriginalAllyRawSource RawNativeSource;
        internal OriginalAllyTargetProjection NativeTargetProjection;
        internal string NativeReadReason;
        internal bool NativeReadable, NativeDecision, ObservedActive;
        internal bool PolicyDecision, DecisionUnbound;
        internal AllyOrderActivityRelation Activity;
        internal bool Queued, InFlight, OriginalHumanRetained;
        internal bool? Accepted;
        internal int EndedDay;
        internal long ReplySequence;
    }

    internal sealed class MilitaryQaWorldSnapshot
    {
        internal long Generation, Tick, NativeWriteCalls;
        internal int Day, Local;
        internal string Map, Roster;
        internal int[] Ai, Humans;
        internal bool[, ] Alliances;
        internal int[] OriginalNativeTeams;
        internal int[] OriginalKeepDoorX, OriginalKeepDoorY;
        internal bool[] OriginalKeepDoorKnown;
        internal Dictionary<uint,OriginalAiNativeTribeTarget> OriginalAiTribeTargets;
        internal Dictionary<uint,NativeAiUnitPosition> NativeAiUnitPositions;
        internal int[, ] Goods;
        internal ObservedNativeAiAidReceipt Aid;
        internal MilitaryQaOrderObservation[] Military;
        internal byte[] AidLedger;
        internal Dictionary<ulong, int> AidWriteAttempts;
    }

    internal sealed partial class DiplomacyFeature
    {

        // This is a private mod-owned supplementary order for a REAL
        // human Ally_Orders(ATTACK) request, not a claim that original CPU
        // itself issued this order. An idle original AI group is untouched
        // without ALL authentic actor/target/safety/response observations.
        private long _attemptGeneration = -1, _attemptEpoch = -1;
        private int _attemptTribeId, _targetX, _targetY;
        private uint _attemptTribeGlobalId;
        private long _attemptTick;
        private static bool BoundHumanAttackEligible(
            int local, int ally, int enemy, int humanTeam, int allyTeam,
            int enemyTeam, bool oneHumanAndHostSolo, bool originalPost,
            bool newerMatchingOriginalTuple, bool sameGenerationAndMap,
            bool safeNativeSaveBoundary, bool originalTribeAndUnits,
            bool nativeEnemyKeepTile, int donorPower, int groupPower,
            int memberCount)
        {
            return local >= 1 && local <= 8 && ally >= 1 && ally <= 8 &&
                enemy >= 1 && enemy <= 8 &&
                local != ally && local != enemy && ally != enemy &&
                humanTeam > 0 && allyTeam == humanTeam &&
                enemyTeam > 0 && enemyTeam != humanTeam &&
                oneHumanAndHostSolo && originalPost &&
                newerMatchingOriginalTuple && sameGenerationAndMap &&
                safeNativeSaveBoundary && originalTribeAndUnits &&
                nativeEnemyKeepTile &&
                memberCount >= 1 && memberCount <= 128 &&
                donorPower >= 4 && groupPower > 0 &&
                (long)groupPower * 4 <= donorPower;
        }

        internal static unsafe bool TryIssueAcceptedHumanNativeAttack(
            MilitaryQaWorldSnapshot observed, int originalAlly,
            int originalEnemy, out string reason)
        {
            reason = "private_human_native_canary_not_armed";
            if (!ExplicitQaScope ||
                Environment.GetEnvironmentVariable(
                    "DIPLOMACY_NATIVE_HUMAN_ALLY_GOAL_CANARY") != "1" ||
                observed == null || observed.Local < 1 ||
                observed.Local > 8 || originalAlly < 1 || originalAlly > 8 ||
                originalEnemy < 1 || originalEnemy > 8 ||
                observed.Military == null || observed.Military.Length != 9)
                return false;
            var prior = observed.Military[originalAlly];
            reason = "original_human_bound_attack_not_accepted";
            if (prior == null || prior.Opcode != 1 ||
                prior.Requester != observed.Local ||
                prior.Consulted != originalAlly ||
                prior.Target != originalEnemy ||
                !prior.NativeDecision || prior.PolicyDecision ||
                prior.DecisionUnbound || prior.Accepted != true ||
                prior.ReplySequence <= 0 || !prior.RawNativeAvailable ||
                prior.RawNativeSource !=
                    OriginalAllyRawSource.CopiedOriginalPlayStateOrderArrays ||
                prior.RawNativeKind != 1 ||
                prior.RawNativeFromPlayerField != observed.Local ||
                prior.RawNativeOrderPlayerField != originalEnemy ||
                prior.Activity != AllyOrderActivityRelation.MatchingLaterSnapshot)
                return false;
            DiplomacyFeature owner = QaOwner();
            if (owner == null || !Monitor.TryEnter(owner._gate))
            {
                reason = "current_original_map_gate_busy";
                return false;
            }
            try
            {
                // native gate predicates: identical original gate predicates; name the REAL
                // refusal instead of treating unavailability as a command
                // rejection. Never force one condition to succeed.
                reason = "native_original_map_not_active";
                if (!owner._mapActive) return false;
                reason = "original_map_disabled";
                if (owner._mapDisabled) return false;
                reason = "original_map_bootstrap_pending";
                if (owner._mapBootstrapPending) return false;
                reason = "original_inventory_restore_pending";
                if (owner._inventoryRestorePending) return false;
                reason = "original_SaveData_owner_still_active";
                if (owner._activeSave != null) return false;
                reason = "original_save_suppression_active";
                if (owner._saveSuppressed) return false;
                reason = "original_pending_trade";
                if (owner._pendingPackage != null) return false;
                reason = "original_native_transfer_pending";
                if (owner._nativeTransferProbe != null) return false;
                reason = "original_team_transition_pending";
                if (owner._pendingTeamTransition != null) return false;
                reason = "original_native_mutation_active";
                if (owner._nativeMutation != null) return false;
                reason = "original_map_generation_changed";
                if (observed.Generation != owner._sessionGeneration)
                    return false;
                reason = "original_map_hash_changed";
                if (!String.Equals(observed.Map, owner._mapFingerprint,
                                   StringComparison.Ordinal))
                    return false;
                reason = "original_roster_hash_changed";
                if (!String.Equals(observed.Roster, owner._rosterFingerprint,
                                   StringComparison.Ordinal))
                    return false;
                reason = "one_shot_SDK_native_invocation_already_consumed";
                if (owner._attemptGeneration == owner._sessionGeneration &&
                    owner._attemptEpoch == owner._mapEpoch)
                    return false;

                IDisposable nativeLease;
                if (!TryEnterNativeInventorySection(out nativeLease))
                {
                    reason = "original_engine_busy";
                    return false;
                }
                using (nativeLease)
                {
                    string solo;
                    reason = "original_solo_not_attested";
                    if (!NativeInventoryContract.IsStrictSoloHost(out solo) ||
                        GameNetworkAPI.IsLobbyEnvironment())
                        return false;
                    var players = GamePlayerManagerAPI.Instance;
                    var tribes = GameTribeManagerAPI.Instance;
                    var units = GameUnitManagerAPI.Instance;
                    var tiles = GameTileManagerAPI.Instance;
                    var data = GameData.Instance;
                    var state = data == null ? null : data.lastGameState;
                    if (players == null || tribes == null || units == null ||
                        tiles == null || state == null ||
                        players.GetLocalPlayerId() != observed.Local ||
                        !state.is_valid_player(observed.Local) ||
                        players.IsAIPlayer(observed.Local) ||
                        !state.is_skirmish_player(originalAlly) ||
                        !state.is_skirmish_player(originalEnemy) ||
                        !players.IsAIPlayer(originalAlly) ||
                        !players.IsAIPlayer(originalEnemy))
                        return false;

                    int ht = players.GetPlayerTeam(observed.Local);
                    int at = players.GetPlayerTeam(originalAlly);
                    int et = players.GetPlayerTeam(originalEnemy);
                    UnmanagedVector2<int> keep =
                        players.GetPlayerKeepDoorPosition(originalEnemy);
                    bool nativeTile = tiles.IsTileInsideMapBounds(
                        keep.X, keep.Y);
                    // native enemy staging read ONLY the original live native tile grid.
                    // A keep door is often itself a building/blocked tile;
                    // such a tile cannot be the target of a MoveHere order.
                    bool originalDoorWalkable = nativeTile &&
                        tiles.IsTileWalkableAndUnoccupied(
                            tiles.GetTileId(keep.X,keep.Y));
                    int nativeStagingTargetX=keep.X,nativeStagingTargetY=keep.Y;
                    bool nativeStagingTargetWalkable=originalDoorWalkable;
                    if (nativeTile && !nativeStagingTargetWalkable)
                    {
                        // Exact original SDK function, bounded radius.
                        // Validate its returned tile again. Never invent
                        // enemy coordinates or ask the game to enter a wall.
                        var nearest = tiles.GetNearestUnoccupiedTile(
                            keep.X,keep.Y,12);
                        int nx=nearest.X,ny=nearest.Y;
                        if (Math.Max(Math.Abs(nx-keep.X),
                                     Math.Abs(ny-keep.Y))<=12 &&
                            tiles.IsTileInsideMapBounds(nx,ny) &&
                            tiles.IsTileWalkableAndUnoccupied(
                                tiles.GetTileId(nx,ny)))
                        {
                            nativeStagingTargetX=nx;
                            nativeStagingTargetY=ny;
                            nativeStagingTargetWalkable=true;
                        }
                    }
                    OriginalAllyOrderSnapshot live;
                    string liveReason;
                    bool currentTuple =
                        OriginalHumanAllyOrderBridge.TryReadActiveUnderOriginalMonitor(
                            originalAlly, out live, out liveReason) &&
                        live.Raw.Available && live.Kind == 1 &&
                        live.Requester == observed.Local &&
                        live.Target == originalEnemy &&
                        live.Raw.FromPlayer == observed.Local &&
                        live.Raw.OrderPlayerField == originalEnemy;
                    reason = "original_enemy_target_not_walkable";
                    if (!nativeStagingTargetWalkable)
                        return false;
                    reason = "native_original_bound_order_not_current";
                    if (!currentTuple || !nativeTile)
                        return false;
                    int[] roster = players.GetAlivePlayerIds();
                    if (roster == null ||
                        !roster.Contains(observed.Local) ||
                        !roster.Contains(originalAlly) ||
                        !roster.Contains(originalEnemy) ||
                        !players.IsPlayerAlliedTo(observed.Local, originalAlly) ||
                        !players.IsPlayerAlliedTo(originalAlly, observed.Local) ||
                        players.IsPlayerAlliedTo(originalAlly, originalEnemy) ||
                        players.IsPlayerAlliedTo(originalEnemy, originalAlly))
                        return false;

                    var nativeTribes = new List<int>();
                    tribes.GetAllTribes(nativeTribes, null, null, null);
                    if (nativeTribes.Count < 4 ||
                        nativeTribes.Count > 10000 ||
                        nativeTribes.Distinct().Count() != nativeTribes.Count)
                        return false;

                    // Count all actual original combat units owned by the
                    // allied AI across native groups. The candidate must
                    // constitute <= 25% of its observed total military power.
                    var candidates = new List<Tuple<int,uint,int,int>>();
                    long allPower = 0;
                    var globalGroups = new HashSet<uint>();
                    var globalUnits = new HashSet<uint>();
                    foreach (int id in nativeTribes.OrderBy(x => x))
                    {
                        GameTribe* g;
                        if (id < 1 || !tribes.IsValid(id) ||
                            !tribes.TryGetTribeById(id, out g) ||
                            g == null || g->r_AliveState != AliveState.IsAlive)
                            continue;
                        if (g->r_PlayerIdOwner != originalAlly)
                            continue;
                        if (g->r_GlobalId == 0 ||
                            !globalGroups.Add(g->r_GlobalId))
                            return false;
                        var memberIds = new List<int>();
                        if (!tribes.GetUnits(id, memberIds) ||
                            memberIds.Count < 1 ||
                            memberIds.Count > 128 ||
                            memberIds.Distinct().Count() != memberIds.Count)
                            return false;
                        long groupPower = 0;
                        bool idle = g->r_OrderCooldownTicks == 0 &&
                            g->r_CurrentPatrolPoints == 0 &&
                            g->r_FollowTargetTribeId == 0 &&
                            g->r_AttackTargetUnitId == 0;
                        foreach (int uid in memberIds)
                        {
                            GameUnit* unit;
                            if (uid < 1 || !units.IsValid(uid) ||
                                !units.TryGetUnitById(uid, out unit) ||
                                unit == null ||
                                unit->r_AliveState != AliveState.IsAlive ||
                                unit->r_TribeId != id ||
                                unit->r_GlobalId == 0 ||
                                !globalUnits.Add(unit->r_GlobalId))
                                return false;
                            eChimps type = units.GetType(uid);
                            if (type == eChimps.CHIMP_TYPE_LORD ||
                                !IsCombatUnit(type))
                            {
                                idle = false;
                                continue;
                            }
                            long power = owner.DiplomacyMilitaryValue(
                                units, uid, type);
                            if (power <= 0 || allPower > Int32.MaxValue - power ||
                                groupPower > Int32.MaxValue - power)
                                return false;
                            allPower += power;
                            groupPower += power;
                            // ORIGINAL AI can already be marching when
                            // the HUMAN's real ATTACK is accepted. A free
                            // move-only group may be redirected ONCE to
                            // that same original enemy keep. A native unit
                            // in ATTACK, siege, construction, patrol or
                            // unknown AI orders is never interrupted.
                            // All original tribe/global owner identity,
                            // no-patrol/follow/attack, current monitor and
                            // 75% army reserve checks above remain binding.
                            ushort lastNative =
                                unit->r_AI_LastIssuedTribeCommand;
                            idle &= lastNative == (ushort)TribeAICommand.UnitStop ||
                                    lastNative == (ushort)TribeAICommand.MoveHerePosition;
                        }
                        if (idle && groupPower > 0)
                            candidates.Add(Tuple.Create(id,g->r_GlobalId,
                                                        (int)groupPower,
                                                        memberIds.Count));
                    }
                    // original eligibility guard: keep EXACT eligibility/quarter-army safety,
                    // but prefer a real multi-member native group; native gate predicates/200
                    // selected the first one-unit tribe 4335, and native
                    // returned 0 while natural original AI commands to
                    // multi-member tribes returned 1. No new role/power/
                    // command, only a different ordering of verified rows.
                    foreach (var group in candidates
                        .OrderByDescending(x => x.Item4)
                        .ThenBy(x => x.Item1))
                    {
                        bool eligible = BoundHumanAttackEligible(
                            observed.Local, originalAlly, originalEnemy,
                            ht, at, et, true, prior.Accepted == true,
                            currentTuple, true, true, true, nativeTile,
                            (int)allPower, group.Item3, group.Item4);
                        if (!eligible)
                            continue;
                        GameTribe* liveGroup;
                        if (!tribes.TryGetTribeById(group.Item1,
                                                   out liveGroup) ||
                            liveGroup == null ||
                            liveGroup->r_PlayerIdOwner != originalAlly ||
                            liveGroup->r_GlobalId != group.Item2 ||
                            (liveGroup->r_ObjectiveTargetTileX == nativeStagingTargetX &&
                             liveGroup->r_ObjectiveTargetTileY == nativeStagingTargetY))
                            continue;
                        // Commit one-shot attempt immediately BEFORE the
                        // SDK native call; a throw or unknown wrapper result
                        // can never be replayed in the same map generation.
                        owner._attemptGeneration=owner._sessionGeneration;
                        owner._attemptEpoch=owner._mapEpoch;
                        owner._attemptTribeId=group.Item1;
                        owner._attemptTribeGlobalId=group.Item2;
                        owner._targetX=nativeStagingTargetX;
                        owner._targetY=nativeStagingTargetY;
                        owner._attemptTick=owner.ReadSimulationTick();
                        bool wrapper = false;
                        try
                        {
                            // original ai native move original AI's ACTUAL native move_here
                            // callback samples used isNewOrder=0,MoveType=0.
                            // This is a one-shot ABI compatibility trial,
                            // not consent, ACK or claimed troop arrival.
                            wrapper=tribes.IssueMoveHereCommand(
                                group.Item1,nativeStagingTargetX,nativeStagingTargetY,false,0,
                                TribeMoveType.NoChange);
                        }
                        catch (Exception error)
                        {
                            reason="original_native_order_outcome_unknown:"+
                                error.GetType().Name;
                            return false;
                        }
                        reason="mod_native_wrapper_returned_unconfirmed"+
                            (wrapper ? "_true" : "_false")+
                            "|selected_native_tribe="+group.Item1+
                            "|original_group_members="+group.Item4+
                            "|original_group_power="+group.Item3+
                            "|total_allied_combat_power="+allPower+
                            "|original_safe_candidates="+candidates.Count+
                            "|original_enemy_door="+keep.X+","+keep.Y+
                            "|original_door_walkable="+originalDoorWalkable+
                            "|original_walkable_staging_goal="+
                                nativeStagingTargetX+","+nativeStagingTargetY+
                            "|within_12_original_keep_tiles="+
                                (Math.Max(Math.Abs(nativeStagingTargetX-keep.X),
                                          Math.Abs(nativeStagingTargetY-keep.Y))<=12);
                        return wrapper;
                    }
                    reason="no_verified_idle_ally_native_tribe";
                    return false;
                }
            }
            catch (Exception error)
            {
                reason="native_gate_or_original_unit_fault:"+
                    error.GetType().Name;
                return false;
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }

        internal static unsafe bool ObserveActualNativeAttackGoal(
            out string note)
        {
            note="native_no_attempt";
            var o=QaOwner();
            if (o==null || !ExplicitQaScope ||
                o._attemptGeneration != o._sessionGeneration ||
                o._attemptEpoch != o._mapEpoch ||
                o._attemptTribeGlobalId==0 ||
                !Monitor.TryEnter(o._gate))
                return false;
            try
            {
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return false;
                using (lease)
                {
                    var tribes=GameTribeManagerAPI.Instance;
                    GameTribe* group;
                    if (tribes==null ||
                        !tribes.TryGetTribeById(o._attemptTribeId,out group) ||
                        group==null ||
                        group->r_AliveState != AliveState.IsAlive ||
                        group->r_GlobalId != o._attemptTribeGlobalId)
                    {
                        note="original_tribe_identity_changed";
                        return false;
                    }
                    bool goal=group->r_ObjectiveTargetTileX==o._targetX &&
                              group->r_ObjectiveTargetTileY==o._targetY &&
                              o.ReadSimulationTick()>o._attemptTick;
                    note="original_native_goal_"+(goal ? "confirmed" : "not_yet_confirmed");
                    return goal;
                }
            }
            finally { Monitor.Exit(o._gate); }
        }


        private ObservedNativeAiAidReceipt _observedNativeAid;
        private long _aidWriteGeneration = -1;
        private readonly Dictionary<ulong, int> _aidWriteAttempts = new Dictionary<ulong, int>();
        private static bool ExplicitQaScope
        {
            get
            {
                string why;
                return Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") == "1" && Environment.GetEnvironmentVariable("DIPLOMACY_AI_MILITARY_TEST") == "1" && DisposableGameScope.IsEnabled(out why);
            }
        }

        // Measurement at the existing exact native write entry. This function never
        // changes a resource, gift model, callback argument or authorization result.
        private static void ObserveActualAidWrite(int player, int good, int signedAmount)
        {
            try
            {
                if (!ExplicitQaScope)
                    return;
                var o = Volatile.Read(ref _activeOwner);
                if (o == null)
                    return;
                var p = o._activeAidPromise;
                if (p == null || o._nativeMutation == null || p.Good != good || Math.Abs((long)signedAmount) != p.Quantity || (player != p.Donor && player != p.Recipient) || !IsNativeDiplomacySectionHeld)
                    return;
                if (o._aidWriteGeneration != o._sessionGeneration)
                {
                    o._aidWriteAttempts.Clear();
                    o._aidWriteGeneration = o._sessionGeneration;
                }

                int n;
                o._aidWriteAttempts.TryGetValue(p.Id, out n);
                o._aidWriteAttempts[p.Id] = checked(n + 1);
            }
            catch
            {
            } // Missing measurement can fail QA, never the gameplay transaction.
        }

        // Called only AFTER verified native settlement and committed bookkeeping.
        // This observer is read-only and cannot make a transfer/credit successful.
        private void ObserveCommittedNativeAid(NativeTransferProbe probe)
        {
            if (!ExplicitQaScope || probe == null || (_observedNativeAid != null && _observedNativeAid.Generation == _sessionGeneration && _observedNativeAid.Map == _mapFingerprint && _observedNativeAid.Roster == _rosterFingerprint))
                return;
            try
            {
                RequireNativeDiplomacySection();
                if (probe.Purpose != DiplomacyTransferPurpose.Gift || ProbeSender(probe) == _localPlayer || probe.RollbackPending || _aidMemory == null || probe.Progress == null || !probe.Progress.SenderObserved || !probe.Progress.RecipientObserved)
                    return;
                int donor = ProbeSender(probe), recipient = probe.TargetPlayer;
                var matches = _aidMemory.Promises.Where(p => p.Status == AidPledgeStatus.Delivered && p.Donor == donor && p.Recipient == recipient && p.Good == probe.GoodId && p.Quantity == probe.Amount && p.IssuedDay < probe.GameDay && p.DueDay >= probe.GameDay).ToArray();
                if (matches.Length != 1)
                    return;
                int da = GoodAmount(donor, probe.GoodId), ra = GoodAmount(recipient, probe.GoodId);
                if (da != probe.SenderBefore - probe.Amount || ra != probe.RecipientBefore + probe.Amount)
                    return;
                _observedNativeAid = new ObservedNativeAiAidReceipt
                {
                    SourcePid = Process.GetCurrentProcess().Id,
                    Generation = _sessionGeneration,
                    Tick = ReadSimulationTick(),
                    Map = _mapFingerprint,
                    Roster = _rosterFingerprint,
                    Id = matches[0].Id,
                    Day = probe.GameDay,
                    Donor = donor,
                    Recipient = recipient,
                    Good = probe.GoodId,
                    Quantity = probe.Amount,
                    DonorBefore = probe.SenderBefore,
                    RecipientBefore = probe.RecipientBefore,
                    DonorAfter = da,
                    RecipientAfter = ra,
                    NativeWriteCalls = Interlocked.Read(ref _nativeWriteCalls)
                };
                HeadlessQaDriver.Note("status=actual_native_ai_aid_observed|id=" + matches[0].Id + "|donor=" + donor + "|recipient=" + recipient + "|good=" + probe.GoodId + "|amount=" + probe.Amount + "|before=" + probe.SenderBefore + "," + probe.RecipientBefore + "|after=" + da + "," + ra + "|native_write_calls=" + _observedNativeAid.NativeWriteCalls);
            }
            catch (Exception e)
            {
                HeadlessQaDriver.Note("status=aid_observation_unavailable|reason=" + e.GetType().Name);
            }
        }

        // original lobby: sampled strictly for the already-started private restart QA.
        // This is not an authorization pathway. Never turn an actual lobby
        // into a solo session or change the map, network, archive or roles.
        internal static bool TryReadQaRestartAuthority(
            out long generation, out bool disabledNewMap,
            out bool actualLobbyActive)
        {
            generation = -1;
            disabledNewMap = false;
            actualLobbyActive = false;
            if (!ExplicitQaScope)
                return false;
            var owner = QaOwner();
            if (owner == null || !Monitor.TryEnter(owner._gate))
                return false;
            try
            {
                generation = Volatile.Read(ref owner._sessionGeneration);
                disabledNewMap = Volatile.Read(ref owner._mapDisabled);
                actualLobbyActive = GameNetworkAPI.IsLobbyEnvironment();
                return true;
            }
            catch (Exception)
            {
                return false; // Uncertain original state is NEVER a grant.
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }


        // Invoked only from the EXISTING private QA snapshot with both the
        // owner gate and original EngineInterface.threadLock held. No new
        // native call, input dispatch, team or resource mutation.
        private static unsafe Dictionary<uint,NativeAiUnitPosition>
            ObserveNativeAiCombatPositions(
                EngineInterface.PlayState live,
                GamePlayerManagerAPI api)
        {
            if (!IsNativeDiplomacySectionHeld || live == null || api == null)
                return null;
            var units=GameUnitManagerAPI.Instance;
            var tribes=GameTribeManagerAPI.Instance;
            if (units == null || tribes == null)
                return null;
            int[] ids=units.GetAllAliveUnits();
            if (ids == null || ids.Length > 50000)
                return null;
            var map=new Dictionary<uint,NativeAiUnitPosition>();
            foreach(int id in ids)
            {
                if (id < 1 || !units.IsValid(id))
                    continue;
                GameUnit* unit;
                if (!units.TryGetUnitById(id,out unit) ||
                    unit == null ||
                    unit->r_AliveState != AliveState.IsAlive ||
                    unit->r_GlobalId == 0 || unit->r_TribeId < 1 ||
                    !IsCombatUnit(units.GetType(id)))
                    continue;
                GameTribe* tribe;
                if (!tribes.TryGetTribeById(unit->r_TribeId,out tribe) ||
                    tribe == null || tribe->r_AliveState != AliveState.IsAlive ||
                    tribe->r_GlobalId == 0)
                    continue;
                int owner=tribe->r_PlayerIdOwner;
                if (owner < 1 || owner > 8 ||
                    !live.is_skirmish_player(owner) || !api.IsAIPlayer(owner))
                    continue;
                var tile=units.GetCurrentWorldTilePosition(id);
                map[unit->r_GlobalId]=new NativeAiUnitPosition
                {
                    GlobalId=unit->r_GlobalId,
                    Owner=owner,TribeId=unit->r_TribeId,
                    WorldX=tile.X,WorldY=tile.Y
                };
                if(map.Count>4096)
                    return null;
            }
            return map;
        }


        private static unsafe Dictionary<uint,OriginalAiNativeTribeTarget>
            ReadOriginalAiTribeTarget(EngineInterface.PlayState live,
                GamePlayerManagerAPI players)
        {
            if (!IsNativeDiplomacySectionHeld || live==null || players==null)
                return null;
            var tribes=GameTribeManagerAPI.Instance;
            if(tribes==null)return null;
            var ids=new List<int>();
            tribes.GetAllTribes(ids,AliveState.IsAlive);
            if(ids.Count>4096 || ids.Count<1)return null;
            var result=new Dictionary<uint,OriginalAiNativeTribeTarget>();
            foreach(int id in ids)
            {
                GameTribe* t;
                if(id<1 || !tribes.IsValid(id) ||
                   !tribes.TryGetTribeById(id,out t) || t==null ||
                   t->r_AliveState!=AliveState.IsAlive || t->r_GlobalId==0)
                    continue;
                int owner=t->r_PlayerIdOwner;
                if(owner<1 || owner>8 || !live.is_skirmish_player(owner)
                   || !players.IsAIPlayer(owner))
                    continue;
                if(result.ContainsKey(t->r_GlobalId))return null;
                result.Add(t->r_GlobalId,new OriginalAiNativeTribeTarget {
                    GlobalId=t->r_GlobalId,Owner=owner,
                    AttackTargetOwner=t->r_AttackTargetOwnerPlayerId>8U?-1:
                        (int)t->r_AttackTargetOwnerPlayerId,
                    ObjectiveX=t->r_ObjectiveTargetTileX,
                    ObjectiveY=t->r_ObjectiveTargetTileY
                });
            }
            return result;
        }

        internal static bool TryReadAiMilitaryQa(out MilitaryQaWorldSnapshot result, out string reason)
        {
            result = null;
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                if (!o._mapActive || o._mapDisabled || o._mapBootstrapPending || o._inventoryRestorePending)
                {
                    reason = "map_not_ready";
                    return false;
                }

                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "engine_busy";
                    return false;
                }

                using (lease)
                {
                    if (!NativeInventoryContract.IsStrictSoloHost(out reason))
                        return false;
                    var live = GameData.Instance.lastGameState;
                    var api = GamePlayerManagerAPI.Instance;
                    var players = o._roster.Where(IsAlivePlayer).Distinct().OrderBy(x => x).ToArray();
                    var aid = o._observedNativeAid;
                    if (aid != null && (aid.Generation != o._sessionGeneration || aid.Map != o._mapFingerprint || aid.Roster != o._rosterFingerprint))
                        aid = null;
                    var s = new MilitaryQaWorldSnapshot
                    {
                        Generation = o._sessionGeneration,
                        Tick = o.ReadSimulationTick(),
                        Day = Math.Max(1, o._lastProcessedDay),
                        Map = o._mapFingerprint,
                        Roster = o._rosterFingerprint,
                        Local = o._localPlayer,
                        Ai = players.Where(p => live.is_skirmish_player(p) && api.IsAIPlayer(p)).ToArray(),
                        Humans = players.Where(p => live.is_valid_player(p) && !api.IsAIPlayer(p)).ToArray(),
                        Alliances = new bool[9, 9],
                        OriginalNativeTeams = new int[9],
                        OriginalKeepDoorX = new int[9],
                        OriginalKeepDoorY = new int[9],
                        OriginalKeepDoorKnown = new bool[9],
                        NativeAiUnitPositions=ObserveNativeAiCombatPositions(live,api),
                        OriginalAiTribeTargets=ReadOriginalAiTribeTarget(live,api),
                        Goods = new int[9, 25],
                        NativeWriteCalls = Interlocked.Read(ref _nativeWriteCalls),
                        Aid = aid,
                        Military = new MilitaryQaOrderObservation[9],
                        AidWriteAttempts = o._aidWriteGeneration == o._sessionGeneration ? new Dictionary<ulong, int>(o._aidWriteAttempts) : new Dictionary<ulong, int>(),
                        AidLedger = o._aidMemory == null ? null : AidPromiseLedgerCodec.Encode(o._aidMemory)
                    };
                    foreach (int p in players)
                    {
                        s.OriginalNativeTeams[p] = api.GetPlayerTeam(p);
                        // Same original engine monitor and authorized
                        // snapshot lease as every current copied PlayState
                        // read. No alternate map position or fake keep.
                        try
                        {
                            var originalDoor =
                                api.GetPlayerKeepDoorPosition(p);
                            var originalTiles=GameTileManagerAPI.Instance;
                            if(originalTiles != null &&
                               originalTiles.IsTileInsideMapBounds(
                                  originalDoor.X,originalDoor.Y))
                            {
                                s.OriginalKeepDoorX[p]=originalDoor.X;
                                s.OriginalKeepDoorY[p]=originalDoor.Y;
                                s.OriginalKeepDoorKnown[p]=true;
                            }
                        }
                        catch (Exception)
                        {
                            // Unknown is NOT silently treated as a castle
                            // location or a true native target.
                            s.OriginalKeepDoorKnown[p]=false;
                        }
                        foreach (int q in players)
                            s.Alliances[p, q] = api.IsPlayerAlliedTo(p, q);
                        foreach (var good in GoodCatalog.All)
                            s.Goods[p, good.Id] = o.GoodAmount(p, good.Id);
                        OriginalAllyOrderSnapshot active;
                        string readReason;
                        bool readable = OriginalHumanAllyOrderBridge.TryReadActiveUnderOriginalMonitor(p, out active, out readReason);
                        int kind = readable ? active.Kind : 0, target = readable ? active.Target : 0;
                        // Raw fields are available only with a validated read
                        // of the SAME copied original PlayState under its monitor.
                        // A failed read never exposes fabricated zero-valued raw
                        // data as if it were an observed native tuple.
                        OriginalAllyRawOrderTuple raw = readable ? active.Raw : default(OriginalAllyRawOrderTuple);
                        var order = o._allyOrderStatus[p];
                        if (!o.IsLatestHumanOrder(order))
                            order = null;
                        s.Military[p] = new MilitaryQaOrderObservation
                        {
                            Consulted = p,
                            NativeKind = kind,
                            NativeTarget = target,
                            NativeReadable = readable,
                            NativeRequester = readable ? active.Requester : 0,
                            RawNativeAvailable = readable && raw.Available,
                            RawNativeSource = raw.Source,
                            RawNativeKind = raw.Available ? raw.Kind : 0,
                            RawNativeOrderPlayerField = raw.Available ? raw.OrderPlayerField : 0,
                            RawNativeFromPlayerField = raw.Available ? raw.FromPlayer : 0,
                            NativeTargetProjection = readable ? active.TargetProjection : OriginalAllyTargetProjection.Unavailable,
                            NativeReadReason = readReason,
                            Opcode = order == null ? 0 : order.Opcode,
                            Target = order == null ? 0 : order.Target,
                            Requester = order == null ? 0 : order.Requester,
                            Accepted = order == null ? null : order.Accepted,
                            NativeDecision = order != null && order.NativeDecision,
                            PolicyDecision = order != null && order.PolicyDecision,
                            DecisionUnbound = order != null && order.DecisionUnbound,
                            Activity = order == null ? AllyOrderActivityRelation.Unavailable : order.NativeActivity,
                            ObservedActive = order != null && order.ObservedActive,
                            EndedDay = order == null ? -1 : order.EndedDay,
                            ReplySequence = order == null ? 0 : order.ReplySequence,
                            Reapplications = o._orderReapplications[p]
                        };
                        s.Military[p].Queued = Volatile.Read(ref o._allyOrders[p]) != null;
                        s.Military[p].InFlight = Volatile.Read(ref o._allyInFlight[p]) != null;
                        s.Military[p].OriginalHumanRetained = Volatile.Read(ref o._originalHumanOrders[p]) != null;
                    }

                    result = s;
                    reason = "observed_under_original_monitor";
                    return true;
                }
            }
            catch (Exception e)
            {
                reason = "observation_" + e.GetType().Name;
                return false;
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }

        // Code submits a real LOCAL HUMAN command at the original HUD GameAction
        // entry. Return proves retained production input, never native acceptance.
        internal static bool SubmitExplicitHumanAllyOrder(int opcode, int consulted, int target, out string reason)
        {
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                if (!o._mapActive || o._mapDisabled || o._mapBootstrapPending || o._pendingTeamTransition != null || o._inventoryRestorePending)
                {
                    reason = "map_not_ready";
                    return false;
                }

                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "engine_busy";
                    return false;
                }

                using (lease)
                {
                    if (!NativeInventoryContract.IsStrictSoloHost(out reason))
                        return false;
                    var api = GamePlayerManagerAPI.Instance;
                    var live = GameData.Instance.lastGameState;
                    if (consulted < 1 || consulted > 8 || consulted == o._localPlayer || !IsAlivePlayer(consulted) || !live.is_valid_player(o._localPlayer) || live.is_skirmish_player(o._localPlayer) || live.is_valid_player(consulted) || !live.is_skirmish_player(consulted) || !api.IsAIPlayer(consulted) || !api.IsPlayerAlliedTo(o._localPlayer, consulted) || !api.IsPlayerAlliedTo(consulted, o._localPlayer))
                    {
                        reason = "actual_human_and_mutually_allied_ai_required";
                        return false;
                    }

                    if (opcode == 11)
                    {
                        if (target != 0)
                        {
                            reason = "defend_requires_original_hud_zero_target";
                            return false;
                        }
                    }
                    else if (opcode >= 1 && opcode <= 6)
                    {
                        if (target < 1 || target > 8 || !IsAlivePlayer(target) || target == o._localPlayer || target == consulted || api.IsPlayerAlliedTo(consulted, target) || api.IsPlayerAlliedTo(target, consulted) || o.TreatyProtectsPlayers(consulted, target) || o.TreatyProtectsPlayers(o._localPlayer, target))
                        {
                            reason = "actual_unprotected_enemy_required";
                            return false;
                        }
                    }
                    else if (opcode != 10 || target != 0)
                    {
                        reason = "unsupported_original_human_command";
                        return false;
                    }

                    // A registered shared event is now the actual managed
                    // GameAction owner. An old direct Harmony prefix MUST be
                    // absent; it would otherwise double-capture the click.
                    var patches = o._allyGameActionTarget == null ? null : Harmony.GetPatchInfo(o._allyGameActionTarget);
                    var prefix = typeof(DiplomacyFeature).GetMethod("AllyGameActionPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                    bool directLegacy = patches != null &&
                        patches.Prefixes.Any(p => p.owner == o._harmony.Id &&
                            p.PatchMethod == prefix);
                    if (!Volatile.Read(ref o._sharedGameActionActive) ||
                        !Volatile.Read(ref _sharedGameActionRegistered) ||
                        o._allyGameActionTarget == null || directLegacy)
                    {
                        reason = "actual_shared_human_capture_owner_absent_or_legacy_double_hook";
                        return false;
                    }

                    EngineInterface.GameAction(Enums.GameActionCommand.Ally_Orders, opcode, consulted, target);
                    var retained = Volatile.Read(ref o._allyOrders[consulted]);
                    bool exact = retained != null && retained.Opcode == opcode && retained.Consulted == consulted && retained.Target == target && retained.Requester == o._localPlayer && retained.Generation == o._sessionGeneration && retained.Epoch == o._mapEpoch;
                    reason = exact ? "actual_local_human_production_input_retained_not_acceptance" : "production_input_not_retained";
                    return exact;
                }
            }
            catch (Exception e)
            {
                reason = "submit_" + e.GetType().Name;
                return false;
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }

        internal static bool ReadActualNativeEligibility(out string reason)
        {
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                return o.TryAuthorizeNativeInventory(out reason);
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }
    }
}
#endif
