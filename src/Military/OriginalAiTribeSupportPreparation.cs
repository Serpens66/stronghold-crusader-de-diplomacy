// Pure, typed preparation of a possible existing allied tribe support order.
// Only the existing strategic planner produces an intent. This component
// creates detached data; it never dispatches original game or native orders.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal enum AiSupportTargetKind : byte
    {
        OriginalLiveAllyTile = 1,
        OriginalLiveEnemyBuilding = 2
    }

    // A complete set of caller-supplied OBSERVED facts, not claimed proof that
    // the running game/host will permit an order.
    internal sealed class AiSupportSessionFacts
    {
        internal readonly long Generation, MapEpoch, Tick, CancellationSerial;
        internal readonly int Day, MapWidthTiles, MapHeightTiles;
        internal readonly bool MapActive, MapDisabled, Cancelled;
        internal readonly bool? OriginalLiveMapIdentityVerified, OriginalLiveMapGeometryVerified;
        internal readonly string CurrentMapIdentitySha256;
        internal readonly string InstalledShcdeseSha256, InstalledShcdeseMvid;
        internal AiSupportSessionFacts(long generation, long mapEpoch, long tick, long cancellationSerial,
            int day, int width, int height,
            bool mapActive, bool mapDisabled, bool cancelled,
            bool? liveMapIdentity, bool? liveMapGeometry,
            string mapIdentity, string extenderSha, string extenderMvid)
        {
            Generation = generation; MapEpoch = mapEpoch; Tick = tick;
            CancellationSerial = cancellationSerial; Day = day;
            MapWidthTiles = width; MapHeightTiles = height;
            MapActive = mapActive; MapDisabled = mapDisabled; Cancelled = cancelled;
            OriginalLiveMapIdentityVerified = liveMapIdentity;
            OriginalLiveMapGeometryVerified = liveMapGeometry;
            CurrentMapIdentitySha256 = mapIdentity;
            InstalledShcdeseSha256 = extenderSha; InstalledShcdeseMvid = extenderMvid;
        }
    }

    // Detached copies of the complete engine session registers and peer
    // roster. The existing SoloSessionPolicy is the sole classifier; its
    // definition permits synthetic network members in actual solo games.
    // Caller-supplied host flags cannot replace these observations.
    internal sealed class AiSupportSoloSessionFacts
    {
        internal readonly int NativeLocalPlayer, SnapshotLocalPlayer;
        internal readonly int? SkirmishGameType, CooperativeTrailId;
        internal readonly bool Networked;
        internal readonly bool? LocalHostVerified, OriginalGameContextReady;
        private readonly bool[] _humans, _managedAis, _nativeAis;
        private readonly SessionMember[] _members;

        internal AiSupportSoloSessionFacts(int nativeLocal, int snapshotLocal,
            int? skirmishGameType, int? cooperativeTrailId, bool networked,
            bool? localHostVerified, bool? originalGameContextReady,
            bool[] humans, bool[] managedAis, bool[] nativeAis,
            IEnumerable<SessionMember> members)
        {
            NativeLocalPlayer = nativeLocal;
            SnapshotLocalPlayer = snapshotLocal;
            SkirmishGameType = skirmishGameType;
            CooperativeTrailId = cooperativeTrailId;
            Networked = networked;
            LocalHostVerified = localHostVerified;
            OriginalGameContextReady = originalGameContextReady;
            _humans = humans == null ? null : (bool[])humans.Clone();
            _managedAis = managedAis == null ? null : (bool[])managedAis.Clone();
            _nativeAis = nativeAis == null ? null : (bool[])nativeAis.Clone();
            _members = members == null ? null : members.Select(CloneMember).ToArray();
        }

        private static SessionMember CloneMember(SessionMember member)
        {
            return member == null ? null : new SessionMember
            {
                Slot = member.Slot,
                Self = member.Self,
                RealIdentity = member.RealIdentity
            };
        }

        internal bool[] HumanSnapshot()
        {
            return _humans == null ? null : (bool[])_humans.Clone();
        }

        internal bool[] ManagedAiSnapshot()
        {
            return _managedAis == null ? null : (bool[])_managedAis.Clone();
        }

        internal bool[] NativeAiSnapshot()
        {
            return _nativeAis == null ? null : (bool[])_nativeAis.Clone();
        }

        internal SessionMember[] PeerSnapshot()
        {
            return _members == null ? null : _members.Select(CloneMember).ToArray();
        }

        internal bool OriginalRegistersAgreeOnAi(byte slot)
        {
            return slot >= 1 && slot <= 8 &&
                _humans != null && _humans.Length == 9 &&
                _managedAis != null && _managedAis.Length == 9 &&
                _nativeAis != null && _nativeAis.Length == 9 &&
                !_humans[slot] && _managedAis[slot] && _nativeAis[slot];
        }

        private static bool ArraysEqual(bool[] a, bool[] b)
        {
            if (a == null || b == null)
                return a == b;
            return a.SequenceEqual(b);
        }

        internal bool SamePlayerRegistersAndPeers(AiSupportSoloSessionFacts other)
        {
            if (other == null || NativeLocalPlayer != other.NativeLocalPlayer ||
                SnapshotLocalPlayer != other.SnapshotLocalPlayer ||
                Networked != other.Networked ||
                SkirmishGameType != other.SkirmishGameType ||
                CooperativeTrailId != other.CooperativeTrailId ||
                LocalHostVerified != other.LocalHostVerified ||
                OriginalGameContextReady != other.OriginalGameContextReady ||
                !ArraysEqual(_humans, other._humans) ||
                !ArraysEqual(_managedAis, other._managedAis) ||
                !ArraysEqual(_nativeAis, other._nativeAis))
                return false;
            if (_members == null || other._members == null)
                return _members == other._members;
            if (_members.Length != other._members.Length ||
                _members.Any(x => x == null) ||
                other._members.Any(x => x == null))
                return false;
            SessionMember[] oldMembers = _members.OrderBy(x => x.Slot).ToArray();
            SessionMember[] newMembers = other._members.OrderBy(x => x.Slot).ToArray();
            for (int i = 0; i < oldMembers.Length; i++)
                if (oldMembers[i].Slot != newMembers[i].Slot ||
                    oldMembers[i].Self != newMembers[i].Self ||
                    oldMembers[i].RealIdentity != newMembers[i].RealIdentity)
                    return false;
            return true;
        }
    }

    // Nullable booleans distinguish unobserved roles from known negative facts.
    // Live ally/enemy observations must be attested independently of host
    // flags, human HUD input and caller-supplied player slot numbers.
    internal sealed class AiSupportRoleFacts
    {
        internal readonly byte Donor, Recipient, Enemy;
        internal readonly bool? DonorAi, RecipientAi, DonorAlive, RecipientAlive;
        internal readonly bool? MutualAlliance, RouteSafe, TreatyAllows;
        internal readonly bool? EnemyAlive, EnemyHostileToDonor, EnemyHostileToRecipient;
        internal AiSupportRoleFacts(byte donor, byte recipient, byte enemy,
            bool? donorAi, bool? recipientAi, bool? donorAlive, bool? recipientAlive,
            bool? mutualAlliance, bool? routeSafe, bool? treatyAllows,
            bool? enemyAlive, bool? hostileToDonor, bool? hostileToRecipient)
        {
            Donor = donor; Recipient = recipient; Enemy = enemy;
            DonorAi = donorAi; RecipientAi = recipientAi;
            DonorAlive = donorAlive; RecipientAlive = recipientAlive;
            MutualAlliance = mutualAlliance; RouteSafe = routeSafe; TreatyAllows = treatyAllows;
            EnemyAlive = enemyAlive; EnemyHostileToDonor = hostileToDonor;
            EnemyHostileToRecipient = hostileToRecipient;
        }
    }

    internal sealed class AiSupportMemberWitness
    {
        internal readonly int UnitId, ActualTribeId;
        internal readonly uint UnitGlobalId;
        internal readonly bool? CurrentMembershipVerified;
        internal AiSupportMemberWitness(int unitId, uint globalId, int actualTribeId,
            bool? currentMembershipVerified)
        {
            UnitId = unitId; UnitGlobalId = globalId;
            ActualTribeId = actualTribeId; CurrentMembershipVerified = currentMembershipVerified;
        }
    }

    internal sealed class AiSupportTribeWitness
    {
        internal readonly int TribeId, CombatPower;
        internal readonly uint TribeGlobalId;
        internal readonly byte ActualOwnerPlayer;
        internal readonly bool? Alive, OriginalOwnerVerified, OriginalGlobalIdVerified;
        internal readonly bool? OriginalMembershipComplete, CombatPowerVerified;
        internal readonly long Generation, MapEpoch, Tick;
        private readonly AiSupportMemberWitness[] _members;
        internal AiSupportTribeWitness(int tribeId, uint globalId, byte ownerPlayer, int combatPower,
            bool? alive, bool? originalOwnerVerified, bool? originalGlobalIdVerified,
            bool? originalMembershipComplete, bool? combatPowerVerified,
            long generation, long mapEpoch, long tick,
            IEnumerable<AiSupportMemberWitness> members)
        {
            TribeId = tribeId; TribeGlobalId = globalId; ActualOwnerPlayer = ownerPlayer;
            CombatPower = combatPower; Alive = alive; OriginalOwnerVerified = originalOwnerVerified;
            OriginalGlobalIdVerified = originalGlobalIdVerified;
            OriginalMembershipComplete = originalMembershipComplete; CombatPowerVerified = combatPowerVerified;
            Generation = generation; MapEpoch = mapEpoch; Tick = tick;
            _members = members == null ? null : members.ToArray();
        }
        internal AiSupportMemberWitness[] DetachedMembers()
        {
            return _members == null ? null : (AiSupportMemberWitness[])_members.Clone();
        }
    }

    internal sealed class AiSupportDestinationWitness
    {
        internal readonly AiSupportTargetKind Kind;
        internal readonly byte ActualTargetOwner;
        internal readonly int TileX, TileY, BuildingId;
        internal readonly uint BuildingGlobalId;
        internal readonly bool? OriginalLiveIdentityVerified, OriginalOwnerVerified;
        internal readonly bool? TileCoordinatesVerified, TargetAlive;
        internal readonly long Generation, MapEpoch, Tick;
        internal readonly string MapIdentitySha256;
        internal AiSupportDestinationWitness(AiSupportTargetKind kind, byte targetOwner,
            int tileX, int tileY, int buildingId, uint buildingGlobalId,
            bool? liveIdentity, bool? ownerVerified, bool? tileCoordinatesVerified, bool? targetAlive,
            long generation, long mapEpoch, long tick, string mapIdentity)
        {
            Kind = kind; ActualTargetOwner = targetOwner; TileX = tileX; TileY = tileY;
            BuildingId = buildingId; BuildingGlobalId = buildingGlobalId;
            OriginalLiveIdentityVerified = liveIdentity;
            OriginalOwnerVerified = ownerVerified;
            TileCoordinatesVerified = tileCoordinatesVerified; TargetAlive = targetAlive;
            Generation = generation; MapEpoch = mapEpoch; Tick = tick;
            MapIdentitySha256 = mapIdentity;
        }
    }

    internal sealed class AiSupportConflictFacts
    {
        internal readonly bool? NoActiveSameTribeOrder, NoDonorOrRecipientConflict;
        internal readonly long LastRelevantOrderTick;
        internal readonly int CooldownTicks;
        internal AiSupportConflictFacts(bool? noTribeConflict, bool? noPairConflict,
            long lastRelevantOrderTick, int cooldownTicks)
        {
            NoActiveSameTribeOrder = noTribeConflict;
            NoDonorOrRecipientConflict = noPairConflict;
            LastRelevantOrderTick = lastRelevantOrderTick;
            CooldownTicks = cooldownTicks;
        }
    }

    internal sealed class AiSupportPreparationFacts
    {
        internal readonly AiSupportSessionFacts Session;
        internal readonly AiSupportSoloSessionFacts Solo;
        internal readonly AiSupportRoleFacts Roles;
        internal readonly AiSupportTribeWitness Tribe;
        internal readonly AiSupportDestinationWitness Destination;
        internal readonly AiSupportConflictFacts Conflicts;
        private readonly AiMilitaryRulerFacts[] _rulers;
        private readonly AiMilitaryCooperationPact[] _pacts;
        internal AiSupportPreparationFacts(AiSupportSessionFacts session,
            AiSupportSoloSessionFacts solo, AiSupportRoleFacts roles,
            AiSupportTribeWitness tribe, AiSupportDestinationWitness destination,
            AiSupportConflictFacts conflicts, IEnumerable<AiMilitaryRulerFacts> rulers,
            IEnumerable<AiMilitaryCooperationPact> pacts)
        {
            Session = session; Solo = solo; Roles = roles; Tribe = tribe; Destination = destination;
            Conflicts = conflicts;
            _rulers = rulers == null ? null : rulers.Select(CloneRuler).ToArray();
            _pacts = pacts == null ? null : pacts.Select(ClonePact).ToArray();
        }
        private static AiMilitaryRulerFacts CloneRuler(AiMilitaryRulerFacts x)
        {
            return x == null ? null : new AiMilitaryRulerFacts
            {
                PlayerId = x.PlayerId, Human = x.Human, LastStandVerified = x.LastStandVerified,
                BusyWithSiege = x.BusyWithSiege, MilitaryStrength = x.MilitaryStrength,
                CastleDefenseReserve = x.CastleDefenseReserve, ThreatStrength = x.ThreatStrength,
                CommonEnemyPlayer = x.CommonEnemyPlayer
            };
        }
        private static AiMilitaryCooperationPact ClonePact(AiMilitaryCooperationPact x)
        {
            return x == null ? null : new AiMilitaryCooperationPact
            {
                A = x.A, B = x.B,
                ReciprocalAllianceConfirmed = x.ReciprocalAllianceConfirmed,
                Trust = x.Trust, Grudge = x.Grudge,
                LastCooperationDay = x.LastCooperationDay,
                HasDirectionalFavorEvidence = x.HasDirectionalFavorEvidence,
                SignedOutstandingFavor = x.SignedOutstandingFavor,
                RouteVerified = x.RouteVerified
            };
        }
        internal AiMilitaryRulerFacts[] RulerSnapshot()
        {
            return _rulers == null ? null : _rulers.Select(CloneRuler).ToArray();
        }
        internal AiMilitaryCooperationPact[] PactSnapshot()
        {
            return _pacts == null ? null : _pacts.Select(ClonePact).ToArray();
        }
    }

    // The only allowed output is an immutable descriptive draft. It contains
    // no delegate, native pointer, action interface, host ticket or permission
    // to call MoveTo/AttackBuildingEx. Exact signed flags are for audit only.
    internal sealed class AiSupportOrderDraft
    {
        internal readonly AiMilitaryCooperationKind Strategy;
        internal readonly byte Donor, Recipient, Enemy;
        internal readonly int Day, Priority, TribeId, TribePower;
        internal readonly uint TribeGlobalId;
        internal readonly AiSupportTargetKind Kind;
        internal readonly int TileX, TileY, BuildingId, BuildingGlobalId;
        internal readonly bool MoveIsPatrolPath;
        internal readonly int MoveIsNewOrder, MoveTypeValue, AttackCommandValue, AttackArg3;
        internal readonly long Generation, MapEpoch, ObservedTick, ExpiresAfterTick, CancellationSerial;
        internal readonly string MapIdentitySha256;
        private readonly AiSupportMemberWitness[] _members;
        private readonly AiSupportSoloSessionFacts _soloIdentity;
        internal bool NativeAck { get { return false; } }
        internal bool CanDispatch { get { return false; } }
        internal bool OriginalAiRequesterProven { get { return false; } }

        internal AiSupportOrderDraft(AiMilitaryCooperationIntent intent, AiSupportPreparationFacts facts,
            long expiration, AiSupportMemberWitness[] members)
        {
            Strategy = intent.Kind; Donor = intent.Donor; Recipient = intent.Recipient;
            Enemy = (byte)intent.Enemy; Day = intent.Day; Priority = intent.Priority;
            TribeId = facts.Tribe.TribeId; TribeGlobalId = facts.Tribe.TribeGlobalId;
            TribePower = facts.Tribe.CombatPower;
            Kind = facts.Destination.Kind;
            TileX = Kind == AiSupportTargetKind.OriginalLiveAllyTile
                ? facts.Destination.TileX : -1;
            TileY = Kind == AiSupportTargetKind.OriginalLiveAllyTile
                ? facts.Destination.TileY : -1;
            BuildingId = Kind == AiSupportTargetKind.OriginalLiveEnemyBuilding
                ? facts.Destination.BuildingId : -1;
            BuildingGlobalId = Kind == AiSupportTargetKind.OriginalLiveEnemyBuilding
                ? (int)facts.Destination.BuildingGlobalId : -1;
            MoveIsPatrolPath = false;
            MoveIsNewOrder = Kind == AiSupportTargetKind.OriginalLiveAllyTile ? 1 : 0;
            MoveTypeValue = Kind == AiSupportTargetKind.OriginalLiveAllyTile ? 1 : 0;
            AttackCommandValue = Kind == AiSupportTargetKind.OriginalLiveEnemyBuilding ? 9 : 0;
            AttackArg3 = 0;
            Generation = facts.Session.Generation; MapEpoch = facts.Session.MapEpoch;
            ObservedTick = facts.Session.Tick; ExpiresAfterTick = expiration;
            CancellationSerial = facts.Session.CancellationSerial;
            MapIdentitySha256 = facts.Session.CurrentMapIdentitySha256;
            _soloIdentity = facts.Solo;
            _members = (AiSupportMemberWitness[])members.Clone();
        }
        internal AiSupportMemberWitness[] DetachedMembers()
        {
            return (AiSupportMemberWitness[])_members.Clone();
        }
        internal bool SameSoloSessionIdentity(AiSupportSoloSessionFacts current)
        {
            return _soloIdentity != null &&
                _soloIdentity.SamePlayerRegistersAndPeers(current);
        }
    }

    internal sealed class AiSupportPreparationDecision
    {
        internal readonly string Reason;
        internal readonly AiSupportOrderDraft Draft;
        internal bool HasDetachedDraft { get { return Draft != null; } }
        internal bool CanDispatch { get { return false; } }
        internal bool NativeAck { get { return false; } }
        internal AiSupportPreparationDecision(string reason, AiSupportOrderDraft draft)
        {
            Reason = reason; Draft = draft;
        }
    }

    internal static class OriginalAiTribeSupportPreparation
    {
        // Only the installed original extender ABI is verified at runtime.
        // Source-selection provenance is validated externally at build/review.
        internal const string OriginalShcdeseSha256 =
            "5cb1c33be5463086505a17f5c48aaa357d733cfff7240e101c821f0e3c668498";
        internal const string OriginalShcdeseMvid = "aab7af1d-dabf-4f55-a7a6-dd67c8f06853";

        private static AiSupportPreparationDecision Refuse(string reason)
        {
            return new AiSupportPreparationDecision(reason, null);
        }
        private static bool Exact(bool? fact)
        {
            return fact.HasValue && fact.Value;
        }
        private static string Required(bool? fact, string name)
        {
            return !fact.HasValue ? name + "_unavailable" :
                fact.Value ? null : name + "_refused";
        }
        private static bool LowerHex64(string s)
        {
            if (s == null || s.Length != 64)
                return false;
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            return true;
        }

        internal static AiSupportPreparationDecision Prepare(AiSupportPreparationFacts input)
        {
            if (input == null || input.Session == null)
                return Refuse("session_unavailable");
            AiSupportSessionFacts s = input.Session;
            if (s.Generation < 1 || s.MapEpoch < 1 || s.Tick < 0 ||
                s.Day < 1 || s.CancellationSerial < 0 ||
                s.MapWidthTiles < 1 || s.MapHeightTiles < 1 ||
                !LowerHex64(s.CurrentMapIdentitySha256))
                return Refuse("current_session_identity_unavailable");
            if (!String.Equals(s.InstalledShcdeseSha256, OriginalShcdeseSha256, StringComparison.Ordinal) ||
                !String.Equals(s.InstalledShcdeseMvid, OriginalShcdeseMvid, StringComparison.Ordinal))
                return Refuse("original_extender_abi_unavailable");
            if (!s.MapActive || s.MapDisabled)
                return Refuse("current_map_unavailable");
            string reason;
            if ((reason = Required(s.OriginalLiveMapIdentityVerified, "original_live_map_identity")) != null)
                return Refuse(reason);
            if ((reason = Required(s.OriginalLiveMapGeometryVerified, "original_live_tile_grid")) != null)
                return Refuse(reason);
            if (input.Solo == null)
                return Refuse("original_solo_registers_unavailable");
            AiSupportSoloSessionFacts solo = input.Solo;
            if (!solo.SkirmishGameType.HasValue || !solo.CooperativeTrailId.HasValue)
                return Refuse("original_solo_mode_unavailable");
            if (solo.SkirmishGameType.Value != 0 || solo.CooperativeTrailId.Value != 0)
                return Refuse("original_solo_mode_refused");
            if ((reason = Required(solo.OriginalGameContextReady, "original_solo_game_context")) != null)
                return Refuse(reason);
            if ((reason = Required(solo.LocalHostVerified, "actual_local_host")) != null)
                return Refuse(reason);

            // Use the canonical sole-human register/peer policy directly.
            // In original solo sessions, Networked may legitimately be true
            // because one local human can have synthetic MPGameMember peers.
            string soloReason;
            if (!SoloSessionPolicy.Allows(
                solo.NativeLocalPlayer, solo.SnapshotLocalPlayer,
                solo.HumanSnapshot(), solo.ManagedAiSnapshot(),
                solo.NativeAiSnapshot(), solo.PeerSnapshot(),
                solo.Networked, out soloReason))
                return Refuse("solo_session_" + soloReason);
            if (s.Cancelled)
                return Refuse("cancelled");
            if (s.Tick == Int64.MaxValue)
                return Refuse("tick_overflow_unavailable");

            if (input.Roles == null)
                return Refuse("live_roles_unavailable");
            AiSupportRoleFacts roles = input.Roles;
            if (roles.Donor < 1 || roles.Donor > 8 ||
                roles.Recipient < 1 || roles.Recipient > 8 ||
                roles.Donor == roles.Recipient ||
                roles.Donor == solo.NativeLocalPlayer || roles.Recipient == solo.NativeLocalPlayer)
                return Refuse("distinct_real_AI_slots_unavailable");
            if ((reason = Required(roles.DonorAi, "donor_AI")) != null) return Refuse(reason);
            if ((reason = Required(roles.RecipientAi, "recipient_AI")) != null) return Refuse(reason);
            if (!solo.OriginalRegistersAgreeOnAi(roles.Donor))
                return Refuse("original_donor_AI_register_unavailable");
            if (!solo.OriginalRegistersAgreeOnAi(roles.Recipient))
                return Refuse("original_recipient_AI_register_unavailable");
            if ((reason = Required(roles.DonorAlive, "donor_alive")) != null) return Refuse(reason);
            if ((reason = Required(roles.RecipientAlive, "recipient_alive")) != null) return Refuse(reason);
            if ((reason = Required(roles.MutualAlliance, "reciprocal_alliance")) != null) return Refuse(reason);
            if ((reason = Required(roles.RouteSafe, "independent_route")) != null) return Refuse(reason);
            if ((reason = Required(roles.TreatyAllows, "treaty_permission")) != null) return Refuse(reason);

            AiMilitaryRulerFacts[] rulers = input.RulerSnapshot();
            AiMilitaryCooperationPact[] pacts = input.PactSnapshot();
            if (rulers == null || pacts == null)
                return Refuse("existing_planner_snapshot_unavailable");
            AiMilitaryCooperationIntent[] ideas;
            try
            {
                // IMPORTANT: consume the existing planner exactly, do NOT
                // duplicate/relax trust, goodwill or strategic thresholds.
                ideas = AiAllianceMilitaryPlanner.Plan(s.Day, rulers, pacts, 2);
            }
            catch (ArgumentException)
            {
                return Refuse("original_strategic_planner_input_invalid");
            }
            AiMilitaryCooperationIntent[] selected = ideas.Where(i =>
                i.Donor == roles.Donor && i.Recipient == roles.Recipient).ToArray();
            if (selected.Length != 1)
                return Refuse("original_strategic_intent_unavailable");
            AiMilitaryCooperationIntent intent = selected[0];
            AiMilitaryRulerFacts donor = rulers.Single(r => r.PlayerId == roles.Donor);
            if (donor.Human || intent.Day != s.Day || intent.Priority < 1)
                return Refuse("strategic_role_or_day_conflict");

            AiSupportTribeWitness tribe = input.Tribe;
            if (tribe == null)
                return Refuse("live_tribe_witness_unavailable");
            if (tribe.Generation != s.Generation || tribe.MapEpoch != s.MapEpoch ||
                tribe.Tick != s.Tick)
                return Refuse("tribe_observation_generation_conflict");
            if (tribe.TribeId < 1 || tribe.TribeGlobalId == 0 ||
                tribe.ActualOwnerPlayer != roles.Donor)
                return Refuse("tribe_owner_or_global_identity_mismatch");
            if ((reason = Required(tribe.Alive, "original_tribe_alive")) != null) return Refuse(reason);
            if ((reason = Required(tribe.OriginalOwnerVerified, "original_tribe_owner")) != null) return Refuse(reason);
            if ((reason = Required(tribe.OriginalGlobalIdVerified, "original_tribe_globalID")) != null) return Refuse(reason);
            if ((reason = Required(tribe.OriginalMembershipComplete, "original_tribe_membership")) != null) return Refuse(reason);
            if ((reason = Required(tribe.CombatPowerVerified, "current_combat_power")) != null) return Refuse(reason);
            AiSupportMemberWitness[] members = tribe.DetachedMembers();
            if (members == null || members.Length < 1 || members.Length > 10000 ||
                members.Any(x => x == null || x.UnitId < 1 || x.UnitId >= 10000 ||
                  x.UnitGlobalId == 0 || x.ActualTribeId != tribe.TribeId) ||
                members.GroupBy(x => x.UnitId).Any(x => x.Count() != 1))
                return Refuse("original_member_identity_or_count_unavailable");
            // An original GetByGlobalId lookup may choose the first match.
            // Never treat two different members with one positive GlobalId as
            // an unambiguous complete-group identity, without claiming that
            // native global-ID uniqueness has been independently proved.
            if (members.GroupBy(x => x.UnitGlobalId).Any(x => x.Count() != 1))
                return Refuse("duplicate_unit_globalID_ambiguous");
            if (members.Any(x => !Exact(x.CurrentMembershipVerified)))
                return Refuse("original_member_link_unavailable");
            if (tribe.CombatPower < 1 || tribe.CombatPower > donor.MilitaryStrength)
                return Refuse("verified_member_power_unavailable");
            long requiredAtHome = MilitaryReservePolicy.RequiredAtHome(
                donor.CastleDefenseReserve, donor.ThreatStrength);
            if ((long)donor.MilitaryStrength - tribe.CombatPower < requiredAtHome)
                return Refuse("castle_defence_reserve_blocked");

            AiSupportConflictFacts conflicts = input.Conflicts;
            if (conflicts == null)
                return Refuse("original_order_conflict_witness_unavailable");
            if ((reason = Required(conflicts.NoActiveSameTribeOrder, "tribe_order_slot_free")) != null) return Refuse(reason);
            if ((reason = Required(conflicts.NoDonorOrRecipientConflict, "donor_recipient_slot_free")) != null) return Refuse(reason);
            if (conflicts.CooldownTicks < 1 || conflicts.CooldownTicks > 100000 ||
                conflicts.LastRelevantOrderTick < -1 ||
                conflicts.LastRelevantOrderTick > s.Tick)
                return Refuse("order_cooldown_snapshot_unavailable");
            if (conflicts.LastRelevantOrderTick >= 0 &&
                s.Tick - conflicts.LastRelevantOrderTick < conflicts.CooldownTicks)
                return Refuse("order_cooldown_active");

            AiSupportDestinationWitness target = input.Destination;
            if (target == null)
                return Refuse("independent_original_target_witness_unavailable");
            if (target.Generation != s.Generation || target.MapEpoch != s.MapEpoch ||
                target.Tick != s.Tick ||
                !String.Equals(target.MapIdentitySha256, s.CurrentMapIdentitySha256, StringComparison.Ordinal))
                return Refuse("target_map_or_generation_conflict");
            if ((reason = Required(target.OriginalLiveIdentityVerified, "original_target_identity")) != null) return Refuse(reason);
            if ((reason = Required(target.OriginalOwnerVerified, "original_target_owner")) != null) return Refuse(reason);

            // Tiles have no "alive" predicate. Only the building attack path
            // requires an original, live target-entity witness.
            if (intent.Kind == AiMilitaryCooperationKind.DefendAlly)
            {
                if (intent.Enemy != 0 || target.Kind != AiSupportTargetKind.OriginalLiveAllyTile ||
                    target.ActualTargetOwner != roles.Recipient)
                    return Refuse("defence_target_owner_or_kind_mismatch");
                if ((reason = Required(target.TileCoordinatesVerified, "original_tile_coordinates")) != null)
                    return Refuse(reason);
                if (target.TileX < 0 || target.TileY < 0 ||
                    target.TileX >= s.MapWidthTiles || target.TileY >= s.MapHeightTiles)
                    return Refuse("original_tile_bounds_unavailable");
            }
            else if (intent.Kind == AiMilitaryCooperationKind.CoordinateSiege)
            {
                if (intent.Enemy < 1 || intent.Enemy > 8 ||
                    roles.Enemy != (byte)intent.Enemy ||
                    intent.Enemy == roles.Donor || intent.Enemy == roles.Recipient ||
                    target.Kind != AiSupportTargetKind.OriginalLiveEnemyBuilding ||
                    target.ActualTargetOwner != (byte)intent.Enemy)
                    return Refuse("hostile_building_owner_or_kind_mismatch");
                if ((reason = Required(roles.EnemyAlive, "original_enemy_alive")) != null) return Refuse(reason);
                if ((reason = Required(target.TargetAlive, "original_target_building_alive")) != null)
                    return Refuse(reason);
                if ((reason = Required(roles.EnemyHostileToDonor, "donor_enemy_relation")) != null) return Refuse(reason);
                if ((reason = Required(roles.EnemyHostileToRecipient, "recipient_enemy_relation")) != null) return Refuse(reason);
                // Original API takes int targetBuildingId + int targetGlobalId.
                // Do not cast unsigned global IDs that cannot fit in int.
                if (target.BuildingId < 1 || target.BuildingGlobalId == 0 ||
                    target.BuildingGlobalId > Int32.MaxValue)
                    return Refuse("original_building_globalID_unavailable");
            }
            else
                return Refuse("unsupported_existing_strategic_intent");

            return new AiSupportPreparationDecision(
                "detached_preparation_only_native_actor_and_replay_unproven",
                new AiSupportOrderDraft(intent, input, s.Tick + 1L, members));
        }

        // Recheck detached original witnesses before any continued planning.
        // Map, player roster or tick changes cannot convey dispatch authority.
        internal static AiSupportPreparationDecision Recheck(AiSupportOrderDraft oldDraft,
            AiSupportPreparationFacts fresh)
        {
            if (oldDraft == null || fresh == null || fresh.Session == null)
                return Refuse("previous_draft_or_session_unavailable");
            AiSupportSessionFacts s = fresh.Session;
            if (s.Cancelled || s.CancellationSerial != oldDraft.CancellationSerial)
                return Refuse("cancelled_or_revoked");
            if (s.Generation != oldDraft.Generation || s.MapEpoch != oldDraft.MapEpoch ||
                !String.Equals(s.CurrentMapIdentitySha256, oldDraft.MapIdentitySha256,
                    StringComparison.Ordinal))
                return Refuse("map_or_generation_changed");
            if (s.Tick < oldDraft.ObservedTick || s.Tick > oldDraft.ExpiresAfterTick)
                return Refuse("preparation_expired_or_time_regressed");
            if (s.Day != oldDraft.Day)
                return Refuse("strategic_day_changed");
            if (!oldDraft.SameSoloSessionIdentity(fresh.Solo))
                return Refuse("solo_session_roster_changed");
            AiSupportPreparationDecision newer = Prepare(fresh);
            if (!newer.HasDetachedDraft)
                return newer;
            AiSupportOrderDraft current = newer.Draft;
            if (current.TribeId != oldDraft.TribeId ||
                current.TribeGlobalId != oldDraft.TribeGlobalId ||
                current.Donor != oldDraft.Donor || current.Recipient != oldDraft.Recipient ||
                current.Enemy != oldDraft.Enemy || current.Strategy != oldDraft.Strategy ||
                current.Priority != oldDraft.Priority ||
                current.Kind != oldDraft.Kind || current.TileX != oldDraft.TileX ||
                current.TileY != oldDraft.TileY ||
                current.BuildingId != oldDraft.BuildingId ||
                current.BuildingGlobalId != oldDraft.BuildingGlobalId ||
                current.TribePower != oldDraft.TribePower)
                return Refuse("original_tribe_or_target_changed");
            AiSupportMemberWitness[] previous = oldDraft.DetachedMembers();
            AiSupportMemberWitness[] members = current.DetachedMembers();
            if (previous.Length != members.Length)
                return Refuse("original_member_list_changed");
            var prevById = previous.OrderBy(m => m.UnitId).ToArray();
            var currById = members.OrderBy(m => m.UnitId).ToArray();
            for (int i = 0; i < prevById.Length; i++)
                if (prevById[i].UnitId != currById[i].UnitId ||
                    prevById[i].UnitGlobalId != currById[i].UnitGlobalId ||
                    prevById[i].ActualTribeId != currById[i].ActualTribeId)
                    return Refuse("original_member_list_changed");
            return new AiSupportPreparationDecision(
                "fresh_detached_preparation_only_still_no_send_authority", current);
        }
    }
}
