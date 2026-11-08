// deterministic, read-only account of canonical trade receipts.
// Does not turn a planned, PREPARING or quarantined transaction into a success.
// It reads the existing schema14 archive; no schema migration or new hook.
using System;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class DiplomacyChronicle
    {
        // This is invoked only while the vanilla-integrated trade view is open.
        // Keep a bounded O(n) scan and never query native game state.
        internal static string Latest(TradeHistoryArchive ledger, int selectedPlayer, int day, bool german, Func<int, string> nameOf)
        {
            if (day < 1 || selectedPlayer < 1 || selectedPlayer > 8 || nameOf == null)
                return string.Empty;
            if (ledger == null || ledger.Records == null)
                return string.Empty;
            TradeRecord best = null;
            foreach (TradeRecord rec in ledger.Records)
            {
                if (rec == null || rec.IssuedDay > day || (long)day - rec.IssuedDay > 14L || (rec.Requester != selectedPlayer && rec.Recipient != selectedPlayer))
                    continue;
                // Superseded is only a parent receipt; PreparingNative is a
                // journal intermediate, NOT a successful trade.
                if (rec.State == TradeDisposition.Superseded || rec.State == TradeDisposition.PreparingNative)
                    continue;
                if (best == null || rec.IssuedDay > best.IssuedDay || (rec.IssuedDay == best.IssuedDay && rec.Id > best.Id))
                    best = rec;
            }

            if (best == null)
                return german ? "Kein jüngerer Handelbucheintrag mit diesem Fürsten." : "No recent ledger entry with this lord.";
            string other;
            try
            {
                other = nameOf(best.Requester == selectedPlayer ? best.Recipient : best.Requester);
            }
            catch
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(other))
                other = german ? "anderer Fürst" : "other lord";
            // Never expose untrusted multi-line ruler names in a single UI label.
            other = other.Trim().Replace("\r", " ").Replace("\n", " ");
            if (other.Length > 26)
                other = other.Substring(0, 26);
            string goods = GoodCatalog.Find(best.OfferedGood) != null && GoodCatalog.Find(best.RequestedGood) != null ? best.OfferedAmount + " " + GoodCatalog.Find(best.OfferedGood).Name + " / " + best.RequestedAmount + " " + GoodCatalog.Find(best.RequestedGood).Name : "";
            if (best.State == TradeDisposition.Committed)
                return german ? "Abgeschlossen: " + other + " – " + goods : "Completed with " + other + ": " + goods;
            if (best.State == TradeDisposition.PendingCounter)
            {
                if (best.ExpiryDay < day)
                    return german ? "Gegenangebot abgelaufen." : "Counteroffer expired.";
                return german ? "Gegenangebot offen: " + other + " – " + goods : "Counteroffer pending: " + other + " – " + goods;
            }

            if (best.State == TradeDisposition.Quarantined)
                return german ? "Handel gesperrt: unsicherer Buchungsstand." : "Trade quarantined: unverified settlement.";
            if (best.State == TradeDisposition.Rejected)
                return german ? "Letzte Anfrage von " + other + " abgelehnt." : "Recent proposal with " + other + " rejected.";
            if (best.State == TradeDisposition.Cancelled)
                return german ? "Letzte Verhandlung mit " + other + " beendet." : "Negotiation with " + other + " cancelled.";
            return string.Empty;
        }
    }
}
