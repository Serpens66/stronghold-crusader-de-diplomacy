#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
// Mod-owned solo-host scheduling, never original AI request.
// Pure gate: no SDK, native, Unity, Harmony or game imports.
using System;
using System.Collections.Generic;

namespace StrongholdEuropeDE.Runtime
{
    internal enum HostDirectedAiTribePhase
    {
        Refused, Deferred, EligibleForPrivateCanary,
        WrapperReturnedUnconfirmed, LaterOriginalObservationUnattributed,
        OriginalOrderUnchanged, ForeignOrderOrIdentityReleased
    }

    internal sealed class HostDirectedAiTribeAuthorizationFacts
    {
        internal long CandidateGeneration, CurrentGeneration, CandidateEpoch, CurrentEpoch;
        internal long CandidateTick, CurrentTick, ExpiryTick;
        internal int OriginalLocalHuman, DonorAiSlot, RecipientAiSlot;
        internal int DonorMilitaryStrength, CastleDefenceReserve, ThreatStrength, TribePower;
        internal bool? SoloRosterVerified, SoloHostVerified, EngineMonitorHeld;
        internal bool? CpuDonorVerified, CpuRecipientVerified, OriginalTribeOwnerVerified;
        internal bool? MemberGlobalIdentitiesVerified, ReciprocalNativeAllianceVerified;
        internal bool? NativeTargetVerified, OriginalSaveFenceClear, OriginalOrderSlotUncontested;
        internal bool HistoricalReplayFromSave, Cancelled = false, MapActive, MapDisabled, BootstrapPending;
        internal bool? OriginalRecipientLastStandVerified, ObservedRecipientReliefThreatVerified;
        internal bool HasCompletedOriginalIdentity, RepeatOrReplay;
        internal int TileX, TileY, MapWidthTiles, MapHeightTiles;
        internal bool IsBuildingTarget;
        // The move-only producer has no building evidence. Preserve unknown defaults.
        internal int BuildingId = 0, BuildingGlobalId = 0, EnemySlot = 0, NativeBuildingOwner = 0;
        internal bool? TargetEnemyHostileVerified = null;
    }

    // The only transition that spends a command is beginning the SDK call.
    // Lease/getter/preflight exceptions leave Attempted=false and retryable.
    internal sealed class HostDirectedAiTribeAttemptGate
    {
        internal bool Attempted { get; private set; }
        internal void BeginSdkInvocation()
        {
            if (Attempted) throw new InvalidOperationException("duplicate_sdk_attempt");
            Attempted = true;
        }
    }

    internal static class HostDirectedAiTribePolicy
    {
        internal const bool OriginalAiRequesterProven = false;
        internal const bool NativeAck = false;
        internal const bool NormalNativeRelease = false;
        internal const bool MpNativeRelease = false;

        // A mod-authored emergency edge is never durable or a save token.
        // The limited age is measured in the original simulation tick.
        internal const long MaxFreshReliefEdgeAgeTicks = 2L;
        internal static bool FreshReliefEdgeWithinTicks(long now, long edgeTick)
        {
            return edgeTick >= 0 && now >= edgeTick &&
                now - edgeTick <= MaxFreshReliefEdgeAgeTicks;
        }

        // A post-restore OLD emergency is not a new order. Wait for the
        // original emergency to clear and a NEW observed transition.
        // This is retryable and never permanently disables a loaded map.
        internal static bool FreshReliefPermitted(
            bool requireNewEdge, bool stillTakingColdBaseline,
            bool previouslyObserved, bool previouslyEmergency,
            bool freshEdgeLatched, bool emergencyNow)
        {
            if (!emergencyNow) return false;
            if (!requireNewEdge) return true;
            return !stillTakingColdBaseline && previouslyObserved &&
                (freshEdgeLatched || !previouslyEmergency);
        }

        private static string Need(bool? b, string label)
        {
            if (!b.HasValue) return "defer_" + label + "_unobserved";
            return b.Value ? null : "refuse_" + label;
        }
        internal static string Evaluate(HostDirectedAiTribeAuthorizationFacts f,
            bool originalEvaluatorRequest = false)
        {
            if (f == null) return "defer_facts_unavailable";
            if (f.Cancelled) return "refuse_cancelled";
            if (f.HistoricalReplayFromSave) return "refuse_historical_intent_replay";
            if (f.RepeatOrReplay) return "refuse_duplicate_or_foreign_order";
            if (f.CandidateGeneration < 1 || f.CandidateEpoch < 1 ||
                f.CandidateTick < 0 || f.CurrentTick < 0 || f.ExpiryTick < f.CandidateTick)
                return "refuse_invalid_observation_identity";
            if (f.CurrentGeneration != f.CandidateGeneration ||
                f.CurrentEpoch != f.CandidateEpoch)
                return "refuse_map_epoch_or_generation_changed";
            if (f.CurrentTick != f.CandidateTick || f.CurrentTick > f.ExpiryTick)
                return "defer_fresh_original_tick_required";
            if (!f.MapActive || f.MapDisabled || f.BootstrapPending ||
                !f.HasCompletedOriginalIdentity)
                return "defer_original_map_not_ready";
            string reason;
            if ((reason = Need(f.EngineMonitorHeld, "original_monitor")) != null) return reason;
            if ((reason = Need(f.OriginalSaveFenceClear, "original_save_fence")) != null) return reason;
            if ((reason = Need(f.SoloRosterVerified, "sole_human_roster")) != null) return reason;
            if ((reason = Need(f.SoloHostVerified, "solo_local_host")) != null) return reason;
            if (f.OriginalLocalHuman < 1 || f.OriginalLocalHuman > 8 ||
                f.DonorAiSlot < 1 || f.DonorAiSlot > 8 ||
                f.RecipientAiSlot < 1 || f.RecipientAiSlot > 8 ||
                f.DonorAiSlot == f.RecipientAiSlot ||
                f.DonorAiSlot == f.OriginalLocalHuman ||
                f.RecipientAiSlot == f.OriginalLocalHuman)
                return "refuse_distinct_original_ai_roles";
            if ((reason = Need(f.CpuDonorVerified, "original_donor_ai")) != null) return reason;
            if ((reason = Need(f.CpuRecipientVerified, "original_recipient_ai")) != null) return reason;
            if ((reason = Need(f.OriginalRecipientLastStandVerified, "original_last_stand")) != null) return reason;
            if ((reason = Need(f.ObservedRecipientReliefThreatVerified, "current_relief_threat")) != null) return reason;
            if ((reason = Need(f.OriginalTribeOwnerVerified, "original_tribe_owner")) != null) return reason;
            if ((reason = Need(f.MemberGlobalIdentitiesVerified, "original_complete_members")) != null) return reason;
            if ((reason = Need(f.ReciprocalNativeAllianceVerified, "original_reciprocal_alliance")) != null) return reason;
            if (f.DonorMilitaryStrength < 0 || f.CastleDefenceReserve < 0 ||
                f.ThreatStrength < 0 || f.TribePower <= 0 ||
                f.TribePower > f.DonorMilitaryStrength)
                return "refuse_strength_evidence";
            if ((long)f.DonorMilitaryStrength - f.TribePower <
                MilitaryReservePolicy.RequiredAtHome(f.CastleDefenceReserve, f.ThreatStrength))
                return "refuse_defensive_home_reserve";
            if (!originalEvaluatorRequest &&
                (reason = Need(f.OriginalOrderSlotUncontested, "original_order_slot")) != null)
                return reason;
            if ((reason = Need(f.NativeTargetVerified, "original_target")) != null) return reason;
            if (f.IsBuildingTarget)
            {
                if (f.BuildingId < 1 || f.BuildingGlobalId < 1 ||
                    f.EnemySlot < 1 || f.EnemySlot > 8 ||
                    f.EnemySlot == f.DonorAiSlot || f.EnemySlot == f.RecipientAiSlot ||
                    f.NativeBuildingOwner != f.EnemySlot)
                    return "refuse_attack_building_identity";
                if ((reason = Need(f.TargetEnemyHostileVerified, "original_enemy_hostility")) != null) return reason;
            }
            else if (f.TileX < 0 || f.TileY < 0 ||
                     f.TileX >= f.MapWidthTiles || f.TileY >= f.MapHeightTiles ||
                     f.MapWidthTiles <= 0 || f.MapHeightTiles <= 0)
                return "refuse_original_tile_bounds";
            return originalEvaluatorRequest
                ? "mod_authored_original_evaluator_candidate"
                : "mod_owned_solo_host_candidate_only";
        }
        // The wrapper boolean is never an ACK. Original order changes can be
        // observed, but attribution, arrival, combat and success require more.
        internal static HostDirectedAiTribePhase LaterObservation(
            long issuedTick, long nowTick, bool sameEpoch, bool sameTribeAndMembers,
            bool originalOrderStillOursOrUnchanged, bool anyOrderChangeObserved)
        {
            if (!sameEpoch || !sameTribeAndMembers || !originalOrderStillOursOrUnchanged)
                return HostDirectedAiTribePhase.ForeignOrderOrIdentityReleased;
            if (nowTick <= issuedTick)
                return HostDirectedAiTribePhase.Deferred;
            return anyOrderChangeObserved
                ? HostDirectedAiTribePhase.LaterOriginalObservationUnattributed
                : HostDirectedAiTribePhase.OriginalOrderUnchanged;
        }
    }

    // Sender-only projection from original GameTribe.r_PlayerIdOwner. A unit's
    // controllable-for-player flag is deliberately not an actor/owner source.
    internal sealed class HostDirectedOriginalTribeFact
    {
        internal readonly int TribeId, OriginalOwner;
        internal readonly uint TribeGlobalId;
        internal HostDirectedOriginalTribeFact(int id, int owner, uint globalId)
        {
            TribeId = id; OriginalOwner = owner; TribeGlobalId = globalId;
        }
    }

    internal sealed class HostDirectedOriginalUnitFact
    {
        internal readonly int UnitId, TribeId, WorldX, WorldY;
        internal readonly uint UnitGlobalId;
        internal readonly bool Combat;
        internal readonly long Strength;
        internal HostDirectedOriginalUnitFact(int id, uint globalId, int tribe,
            bool combat, long strength, int worldX, int worldY)
        {
            UnitId = id; UnitGlobalId = globalId; TribeId = tribe;
            Combat = combat; Strength = strength; WorldX = worldX; WorldY = worldY;
        }
    }

    internal sealed class HostDirectedOriginalOwnerLedger
    {
        private sealed class OwnedWorldUnit
        {
            internal HostDirectedOriginalUnitFact Unit;
            internal int Owner; // zero means ORIGINAL ownership not proved
        }
        private readonly List<OwnedWorldUnit> _live;
        private readonly Dictionary<int, long> _provenStrength;
        private readonly Dictionary<int, HostDirectedOriginalUnitFact> _byUnitId;
        private HostDirectedOriginalOwnerLedger(
            List<OwnedWorldUnit> live, Dictionary<int, long> strength,
            Dictionary<int, HostDirectedOriginalUnitFact> unitIds)
        {
            _live = live; _provenStrength = strength; _byUnitId = unitIds;
        }

        // Inputs are copied from original ALIVE native tribe and unit records
        // under the original monitor, and NEVER manufactured from GetOwner().
        // Global IDs are checked across the ENTIRE alive-unit enumeration,
        // including different tribes and noncombat units.
        internal static bool TryBuild(
            IList<HostDirectedOriginalTribeFact> originalTribes,
            IList<HostDirectedOriginalUnitFact> allAliveUnits,
            out HostDirectedOriginalOwnerLedger ledger, out string reason)
        {
            ledger = null; reason = "original_world_unavailable";
            if (originalTribes == null || allAliveUnits == null ||
                originalTribes.Count > 10000 || allAliveUnits.Count > 50000 ||
                allAliveUnits.Count == 0)
                return false;
            var owners = new Dictionary<int, int>();
            var globalTribes = new HashSet<uint>();
            foreach (HostDirectedOriginalTribeFact tribe in originalTribes)
            {
                if (tribe == null || tribe.TribeId < 1 ||
                    tribe.OriginalOwner < 1 || tribe.OriginalOwner > 8 ||
                    tribe.TribeGlobalId == 0U || owners.ContainsKey(tribe.TribeId) ||
                    !globalTribes.Add(tribe.TribeGlobalId))
                {
                    reason = "ambiguous_original_tribe_owner_or_global_identity";
                    return false;
                }
                owners.Add(tribe.TribeId, tribe.OriginalOwner);
            }
            var globalUnits = new HashSet<uint>();
            var byId = new Dictionary<int, HostDirectedOriginalUnitFact>();
            var live = new List<OwnedWorldUnit>(allAliveUnits.Count);
            var strength = new Dictionary<int, long>();
            foreach (HostDirectedOriginalUnitFact u in allAliveUnits)
            {
                if (u == null || u.UnitId < 1 || u.TribeId < 0 ||
                    u.Strength < 0 || byId.ContainsKey(u.UnitId) ||
                    u.Combat && u.UnitGlobalId == 0U ||
                    u.UnitGlobalId != 0U && !globalUnits.Add(u.UnitGlobalId))
                {
                    reason = "duplicate_or_invalid_live_unit_global_identity";
                    return false;
                }
                byId.Add(u.UnitId, u);
                if (!u.Combat) continue;
                int nativeOwner;
                if (u.TribeId < 1 || !owners.TryGetValue(u.TribeId, out nativeOwner))
                    nativeOwner = 0; // unknown, not player zero authorization
                live.Add(new OwnedWorldUnit { Unit = u, Owner = nativeOwner });
                if (nativeOwner == 0 || u.Strength == 0) continue;
                long prior;
                strength.TryGetValue(nativeOwner, out prior);
                if (prior > Int32.MaxValue - u.Strength)
                {
                    reason = "original_grouped_army_power_overflow";
                    return false;
                }
                strength[nativeOwner] = prior + u.Strength;
            }
            ledger = new HostDirectedOriginalOwnerLedger(live, strength, byId);
            reason = "original_tribe_owner_world_projection";
            return true;
        }

        internal long KnownGroupedStrength(int nativeOwner)
        {
            long value;
            return nativeOwner >= 1 && nativeOwner <= 8 &&
                _provenStrength.TryGetValue(nativeOwner, out value) ? value : 0L;
        }

        // All native-alive units claiming this group must equal the group's
        // current returned member IDs AND their original global IDs.
        internal bool ExactMemberList(int tribeId, int[] ids, uint[] globalIds)
        {
            if (tribeId < 1 || ids == null || globalIds == null ||
                ids.Length == 0 || ids.Length != globalIds.Length)
                return false;
            int liveCount = 0;
            foreach (HostDirectedOriginalUnitFact member in _byUnitId.Values)
                if (member.TribeId == tribeId) liveCount++;
            if (liveCount != ids.Length) return false;
            var used = new HashSet<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                HostDirectedOriginalUnitFact member;
                if (!used.Add(ids[i]) || !_byUnitId.TryGetValue(ids[i], out member) ||
                    member.TribeId != tribeId || globalIds[i] == 0 ||
                    member.UnitGlobalId != globalIds[i])
                    return false;
            }
            return true;
        }
        private static bool NearKeep(HostDirectedOriginalUnitFact u, int x, int y)
        {
            return Math.Abs((long)u.WorldX - x) <= 192L &&
                Math.Abs((long)u.WorldY - y) <= 192L;
        }

        // A nearby combat unit without an actual ORIGINAL GameTribe owner
        // could be a hostile troop. It cannot be ignored as "no threat".
        internal bool TryThreatAroundKeep(int defender, int centerX, int centerY,
            Func<int, int, bool> originalAlliedTo,
            out long threat, out string reason)
        {
            threat = 0L; reason = "original_hostile_owner_or_world_unavailable";
            if (defender < 1 || defender > 8 || originalAlliedTo == null)
                return false;
            foreach (OwnedWorldUnit entry in _live)
            {
                if (!NearKeep(entry.Unit, centerX, centerY)) continue;
                if (entry.Owner < 1 || entry.Owner > 8)
                {
                    reason = "unowned_original_combat_unit_near_keep";
                    return false;
                }
                if (entry.Owner == defender ||
                    originalAlliedTo(entry.Owner, defender) &&
                    originalAlliedTo(defender, entry.Owner))
                    continue;
                if (entry.Unit.Strength > Int32.MaxValue - threat)
                {
                    reason = "original_hostile_strength_overflow";
                    return false;
                }
                threat += entry.Unit.Strength;
            }
            reason = "original_tribe_owner_hostile_strength";
            return true;
        }

        // A home reserve counts only proven original OWNER=donor combat units
        // within the original keep envelope. Unknown combat near home blocks
        // the whole sender candidate, rather than padding a safe reserve.
        internal bool TryHomeStrength(int donor, int centerX, int centerY,
            out long home, out string reason)
        {
            home = 0L; reason = "original_home_strength_unavailable";
            if (donor < 1 || donor > 8) return false;
            foreach (OwnedWorldUnit entry in _live)
            {
                if (!NearKeep(entry.Unit, centerX, centerY)) continue;
                if (entry.Owner < 1 || entry.Owner > 8)
                {
                    reason = "unowned_original_combat_unit_near_home";
                    return false;
                }
                if (entry.Owner != donor) continue;
                if (entry.Unit.Strength > Int32.MaxValue - home)
                {
                    reason = "original_home_strength_overflow";
                    return false;
                }
                home += entry.Unit.Strength;
            }
            reason = "actual_original_grouped_home_strength";
            return true;
        }
    }
}
#endif
