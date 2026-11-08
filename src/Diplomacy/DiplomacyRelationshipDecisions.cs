using System;
using System.Linq;
using CrusaderDE;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private string DiplomacyCoalitionNames(CoalitionState coalition)
        {
            return string.Join(", ", coalition.MemberPlayerIds.OrderBy(p => p).Select(player =>
            {
                return PlayerName(player);
            }));
        }

        private sealed class RelationshipDecisionQuote
        {
            internal string CoalitionId, Fingerprint, Contract, Names;
            internal int LocalPlayer, TargetPlayer;
            internal long Generation;
            internal DiplomacyRelationshipDecisionKind Kind;
            internal string Key
            {
                get
                {
                    return Kind == DiplomacyRelationshipDecisionKind.AcceptAllianceOffer ? "alliance_offer:" + CoalitionId : "relationship:" + CoalitionId + ":" + Kind + ":" + Contract;
                }
            }
        }

        private RelationshipDecisionQuote CaptureRelationshipDecision(string id, int player, DiplomacyRelationshipDecisionKind kind)
        {
            CoalitionState coalition;
            RelationState relation;
            if (!_mapActive || _mapDisabled || !_coalitions.TryGetValue(id, out coalition) || !_relations.TryGetValue(id, out relation) || DiplomacyRelationshipDecisions.HasPending(relation.ExtendedState) || !DiplomacyRelationshipDecisions.Eligible(relation, kind, Math.Max(0, _lastProcessedDay)))
                return null;
            string stamp = DiplomacyRelationshipDecisions.Contract(relation, coalition, _localPlayer, player, kind);
            if (stamp == null)
                return null;
            return new RelationshipDecisionQuote
            {
                CoalitionId = id,
                TargetPlayer = player,
                LocalPlayer = _localPlayer,
                Generation = _sessionGeneration,
                Fingerprint = _mapFingerprint,
                Contract = stamp,
                Kind = kind,
                Names = DiplomacyCoalitionNames(coalition)
            };
        }

        private bool IsCurrentRelationshipDecision(RelationshipDecisionQuote quote)
        {
            CoalitionState coalition;
            RelationState relation;
            return quote != null && _mapActive && !_mapDisabled && !_mapBootstrapPending && quote.Generation == _sessionGeneration && quote.LocalPlayer == _localPlayer && string.Equals(quote.Fingerprint, _mapFingerprint, StringComparison.Ordinal) && _coalitions.TryGetValue(quote.CoalitionId, out coalition) && _relations.TryGetValue(quote.CoalitionId, out relation) && !DiplomacyRelationshipDecisions.HasPending(relation.ExtendedState) && IsAlivePlayer(quote.LocalPlayer) && IsAlivePlayer(quote.TargetPlayer) && (quote.Kind != DiplomacyRelationshipDecisionKind.AcceptAllianceOffer || CanJoinAlliance(quote.CoalitionId)) && DiplomacyRelationshipDecisions.Eligible(relation, quote.Kind, Math.Max(0, _lastProcessedDay)) && string.Equals(quote.Contract, DiplomacyRelationshipDecisions.Contract(relation, coalition, _localPlayer, quote.TargetPlayer, quote.Kind), StringComparison.Ordinal);
        }

        private void StageRelationshipDecision(RelationshipDecisionQuote quote, bool accepted = true)
        {
            lock (_gate)
            {
                if (!IsCurrentRelationshipDecision(quote))
                    return;
                var kind = quote.Kind;
                if (kind == DiplomacyRelationshipDecisionKind.AcceptAllianceOffer && !accepted)
                    kind = DiplomacyRelationshipDecisionKind.RejectAllianceOffer;
                RelationState relation = _relations[quote.CoalitionId];
                if (!DiplomacyRelationshipDecisions.Stage(relation, _coalitions[quote.CoalitionId], _localPlayer, quote.TargetPlayer, Math.Max(0, _lastProcessedDay), kind, quote.Contract))
                    return;
                // A later rollback-only rescue save must retain an answer made
                // while its older inventory base was waiting for completion.
                CopyRelationshipDecisionToInventoryBase(relation);
            }
        }

        private void CopyRelationshipDecisionToInventoryBase(RelationState live)
        {
            if (_inventoryRecoveryBaseState == null)
                return;
            RelationState before = _inventoryRecoveryBaseState.Relations.FirstOrDefault(r => r.CoalitionId == live.CoalitionId);
            if (before == null || before.ExtendedState == null)
                return;
            DiplomacyRelationshipDecisions.CopyPendingDecision(live.ExtendedState, before.ExtendedState);
        }

        private void PumpPendingRelationshipDecisions()
        {
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            foreach (string id in _relations.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray())
            {
                RelationState relation = _relations[id];
                CoalitionState coalition = _coalitions[id];
                if (!DiplomacyRelationshipDecisions.HasPending(relation.ExtendedState))
                    continue;
                if (!DiplomacyRelationshipDecisions.Matches(relation, coalition, _localPlayer, Math.Max(0, _lastProcessedDay)))
                {
                    DiplomacyRelationshipDecisions.Clear(relation.ExtendedState);
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
                        int player = relation.ExtendedState.PendingRelationshipDecisionPlayerId;
                        if (!IsAlivePlayer(_localPlayer) || !IsAlivePlayer(player))
                        {
                            DiplomacyRelationshipDecisions.Clear(relation.ExtendedState);
                            continue;
                        }

                        var kind = relation.ExtendedState.PendingRelationshipDecision;
                        before = CaptureManagedStateMemento();
                        DiplomacyRelationshipDecisions.Clear(relation.ExtendedState);
                        switch (kind)
                        {
                            case DiplomacyRelationshipDecisionKind.EndAlliance:
                                PlayerBreakOne(id);
                                if (_pendingTeamTransition == null || _pendingTeamTransition.CoalitionId != id || _pendingTeamTransition.Kind != DiplomacyTeamTransitionKind.PlayerBreakOne)
                                    throw new InvalidOperationException("Alliance end was not journaled");
                                break;
                            case DiplomacyRelationshipDecisionKind.EndAgreement:
                                ExecuteBoundAgreementEnd(coalition, relation);
                                break;
                            case DiplomacyRelationshipDecisionKind.AcceptAllianceOffer:
                            case DiplomacyRelationshipDecisionKind.RejectAllianceOffer:
                                ResolveSpontaneousOffer(id, _sessionGeneration, _mapFingerprint, kind == DiplomacyRelationshipDecisionKind.AcceptAllianceOffer);
                                if (relation.PendingOffer && _pendingTeamTransition == null)
                                    throw new InvalidOperationException("Alliance answer was not committed");
                                break;
                        }
                    }
                    catch (Exception exception)
                    {
                        if (before != null)
                            RestoreManagedStateMemento(before);
                        try
                        {
                            Log("relationship_decision", "status=deferred|reason=" + Safe(exception.GetType().Name));
                        }
                        catch
                        {
                        }
                    }
                    finally
                    {
                        PublishCombatProtectionSnapshot();
                    }
                }

                if (_pendingTeamTransition != null)
                    return;
            }
        }

        private void ExecuteBoundAgreementEnd(CoalitionState coalition, RelationState relation)
        {
            DiplomacyAgreementRules.BreakAgreementByPlayer(relation, Math.Max(0, _lastProcessedDay), coalition.Target, _rules);
            ShowMessage(DiplomacyCoalitionNames(coalition) + " · " + Text("Das Abkommen wurde aufgekündigt. Vertrauen und Ansehen sind beschädigt.", "The agreement was ended. Trust and standing have suffered."));
            Log("agreement", "coalition=" + coalition.CoalitionId + "|status=broken|actor=player");
        }
    }
}
