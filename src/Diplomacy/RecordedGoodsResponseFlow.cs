using System;
using System.Linq;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // A response ID is minted only by the Mod's real goods-demand
        // confirmation. It is never attached to a foreign/native request by
        // comparing target, good and amount. Selection and quote are bound.
        private bool TryCaptureRecordedDemandResponse(PendingGoodsCommand command)
        {
            if (command == null || command.Demand == null || command.DemandQuote == null ||
                command.RecordedRequestIds == null || command.RecordedResponsePrepared ||
                !Monitor.IsEntered(_gate) || !RequestHistoryOwnerReady() ||
                command.Generation != _sessionGeneration ||
                !String.Equals(command.Fingerprint, _mapFingerprint, StringComparison.Ordinal) ||
                !String.Equals(command.RosterFingerprint, _rosterFingerprint, StringComparison.Ordinal) ||
                command.LocalPlayer != _localPlayer || command.TargetPlayer == _localPlayer ||
                !_roster.Contains(command.TargetPlayer) || command.DemandQuote.Complete ||
                command.DemandQuote.IssuedDay > _lastProcessedDay ||
                _lastProcessedDay >= command.DemandQuote.ExpiresDay ||
                command.DemandQuote.Items == null ||
                command.RecordedRequestIds.Length != command.DemandQuote.Items.Length ||
                !SameGoodsDemandQuote(command.Demand, command.DemandQuote) ||
                command.Lifetime.Cancelled)
                return false;

            int counted = 0;
            foreach (DemandItem item in command.DemandQuote.Items)
                if (item != null && item.RemainingAmount > 0)
                    ++counted;
            if (counted == 0 || counted > 3)
                return false;

            for (int i = 0; i < command.DemandQuote.Items.Length; ++i)
            {
                DemandItem item = command.DemandQuote.Items[i];
                if (item != null && item.RemainingAmount == 0)
                    continue;
                if (item == null || item.GoodId < 1 || item.GoodId > 24 ||
                    item.RemainingAmount < 1 || item.RemainingAmount > 1000000)
                {
                    CancelUnsentRecordedDemandResponse(command);
                    return false;
                }

                ulong id;
                if (!TryCaptureDiplomaticRequest(DiplomaticRequestKind.Goods,
                        DiplomaticRequestOrigin.ModAuthored, command.TargetPlayer,
                        command.LocalPlayer, 0, item.GoodId, item.RemainingAmount, 0,
                        out id))
                {
                    CancelUnsentRecordedDemandResponse(command);
                    return false;
                }

                command.RecordedRequestIds[i] = id;
                if (!TryDecideDiplomaticRequest(id, true, DiplomaticRequestDecisionEvidence.ModPolicy))
                {
                    CancelUnsentRecordedDemandResponse(command);
                    return false;
                }
            }

            command.RecordedResponsePrepared = true;
            return true;
        }

        private void CancelUnsentRecordedDemandResponse(PendingGoodsCommand command)
        {
            if (command == null || command.RecordedRequestIds == null ||
                !Monitor.IsEntered(_gate) || !RequestHistoryOwnerReady() ||
                command.Generation != _sessionGeneration ||
                command.LocalPlayer != _localPlayer ||
                !String.Equals(command.Fingerprint, _mapFingerprint, StringComparison.Ordinal) ||
                !String.Equals(command.RosterFingerprint, _rosterFingerprint, StringComparison.Ordinal) ||
                _requestHistory == null)
                return;

            foreach (ulong id in command.RecordedRequestIds)
            {
                if (id == 0)
                    continue;
                DiplomaticRequestRecord record = _requestHistory.Find(id);
                if (record != null && !record.Terminal &&
                    record.Origin == DiplomaticRequestOrigin.ModAuthored &&
                    record.Kind == DiplomaticRequestKind.Goods)
                    TryNoteDiplomaticRequestCancelled(id, false);
            }
        }

        // Called only after the exact queued UI quote is revalidated and
        // claimed, before starting any Mod-owned inventory transaction.
        // Forwarded here means Mod submission, never OriginalSubmitted/ACK.
        private bool TryForwardRecordedDemandResponse(PendingGoodsCommand command)
        {
            if (command == null || !command.RecordedResponsePrepared ||
                command.RecordedRequestIds == null || command.DemandQuote == null ||
                !Monitor.IsEntered(_gate) || !RequestHistoryOwnerReady() ||
                command.Generation != _sessionGeneration ||
                command.LocalPlayer != _localPlayer ||
                !String.Equals(command.Fingerprint, _mapFingerprint, StringComparison.Ordinal) ||
                !String.Equals(command.RosterFingerprint, _rosterFingerprint, StringComparison.Ordinal) ||
                _requestHistory == null || !SameGoodsDemandQuote(command.Demand, command.DemandQuote) ||
                _lastProcessedDay >= command.DemandQuote.ExpiresDay ||
                command.RecordedRequestIds.Length != command.DemandQuote.Items.Length)
                return false;

            for (int i = 0; i < command.RecordedRequestIds.Length; ++i)
            {
                DemandItem quoted = command.DemandQuote.Items[i];
                if (quoted != null && quoted.RemainingAmount == 0 &&
                    command.RecordedRequestIds[i] == 0)
                    continue;
                DiplomaticRequestRecord record = _requestHistory.Find(command.RecordedRequestIds[i]);
                if (record == null || quoted == null || record.Id == 0 ||
                    record.Origin != DiplomaticRequestOrigin.ModAuthored ||
                    record.Kind != DiplomaticRequestKind.Goods ||
                    record.Decision != DiplomaticRequestDecision.Allowed ||
                    record.DecisionEvidence != DiplomaticRequestDecisionEvidence.ModPolicy ||
                    record.Status != DiplomaticRequestStatus.Approved ||
                    record.Requester != command.TargetPlayer ||
                    record.Counterparty != command.LocalPlayer ||
                    record.Subject != 0 || record.Opcode != 0 ||
                    record.GoodId != quoted.GoodId ||
                    record.Quantity != quoted.RemainingAmount)
                    return false;
            }

            foreach (ulong id in command.RecordedRequestIds)
                if (id != 0 && !TryNoteDiplomaticRequestForwarded(id))
                {
                    // No native attempt was begun: this is a Mod UI abort.
                    CancelUnsentRecordedDemandResponse(command);
                    return false;
                }
            return true;
        }

        private bool HasExactRecordedDemandLine(PendingGoodsCommand command,
            int index, DemandItem item)
        {
            if (command == null || item == null || command.RecordedRequestIds == null ||
                index < 0 || index >= command.RecordedRequestIds.Length ||
                _requestHistory == null || !RequestHistoryOwnerReady())
                return false;
            DiplomaticRequestRecord record = _requestHistory.Find(command.RecordedRequestIds[index]);
            return record != null && record.Id != 0 &&
                record.Origin == DiplomaticRequestOrigin.ModAuthored &&
                record.Kind == DiplomaticRequestKind.Goods &&
                record.Requester == command.TargetPlayer &&
                record.Counterparty == command.LocalPlayer &&
                record.GoodId == item.GoodId &&
                record.Quantity == item.RemainingAmount &&
                record.Status == DiplomaticRequestStatus.Forwarded &&
                record.Decision == DiplomaticRequestDecision.Allowed &&
                record.DecisionEvidence == DiplomaticRequestDecisionEvidence.ModPolicy;
        }
    }
}
