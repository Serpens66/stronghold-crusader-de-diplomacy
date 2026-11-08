// RECIPIENT relief goal is an explicit, bounded Mod food policy.
// These are not original AI pending purchase orders, consumption,
// production or physical stock values. Original Q and exact native
// warehouse capacity are supplied by / and never changed here.
// The DONOR's protected reserve remains 's unchanged
// SafetyReserveIgnoringForecast(source.Snapshot).
using System;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class RecipientFoodReliefTargetPolicy
    {
        // Independent MOD policy: retain a modest target of 55 units per
        // allowed original FOOD commodity (10..13) when warehouse permits.
        // This is a welfare TARGET, not native PendingPurchases or population
        // consumption evidence. Revision must be deliberate and reviewed.
        internal const int FoodReliefMinimumStockPerGood = 55;
        internal const string PolicyId = "MOD_FOOD_RELIEF_TARGET_55_PER_GOOD";
        // Observation includes genuine Q, exact C, and the already observed
        //  Buildings reservation from original AI pending purchases.
        // Preserve that baseline even if it exceeds the new Mod goal.
        // No native inventory API, mutation, timer, forecast or authority.
        internal static int RecipientReliefGoal(ushort originalGood, ObservedAidCommodity originalObservedRecipient)
        {
            if (originalObservedRecipient == null || originalObservedRecipient.Snapshot == null || originalObservedRecipient.Good != originalGood || GoodCatalog.Find(originalGood) == null || !originalObservedRecipient.ExactCapacityAttested || originalObservedRecipient.PendingPurchases < 0)
                throw new ArgumentException("Unverified original recipient stock for mod aid goal");
            StockCell observed = originalObservedRecipient.Snapshot;
            observed.Validate();
            // Preserve old nonfood receiver behavior, and any stronger
            // already-protected receiver needs from genuinely observed
            // Buildings reservations. Never spend forecast production.
            StockCell baseline = observed.Copy();
            baseline.DailyProduction = 0;
            int previousReceiverReserve = baseline.Reserve();
            if (originalGood < 10 || originalGood > 13)
                return previousReceiverReserve;
            int boundedModFoodTarget = Math.Min(observed.Capacity, FoodReliefMinimumStockPerGood);
            return Math.Max(previousReceiverReserve, boundedModFoodTarget);
        }
    }
}
