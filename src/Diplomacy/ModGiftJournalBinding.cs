using System;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    // Schema21 continuation of the EXISTING native gift journal, not a new ledger.
    // Pure class: usable in offline CLR tests with no engine/native/SDK calls.
    internal static class ModGiftJournalBinding
    {
        internal static void Write(BinaryWriter writer, LegacyDiplomacySave state)
        {
            writer.Write(state.RequestHistory == null ? 1UL : state.RequestHistory.NextModGiftReceiptId);
            writer.Write(state.PendingTransfer == null ? 0UL : state.PendingTransfer.BoundRequestId);
            writer.Write(state.PendingTransfer == null ? 0UL : state.PendingTransfer.ModGiftReceiptId);
        }

        internal static void Read(BinaryReader reader, LegacyDiplomacySave state)
        {
            if (state.RequestHistory == null)
                throw new InvalidDataException("Schema21 requires request history ownership");
            state.RequestHistory.NextModGiftReceiptId = reader.ReadUInt64();
            ulong requestId = reader.ReadUInt64();
            ulong receiptId = reader.ReadUInt64();
            if (state.PendingTransfer == null)
            {
                if (requestId != 0 || receiptId != 0)
                    throw new InvalidDataException("Orphaned bound Mod gift journal");
            }
            else
            {
                state.PendingTransfer.BoundRequestId = requestId;
                state.PendingTransfer.ModGiftReceiptId = receiptId;
            }
            Validate(state);
        }

        internal static void MigratePre21(LegacyDiplomacySave state)
        {
            if (state.RequestHistory == null)
                throw new InvalidDataException("Legacy request history scope not available");
            // Old receipt identities are unknown and MUST NEVER bind a transfer.
            // Avoid serial reuse even if historical synthetic IDs were present.
            ulong oldHigh = state.RequestHistory.HighestGoodsReceiptId;
            state.RequestHistory.NextModGiftReceiptId = oldHigh == ulong.MaxValue ?
                ulong.MaxValue : oldHigh + 1;
            if (state.PendingTransfer != null)
            {
                state.PendingTransfer.BoundRequestId = 0;
                state.PendingTransfer.ModGiftReceiptId = 0;
            }
        }

        internal static void Validate(LegacyDiplomacySave state)
        {
            if (state == null)
                throw new InvalidDataException("Missing Mod-gift owner scope");
            DiplomaticRequestHistory history = state.RequestHistory;
            if (history != null && (history.NextModGiftReceiptId == 0 ||
                (history.NextModGiftReceiptId <= history.HighestGoodsReceiptId &&
                 !(history.NextModGiftReceiptId == ulong.MaxValue &&
                   history.HighestGoodsReceiptId == ulong.MaxValue))))
                throw new InvalidDataException("Invalid Mod-gift owner serial high-water");

            // One existing package may carry several distinct Mod response
            // receipts. Each line belongs to that SAME native package journal;
            // no package-wide synthetic receipt, parallel ledger or barter ID.
            DiplomacyPendingPackage package = state.PendingPackage;
            if (package != null)
            {
                if (package.Lines == null || package.Lines.Count < 1 ||
                    package.Lines.Any(line => line == null))
                    throw new InvalidDataException("Invalid Mod gift package line list");
                bool anyBound = package.Lines.Any(line =>
                    line.BoundRequestId != 0 || line.ModGiftReceiptId != 0);
                if (anyBound)
                {
                    if (package.TradeRecordId != 0 || state.PendingTransfer != null ||
                        history == null || package.SenderPlayer != state.LocalPlayerId)
                        throw new InvalidDataException("Mod-gift response cannot borrow barter or separate transfer ownership");
                    var requestIds = new System.Collections.Generic.HashSet<ulong>();
                    ulong priorReceipt = 0;
                    foreach (DiplomacyPendingTransfer line in package.Lines)
                    {
                        if (line.BoundRequestId == 0 || line.ModGiftReceiptId == 0 ||
                            !requestIds.Add(line.BoundRequestId) ||
                            line.ModGiftReceiptId <= priorReceipt ||
                            line.ModGiftReceiptId >= history.NextModGiftReceiptId ||
                            line.SenderPlayer != package.SenderPlayer ||
                            line.TargetPlayer != package.TargetPlayer ||
                            line.Purpose != DiplomacyTransferPurpose.Gift ||
                            !line.DirectAtomicTransfer || line.GameDay != package.GameDay)
                            throw new InvalidDataException("Response package line has no independent journal owner");
                        priorReceipt = line.ModGiftReceiptId;
                        if (package.RollbackPending || line.RollbackPending)
                            continue; // Undo-only, no grant, even if the request was evicted.
                        if (line.ModGiftReceiptId <= history.HighestGoodsReceiptId)
                            throw new InvalidDataException("Replayed response-package receipt");
                        DiplomaticRequestRecord request = history.Find(line.BoundRequestId);
                        if (request == null || request.Kind != DiplomaticRequestKind.Goods ||
                            request.Origin != DiplomaticRequestOrigin.ModAuthored ||
                            request.Counterparty != line.SenderPlayer ||
                            request.Requester != line.TargetPlayer ||
                            request.GoodId != line.GoodId || request.Quantity != line.Amount ||
                            request.Decision != DiplomaticRequestDecision.Allowed ||
                            (request.Status != DiplomaticRequestStatus.Forwarded &&
                             request.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                            line.GameDay < request.UpdatedDay ||
                            request.Deliveries.Count >= DiplomaticRequestHistory.MaximumDeliveriesPerRequest)
                            throw new InvalidDataException("Mod response package has no eligible request");
                    }
                }
            }

            DiplomacyPendingTransfer t = state.PendingTransfer;
            if (t == null)
                return;
            if ((t.BoundRequestId == 0) != (t.ModGiftReceiptId == 0))
                throw new InvalidDataException("Mod-gift journal binding is incomplete");
            if (t.BoundRequestId == 0)
                return; // vanilla/native, tribute, legacy and unbound gifts stay unknown.

            if (history == null || t.Purpose != DiplomacyTransferPurpose.Gift ||
                !t.DirectAtomicTransfer || t.SenderPlayer != state.LocalPlayerId ||
                t.ModGiftReceiptId >= history.NextModGiftReceiptId ||
                t.ModGiftReceiptId <= history.HighestGoodsReceiptId)
                throw new InvalidDataException("Unowned or replayed Mod-gift journal receipt");

            DiplomaticRequestRecord r = history.Find(t.BoundRequestId);
            // Rollback-only journals may outlive terminal eviction. They remain
            // unknown/undo-only and cannot be fulfilled on cold restore.
            if (r == null)
            {
                if (!t.RollbackPending)
                    throw new InvalidDataException("Live Mod-gift journal lacks its request");
                return;
            }
            if (r.Kind != DiplomaticRequestKind.Goods ||
                r.Counterparty != t.SenderPlayer || r.Requester != t.TargetPlayer ||
                r.GoodId != t.GoodId || t.GameDay < r.IssuedDay)
                throw new InvalidDataException("Mod-gift journal request identity mismatch");
            if (!t.RollbackPending &&
                ((r.Status != DiplomaticRequestStatus.Forwarded &&
                  r.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                 r.Decision != DiplomaticRequestDecision.Allowed ||
                 t.GameDay < r.UpdatedDay ||
                 t.Amount > r.Quantity - r.DeliveredQuantity ||
                 r.Deliveries.Count >= DiplomaticRequestHistory.MaximumDeliveriesPerRequest))
                throw new InvalidDataException("Live Mod-gift journal request is not eligible");
        }
    }
}
