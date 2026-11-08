// live, per-faction AI inventory commitments read from SHCDE-SE's
// original pending-purchase ledger. Read-only; never predicts fabricated
// production, never edits pending purchases, native stock or save data.
using System;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // Must be called while holding the original engine inventory monitor.
        // A missing source means that the quote is NOT safely priceable.
        private int ReadCommittedAiDemand(int owner, ushort good)
        {
            RequireNativeDiplomacySection();
            if (owner < 1 || owner > 8 || GoodCatalog.Find(good) == null)
                throw new InvalidOperationException("Invalid observed AI demand identity");
            var players = GamePlayerManagerAPI.Instance;
            var ai = GameAIManagerAPI.Instance;
            if (players == null || ai == null || !players.IsPlayerIdValid(owner))
                throw new InvalidOperationException("Original AI demand API unavailable");
            if (!players.IsAIPlayer(owner))
                return 0;
            int planned = ai.GetGoodFromPendingPurchases(owner, (eGoods)good);
            if (planned < 0 || planned > 1000000)
                throw new InvalidOperationException("Original AI future demand invalid");
            return planned;
        }

        private int ReserveWithPlannedPurchases(int player, ushort good, int quantity, Ruler profile)
        {
            RequireNativeDiplomacySection();
            if (quantity < 0 || profile == null || profile.PlayerId != player)
                throw new InvalidOperationException("Invalid trade reserve context");
            int baseReserve = BoundedReserve(good, quantity, profile);
            int planned = ReadCommittedAiDemand(player, good);
            // Native demand is an observed purchase commitment, not daily
            // production or stock already owned by the AI.
            return (int)Math.Min((long)quantity, Math.Min((long)int.MaxValue, (long)baseReserve + planned));
        }

        // Return exactly the value read from the original native pending-purchase
        // ledger. The canonical frame carries it separately from the defensive
        // StockCell.Buildings reserve; never fetch a different tick's second value.
        private int AttachAiDemandToSnapshot(int player, ushort good, StockCell stock)
        {
            RequireNativeDiplomacySection();
            if (stock == null)
                throw new InvalidOperationException("No authoritative stock");
            int planned = ReadCommittedAiDemand(player, good);
            // Buildings is future commitment protection, NOT delivered inventory.
            stock.Buildings = stock.Capacity == 0 ? 0 : Math.Min(stock.Capacity, planned);
            stock.Validate();
            return planned;
        }
    }
}
