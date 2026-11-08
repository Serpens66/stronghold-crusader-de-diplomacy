// exact four-cell native barter precommit for the existing
// DIPLOMACY ExtendedState game integration; builds against genuine rawra SE2.14.1.
// This code checks original quantities, all four verified storage ceilings,
// two survival reserves, player/good identity and full int overflow BEFORE
// the first native write; all checks are performed inside the game's
// existing exclusive inventory section. No cap is ever guessed here.
// The IOriginalWarehouseCapacityAttestation provider is intentionally MISSING in this game
// version. Its hard-deny is NOT a release/Serps compatibility bypass.
using System;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class FourCellSnapshot
    {
        internal int Requester, Recipient;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount;
        internal int RequesterOfferedBefore, RecipientOfferedBefore;
        internal int RecipientRequestedBefore, RequesterRequestedBefore;
        internal int RequesterOfferedReserve, RecipientRequestedReserve;
        internal int RequesterOfferedCapacity, RecipientOfferedCapacity;
        internal int RecipientRequestedCapacity, RequesterRequestedCapacity;
        internal FourCellSnapshot Clone()
        {
            return (FourCellSnapshot)MemberwiseClone();
        }
    }

    internal static class FourCellPrecommit
    {
        // These fields cover exactly four DISTINCT native inventory cells:
        // (requester,offered), (recipient,offered),
        // (recipient,requested), (requester,requested).
        // One transfer affects two cells; the two different goods do not
        // share one field even when the players are reversed.
        private static bool ValidPlayer(int slot)
        {
            return slot >= 1 && slot <= 8;
        }

        private static bool ValidGoods(ushort id)
        {
            return GoodCatalog.Find(id) != null;
        }

        internal static bool Validate(FourCellSnapshot s, out string reason)
        {
            reason = "invalid_barter_session_or_terms";
            if (s == null || !ValidPlayer(s.Requester) || !ValidPlayer(s.Recipient) || s.Requester == s.Recipient || !ValidGoods(s.OfferedGood) || !ValidGoods(s.RequestedGood) || s.OfferedGood == s.RequestedGood || s.OfferedAmount < 1 || s.RequestedAmount < 1 || s.OfferedAmount > 100000 || s.RequestedAmount > 100000)
                return false;
            reason = "missing_or_impossible_four_cell_amounts";
            if (s.RequesterOfferedBefore < 0 || s.RecipientOfferedBefore < 0 || s.RecipientRequestedBefore < 0 || s.RequesterRequestedBefore < 0 || s.RequesterOfferedReserve < 0 || s.RecipientRequestedReserve < 0 || s.RequesterOfferedCapacity < 1 || s.RecipientOfferedCapacity < 1 || s.RecipientRequestedCapacity < 1 || s.RequesterRequestedCapacity < 1)
                return false;
            // Reject exceptional massive 'capacity' placeholders as untrusted,
            // matching the existing  Max<=(Int32.MaxValue-200000) invariant.
            reason = "native_capacity_exceeds_attested_integer_range";
            if (s.RequesterOfferedCapacity > int.MaxValue - 200000 || s.RecipientOfferedCapacity > int.MaxValue - 200000 || s.RecipientRequestedCapacity > int.MaxValue - 200000 || s.RequesterRequestedCapacity > int.MaxValue - 200000)
                return false;
            reason = "native_inventory_exceeds_original_capacity";
            if (s.RequesterOfferedBefore > s.RequesterOfferedCapacity || s.RecipientOfferedBefore > s.RecipientOfferedCapacity || s.RecipientRequestedBefore > s.RecipientRequestedCapacity || s.RequesterRequestedBefore > s.RequesterRequestedCapacity)
                return false;
            reason = "source_safety_reserve_or_goods_insufficient";
            long requesterSourceAfter = (long)s.RequesterOfferedBefore - s.OfferedAmount;
            long recipientSourceAfter = (long)s.RecipientRequestedBefore - s.RequestedAmount;
            if (requesterSourceAfter < s.RequesterOfferedReserve || recipientSourceAfter < s.RecipientRequestedReserve)
                return false;
            reason = "receiver_capacity_changed_before_native_commit";
            long recipientOfferedAfter = (long)s.RecipientOfferedBefore + s.OfferedAmount;
            long requesterRequestedAfter = (long)s.RequesterRequestedBefore + s.RequestedAmount;
            if (recipientOfferedAfter > s.RecipientOfferedCapacity || requesterRequestedAfter > s.RequesterRequestedCapacity || recipientOfferedAfter > int.MaxValue || requesterRequestedAfter > int.MaxValue)
                return false;
            // Every leg must conserve native goods exactly.
            if (requesterSourceAfter + recipientOfferedAfter != (long)s.RequesterOfferedBefore + s.RecipientOfferedBefore || recipientSourceAfter + requesterRequestedAfter != (long)s.RecipientRequestedBefore + s.RequesterRequestedBefore)
            {
                reason = "conservation_invariant_failed";
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool TryAuthorizeWithCapacity(FourCellSnapshot untrustedBefore, OriginalInventoryEvidence.IOriginalWarehouseCapacityAttestation attestedOriginalNativeCapacity, out FourCellSnapshot exactReceipt, out string reason)
        {
            exactReceipt = null;
            reason = "no_official_native_warehouse_capacity_authority";
            if (untrustedBefore == null || attestedOriginalNativeCapacity == null)
                return false;
            FourCellSnapshot candidate = untrustedBefore.Clone();
            // Ignore ANY external/UI-supplied capacity in this input struct.
            candidate.RequesterOfferedCapacity = 0;
            candidate.RecipientOfferedCapacity = 0;
            candidate.RecipientRequestedCapacity = 0;
            candidate.RequesterRequestedCapacity = 0;
            if (!ValidPlayer(candidate.Requester) || !ValidPlayer(candidate.Recipient) || candidate.Requester == candidate.Recipient || !ValidGoods(candidate.OfferedGood) || !ValidGoods(candidate.RequestedGood) || candidate.OfferedGood == candidate.RequestedGood || candidate.OfferedAmount < 1 || candidate.OfferedAmount > 100000 || candidate.RequestedAmount < 1 || candidate.RequestedAmount > 100000)
            {
                reason = "invalid_barter_terms";
                return false;
            }

            try
            {
                if (!attestedOriginalNativeCapacity.TryReadWarehouseCapacity(candidate.Requester, candidate.OfferedGood, out candidate.RequesterOfferedCapacity) || !attestedOriginalNativeCapacity.TryReadWarehouseCapacity(candidate.Recipient, candidate.OfferedGood, out candidate.RecipientOfferedCapacity) || !attestedOriginalNativeCapacity.TryReadWarehouseCapacity(candidate.Recipient, candidate.RequestedGood, out candidate.RecipientRequestedCapacity) || !attestedOriginalNativeCapacity.TryReadWarehouseCapacity(candidate.Requester, candidate.RequestedGood, out candidate.RequesterRequestedCapacity))
                {
                    reason = "unattested_four_cell_capacity";
                    return false;
                }
            }
            catch (Exception)
            {
                reason = "attested_capacity_provider_exception";
                return false;
            }

            if (!Validate(candidate, out reason))
                return false;
            exactReceipt = candidate;
            return true;
        }
    }
}
