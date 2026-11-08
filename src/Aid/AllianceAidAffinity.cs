// A verified native alliance has the same aid affinity before and after
// lazy allocation of an otherwise untouched neutral trade relationship.
// Real trade history always keeps its actual recorded goodwill. This projection
// neither edits that history nor awards a successful trade or an earned favor.
#if DIPLOMACY_PRIVATE_TESTS
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal static class AllianceAidAffinity
    {
        private const int NativeMutualAllianceAffinity = 65;
        private static readonly TradeRelationship UnusedTradeTerms = new TradeRelationship();

        private static bool IsUnusedNeutralRelation(TradeRelationship relation)
        {
            return relation.Goodwill == UnusedTradeTerms.Goodwill &&
                relation.RejectionStreak == UnusedTradeTerms.RejectionStreak &&
                relation.LastRejectedDay == UnusedTradeTerms.LastRejectedDay &&
                relation.LastAttemptDay == UnusedTradeTerms.LastAttemptDay &&
                relation.LastPenaltyDay == UnusedTradeTerms.LastPenaltyDay &&
                relation.RejectedToday == UnusedTradeTerms.RejectedToday &&
                relation.AcceptedTrades == UnusedTradeTerms.AcceptedTrades &&
                relation.LastDealDay == UnusedTradeTerms.LastDealDay;
        }

        // The outer runtime validates this archive/mailbox against the current
        // native map and roster. Reading the complete pair context here avoids
        // mistaking a lazy default relation for the absence of real activity.
        internal static bool TryResolve(TradeHistoryArchive history,
            TradeIntentMailbox mailbox, byte a, byte b,
            bool reciprocalAllianceConfirmed, out int affinity,
            out bool recordedTradeGoodwillUsed)
        {
            affinity = 0;
            recordedTradeGoodwillUsed = false;
            if (!reciprocalAllianceConfirmed || history == null || mailbox == null)
                return false;
            int pair = TradeHistoryArchive.PairKey(a, b);
            TradeRelationship relation = history.Relationships.FirstOrDefault(x =>
                TradeHistoryArchive.PairKey(x.A, x.B) == pair);
            bool recordedPairActivity = history.Records.Any(x =>
                TradeHistoryArchive.PairKey(x.Requester, x.Recipient) == pair) ||
                mailbox.Intents.Any(x =>
                    TradeHistoryArchive.PairKey(x.Requester, x.Recipient) == pair);
            // Checked archive receipts and retained attempts can precede a
            // relationship row. Missing terms never manufacture goodwill.
            if (relation == null && recordedPairActivity)
                return false;
            if (!recordedPairActivity &&
                (relation == null || IsUnusedNeutralRelation(relation)))
            {
                affinity = NativeMutualAllianceAffinity;
                return true;
            }
            affinity = relation.Goodwill;
            recordedTradeGoodwillUsed = true;
            return true;
        }

    }
}
#endif
