using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private sealed class MilitaryPosition
        {
            internal int Owner, X, Y;
            internal long Value;
        }

        private MilitaryPosition[] _militaryPositions;
        private Dictionary<int, int> _militaryTeams;
        private long _militaryByPlayerTick = long.MinValue;
        private long _militaryGeneration = -1;
        private long _militaryEpoch = -1;
        private long _militaryFailedTick = long.MinValue;
        private long _militaryFailedGeneration = -1;
        private long _militaryFailedEpoch = -1;
        private int _militaryFailedDay = -1;
        private long _submissionQuoteReviewTick = long.MinValue;
        private readonly Dictionary<string, int> _submissionVerifiedQuotes = new Dictionary<string, int>(StringComparer.Ordinal);
        private void ClearSubmissionPressure()
        {
            _militaryPositions = null;
            _militaryTeams = null;
            _militaryByPlayerTick = long.MinValue;
            _militaryGeneration = _militaryEpoch = -1;
            _militaryFailedTick = long.MinValue;
            _militaryFailedGeneration = _militaryFailedEpoch = -1;
            _militaryFailedDay = -1;
            _submissionQuoteReviewTick = long.MinValue;
            _submissionVerifiedQuotes.Clear();
        }

        // This is an engine observation, not the persisted diplomacy pressure
        // history. Never reuse positions after Load, a map change, or movement
        // during a later simulation tick when taking a new decision.
        private bool TrySubmissionPressureBand(int player, int lord, int day, out int band)
        {
            try
            {
                return CaptureSubmissionPressureBand(player, lord, day, out band);
            }
            catch (Exception exception)
            {
                band = 0;
                NoteVassalOrderFault("submission_pressure", exception);
                return false;
            }
        }

        private bool CaptureSubmissionPressureBand(int player, int lord, int day, out int band)
        {
            RequireNativeDiplomacySection();
            band = 0;
            if (player < 1 || player > 8 || player == _localPlayer)
                return false;
            // An unfinished castle scan cannot masquerade as an empty castle.
            // In particular it must not omit allied defenders. The completed
            // snapshot is what guarantees that: _castleBoundaries is only
            // ever replaced by a finished scan, and _castleSnapshotReady is
            // only cleared when the map changes.
            // this used to also reject a running rescan and a
            // pending refresh request, which made the whole feature
            // unreachable in a real game. Every building, wall, demolition and
            // bulldoze sets _castleRefreshRequested, and it is only cleared
            // when the NEXT scan starts - at which point _castleBuilder is
            // non-null. A full scan needs 800*800/4096 = 157 ticks plus the
            // geometry pass, so in any live map with eight builders the three
            // conditions were never simultaneously true and the answer was
            // permanently "still being assessed". A rescan means the data may
            // age, not that there is no data.
            if (!_castleSnapshotReady || _castleEpoch != _mapEpoch)
                return false;
            long tick = ReadSimulationTick();
            if (_militaryFailedTick == tick && _militaryFailedDay == day && _militaryFailedGeneration == _sessionGeneration && _militaryFailedEpoch == _mapEpoch)
                return false;
            WorldForceUnit keep;
            if (!TryGetFixedKeepCenter(player, out keep))
                return false;
            Dictionary<int, long> military;
            try
            {
                military = MilitaryByPlayer(day, true);
            }
            catch
            {
                // Only the shared unit/team observation is negatively cached.
                // A missing target keep must not poison other rulers' answers.
                // This suppresses repeated full failed scans in one simulation
                // tick while automatically retrying the next tick/map/session.
                _militaryFailedTick = tick;
                _militaryFailedDay = day;
                _militaryFailedGeneration = _sessionGeneration;
                _militaryFailedEpoch = _mapEpoch;
                throw;
            }

            if (_militaryPositions == null || _militaryTeams == null)
                return false;
            int targetTeam;
            if (!_militaryTeams.TryGetValue(player, out targetTeam))
                return false;
            DiplomacyCastleBoundary boundary = null;
            DiplomacyCastleBoundary[] boundaries = Volatile.Read(ref _castleBoundaries);
            if (boundaries != null && player < boundaries.Length)
                boundary = boundaries[player];
            var zone = new TreatyZone
            {
                Owner = player,
                X = keep.X,
                Y = keep.Y,
                Boundary = boundary
            };
            int attackingUnits = 0;
            long attacking = 0, defending = 0;
            foreach (MilitaryPosition unit in _militaryPositions)
            {
                if (!InsideTreatyZone(zone, unit.X, unit.Y))
                    continue;
                if (unit.Owner == _localPlayer)
                {
                    attackingUnits++;
                    attacking = SaturatingAdd(attacking, unit.Value);
                }
                else
                {
                    int unitTeam;
                    // Team0 denotes independent players, not a mutual alliance.
                    // Only actual native allies physically in this castle add
                    // to its defense; distant third-party forces count nowhere.
                    if (unit.Owner == player || targetTeam > 0 && _militaryTeams.TryGetValue(unit.Owner, out unitTeam) && unitTeam == targetTeam)
                        defending = SaturatingAdd(defending, unit.Value);
                }
            }

            long own, target;
            military.TryGetValue(_localPlayer, out own);
            military.TryGetValue(player, out target);
            band = DiplomacySubmission.PressureBand(lord, own, target, attackingUnits, attacking, defending);
            return true;
        }

        // No native inventory operation is discarded to remove old support.
        // The frame caller holds _gate; live/loaded journals finish first.
        private void NormalizeVassalDemands()
        {
            if (_inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            foreach (RelationState relation in _relations.Values)
            {
                if (relation.ExtendedState != null && DiplomacyVassalage.IsActive(relation.ExtendedState))
                    DiplomacyAgreementRules.SuspendVassalMaintenance(relation);
                WithdrawLegacyAllianceSubjugation(relation);
            }
        }

        private void WithdrawLegacyAllianceSubjugation(RelationState relation)
        {
            if (_inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null || relation == null || relation.ExtendedState == null || DiplomacyVassalage.IsActive(relation.ExtendedState) || relation.ExtendedState.PendingVassalOverlordPlayerId == 0)
                return;
            DiplomacyVassalage.ClearPendingOffer(relation.ExtendedState);
            CancelDiplomacyDialog("subjugation:" + relation.CoalitionId);
            Log("vassalage", "coalition=" + relation.CoalitionId + "|status=legacy_alliance_submission_withdrawn|reason=no_hostile_siege");
        }
    }
}
