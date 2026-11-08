// checked, saturating diplomacy-trade relation updates integrated
// directly into the DIPLOMACY-ExtendedState game plugin. No hook, SaveData API or
// Native goods permission is granted by this helper.
using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    internal static class TradeRelationshipRules
    {
        internal const int MaxStreak = 5000;
        internal const int MaxDailyGoodwillLoss = 24;
        internal const int MaxAcceptedTrades = int.MaxValue - 1;
        private static bool DayValid(int value, int currentDay)
        {
            return value == -100 || value >= 1 && value <= currentDay;
        }

        internal static void ValidateBeforeEffect(TradeRelationship r, int gameDay)
        {
            if (r == null || gameDay < 1 || r.Goodwill < 0 || r.Goodwill > 100 || r.RejectionStreak < 0 || r.RejectionStreak > MaxStreak || r.RejectedToday < 0 || r.RejectedToday > MaxDailyGoodwillLoss || r.AcceptedTrades < 0 || r.AcceptedTrades >= MaxAcceptedTrades || !DayValid(r.LastRejectedDay, gameDay) || !DayValid(r.LastAttemptDay, gameDay) || !DayValid(r.LastPenaltyDay, gameDay) || !DayValid(r.LastDealDay, gameDay))
                throw new InvalidDataException("Invalid diplomatically bound trade relationship");
        }

        // Each rapid counteroffer refusal worsens the pair relationship, but
        // never beyond the 24 goodwill/day penalty budget shared with ordinary
        // refusals. This function touches no real game state except the caller's
        // single already-verified  ledger record.
        internal static int DeclineCounter(TradeRelationship r, int gameDay)
        {
            ValidateBeforeEffect(r, gameDay);
            int nextStreak = r.LastRejectedDay >= 1 && (long)gameDay - r.LastRejectedDay <= 2 ? Math.Min(MaxStreak, r.RejectionStreak + 1) : 1;
            int alreadySpent = r.LastPenaltyDay == gameDay ? r.RejectedToday : 0;
            int penalty = (int)Math.Min(14L, 2L + 4L * (nextStreak - 1));
            int actualLoss = Math.Min(penalty, Math.Max(0, MaxDailyGoodwillLoss - alreadySpent));
            // The validation and arithmetic above must succeed BEFORE any
            // of the following assignments.
            r.Goodwill = Math.Max(0, r.Goodwill - actualLoss);
            r.RejectionStreak = nextStreak;
            r.LastRejectedDay = gameDay;
            r.LastAttemptDay = gameDay;
            r.LastPenaltyDay = gameDay;
            r.RejectedToday = alreadySpent + actualLoss;
            return actualLoss;
        }

        // This is invoked ONLY under the actual two-legged native transaction's
        // managed commit + rollback memento. Reject an overflowing trade
        // counter before changing goodwill, grudge or any other field.
        internal static int CommitSuccessfulTrade(TradeRelationship r, int gameDay)
        {
            ValidateBeforeEffect(r, gameDay);
            int before = r.Goodwill;
            int after = Math.Min(100, before + 6);
            r.Goodwill = after;
            r.RejectionStreak = 0;
            r.LastRejectedDay = -100;
            r.LastDealDay = gameDay;
            r.LastAttemptDay = gameDay;
            r.AcceptedTrades++;
            // Keep RejectedToday and LastPenaltyDay. One successful trade
            // may repair trust but may NOT reset the same-day spam penalty
            // budget by repeatedly alternating accepted/rejected offers.
            return after - before;
        }
    }
}
