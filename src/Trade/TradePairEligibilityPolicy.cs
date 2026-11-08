//  deterministic diplomacy policy. Pure decisions only: this file has no
// native, network, inventory-write or SaveData authority.
using System;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.TradeNegotiation
{
    // The live adapter must supply an observed, current, ordered pair. A missing
    // pair is denied; global "at war" or "not allied" is not pair hostility.
    public interface IObservedTradePairProvider
    {
        bool TryGetPair(byte requester, byte recipient, int day, out ObservedTradePairState pair);
    }

    public sealed class ObservedTradePairState
    {
        public byte Requester, Recipient, Vassal, Overlord;
        public int Day, Goodwill, Trust, Grudge, Threat;
        // Authoritative means identity, life state, relation tier and explicit
        // pair hostility were all resolved by the host's existing diplomacy.
        public bool Authoritative, RequesterAlive, RecipientAlive;
        public bool Hostile, HasTradeRelation;
        public ObservedTradePairState Copy()
        {
            return (ObservedTradePairState)MemberwiseClone();
        }
    }

    public sealed class TradeAcceptanceAssessment
    {
        public bool Valid, RecipientIsVassal;
        public int ProbabilityBasisPoints;
        public string Reason;
    }

    public sealed class AutonomousTradeProposal
    {
        public byte Requester, Recipient;
        public ushort OfferedGood, RequestedGood;
        public int RequestedAmount;
        public TradeOfferGenerosity Generosity;
    }

    public sealed class RebellionAssessmentInput
    {
        public bool Authoritative, ActiveVassal, PreviouslyAtRisk;
        public int Day, VassalSinceDay, LastRebellionDay = -100;
        public int StrengthEdgeDays, PoorRelationDays;
        public int Goodwill, Trust, Grudge, Ambition, Caution;
        public long VassalPower, OverlordPower;
        // Unknown data remains unknown. Null contributes no invented supply
        // or threat signal. Measured values are normalised to 0..100.
        public int? SupplyReadiness, ExternalThreat;
    }

    public sealed class RebellionAssessmentDecision
    {
        public bool Valid, UnderPressure, ShouldRebel;
        public int ScoreBasisPoints;
        public string Reason;
    }

    public static class TradePairEligibilityRules
    {
        public const int RebellionMinimumTenureDays = 40;
        public const int RebellionSustainedDays = 5;
        public const int RebellionCooldownDays = 12;
        public const decimal RebellionEnterStrengthRatio = 1.15m;
        public const decimal RebellionHoldStrengthRatio = 1.05m;
        static bool Percent(int n)
        {
            return n >= 0 && n <= 100;
        }

        static int Bound(decimal n, int low, int high)
        {
            return (int)Math.Min(high, Math.Max(low, n));
        }

        static decimal Unit(decimal n)
        {
            return Math.Min(1m, Math.Max(0m, n));
        }

        static bool Slot(byte n)
        {
            return n >= 1 && n <= 8;
        }

        public static bool CanTrade(ObservedTradePairState pair, byte requester, byte recipient, int day, out string reason)
        {
            reason = "Keine bestätigte Handelsbeziehung";
            if (pair == null || !pair.Authoritative || day < 1 || !Slot(requester) || !Slot(recipient) || requester == recipient || pair.Requester != requester || pair.Recipient != recipient || pair.Day != day)
                return false;
            if (!pair.RequesterAlive || !pair.RecipientAlive)
            {
                reason = "Ein Handelspartner ist nicht mehr im Spiel";
                return false;
            }

            if (pair.Hostile)
            {
                reason = "Mit Feinden ist kein Handel möglich";
                return false;
            }

            if (!pair.HasTradeRelation)
                return false;
            if (!Percent(pair.Goodwill) || !Percent(pair.Trust) || !Percent(pair.Grudge) || !Percent(pair.Threat))
            {
                reason = "Ungültiger diplomatischer Zustand";
                return false;
            }

            if (!(pair.Vassal == 0 && pair.Overlord == 0) && (!Slot(pair.Vassal) || !Slot(pair.Overlord) || pair.Vassal == pair.Overlord || !((pair.Vassal == requester && pair.Overlord == recipient) || (pair.Vassal == recipient && pair.Overlord == requester))))
            {
                reason = "Ungültige Lehensbindung";
                return false;
            }

            reason = "Handelsbeziehung bestätigt";
            return true;
        }

        public static bool IsEligible(ObservedTradePairState pair, byte requester, byte recipient, int day, out string reason)
        {
            return CanTrade(pair, requester, recipient, day, out reason);
        }

        public static bool SameDecisionContext(ObservedTradePairState a, ObservedTradePairState b)
        {
            return a != null && b != null && a.Authoritative && b.Authoritative && a.Requester == b.Requester && a.Recipient == b.Recipient && a.Day == b.Day && a.RequesterAlive == b.RequesterAlive && a.RecipientAlive == b.RecipientAlive && a.Hostile == b.Hostile && a.HasTradeRelation == b.HasTradeRelation && a.Vassal == b.Vassal && a.Overlord == b.Overlord && a.Goodwill == b.Goodwill && a.Trust == b.Trust && a.Grudge == b.Grudge && a.Threat == b.Threat;
        }

        public static bool RecipientIsVassalOfRequester(ObservedTradePairState pair)
        {
            return pair != null && pair.Authoritative && pair.Vassal != 0 && pair.Vassal == pair.Recipient && pair.Overlord == pair.Requester;
        }

        // All hard reserve, capacity, price and lease conditions are still
        // enforced by BarterEngine before this probability can authorise its
        // *memory-only* transaction. Vassal obedience never changes those gates.
        public static TradeAcceptanceAssessment EvaluateAcceptance(ObservedTradePairState pair, Ruler recipient, BarterQuote quote, StockCell outgoing, StockCell incoming, decimal recipientGain, decimal requesterSaving)
        {
            var result = new TradeAcceptanceAssessment
            {
                Reason = "Unvollständige Handelsdaten"
            };
            string eligibility;
            if (quote == null || !CanTrade(pair, quote.Requester, quote.Recipient, quote.Day, out eligibility) || recipient == null || recipient.PlayerId != quote.Recipient || outgoing == null || incoming == null || !Percent(recipient.Trust) || !Percent(recipient.Greed) || !Percent(recipient.Caution) || !Percent(recipient.Commerce) || quote.RequestedAmount < 1 || quote.RequestedAmount > 100000 || quote.OfferedAmount < 1 || quote.OfferedAmount > 100000 || quote.RequestedGood == quote.OfferedGood || recipientGain <= requesterSaving || requesterSaving <= 0)
                return result;
            try
            {
                outgoing.Validate();
                incoming.Validate();
            }
            catch (InvalidOperationException)
            {
                return result;
            }

            if (outgoing.Surplus < quote.RequestedAmount || incoming.Space < quote.OfferedAmount)
            {
                result.Reason = "Reserve oder Lagerplatz verhindert den Tausch";
                return result;
            }

            decimal excess = Unit(((decimal)outgoing.Surplus - quote.RequestedAmount) / Math.Max(1m, quote.RequestedAmount * 2m));
            decimal consumptionGap = Math.Max(0m, (decimal)incoming.DailyConsumption - incoming.DailyProduction);
            decimal need = Unit((incoming.Reserve() - (decimal)incoming.Quantity + consumptionGap * 2m) / Math.Max(1m, incoming.Reserve() + consumptionGap * 2m));
            // Dividing before adding also accepts extreme valid decimal
            // prices without overflowing the combined market spread.
            decimal share = 1m / (1m + requesterSaving / recipientGain);
            decimal economicPremium = Unit((share - 0.5m) * 2m);
            decimal outgoingExposure = Unit(quote.RequestedAmount / Math.Max(1m, outgoing.Surplus));
            // Every sign has an economic interpretation: trust, relations,
            // surplus, need and premium help; grudge, threat, greed, caution,
            // military distraction and spending the last surplus hurt.
            decimal score = 4800m + (pair.Trust - 50m) * 24m + (pair.Goodwill - 50m) * 18m - pair.Grudge * 18m - pair.Threat * 10m + recipient.Commerce * 9m - recipient.Greed * 16m - recipient.Caution * 8m + excess * 1700m + need * 2200m + economicPremium * 1800m - outgoingExposure * recipient.Caution * 6m - (recipient.AtWar ? 800m : 0m);
            result.RecipientIsVassal = RecipientIsVassalOfRequester(pair);
            if (result.RecipientIsVassal)
            {
                // A proven active vassal ordinarily obeys a feasible demand
                // from its own overlord, even for the stingiest safe price.
                // Bad relations still affect the small refusal chance and the
                // separate rebellion policy, never the physical reserves.
                score = 9850m + (pair.Trust - 50m) * 2m + (pair.Goodwill - 50m) - pair.Grudge - pair.Threat / 2m + need * 60m;
                result.ProbabilityBasisPoints = Bound(score, 9700, 9950);
                result.Reason = "Der Vasall berücksichtigt seine Lehenspflicht";
            }
            else
            {
                result.ProbabilityBasisPoints = Bound(score, 400, 9600);
                result.Reason = "Wert, Bedarf, Reserven, Verhältnis und Persönlichkeit abgewogen";
            }

            result.Valid = true;
            return result;
        }

        public static RebellionAssessmentDecision EvaluateRebellion(RebellionAssessmentInput x)
        {
            var r = new RebellionAssessmentDecision
            {
                Reason = "Keine bestätigte Lehenslage"
            };
            if (x == null || !x.Authoritative || !x.ActiveVassal || x.Day < 1 || x.VassalSinceDay < 0 || x.VassalSinceDay > x.Day || (x.LastRebellionDay != -100 && (x.LastRebellionDay < 0 || x.LastRebellionDay > x.Day)) || x.StrengthEdgeDays < 0 || x.PoorRelationDays < 0 || x.VassalPower <= 0 || x.OverlordPower <= 0 || !Percent(x.Goodwill) || !Percent(x.Trust) || !Percent(x.Grudge) || !Percent(x.Ambition) || !Percent(x.Caution) || (x.SupplyReadiness.HasValue && !Percent(x.SupplyReadiness.Value)) || (x.ExternalThreat.HasValue && !Percent(x.ExternalThreat.Value)))
                return r;
            r.Valid = true;
            decimal ratio = (decimal)x.VassalPower / x.OverlordPower;
            // Hysteresis is bounded: even a previously angry vassal must still
            // be stronger AND have a poor relationship at the decision tick.
            decimal strengthThreshold = x.PreviouslyAtRisk ? RebellionHoldStrengthRatio : RebellionEnterStrengthRatio;
            int relationshipThreshold = x.PreviouslyAtRisk ? 40 : 35;
            bool poorRelation = x.Goodwill <= relationshipThreshold && (x.Trust <= 45 || x.Grudge >= 40);
            r.UnderPressure = poorRelation && ratio >= strengthThreshold;
            if (!r.UnderPressure)
            {
                r.Reason = "Schlechtes Verhältnis und militärische Stärke liegen nicht gemeinsam vor";
                return r;
            }

            decimal power = Math.Min(2200m, Math.Max(0m, (ratio - 1m) * 3000m));
            decimal score = 1800m + power + (40m - x.Goodwill) * 42m + (50m - x.Trust) * 22m + x.Grudge * 17m + x.Ambition * 11m - x.Caution * 12m;
            if (x.SupplyReadiness.HasValue)
                score += (x.SupplyReadiness.Value - 50m) * 12m;
            if (x.ExternalThreat.HasValue)
                score -= x.ExternalThreat.Value * 12m;
            r.ScoreBasisPoints = Bound(score, 0, 10000);
            if ((long)x.Day - x.VassalSinceDay < RebellionMinimumTenureDays || x.StrengthEdgeDays < RebellionSustainedDays || x.PoorRelationDays < RebellionSustainedDays)
            {
                r.Reason = "Unzufriedenheit und Stärke müssen sich über mehrere Tage bestätigen";
                return r;
            }

            if (x.LastRebellionDay >= 0 && (long)x.Day - x.LastRebellionDay < RebellionCooldownDays)
            {
                r.Reason = "Nach einer Rebellion gilt eine Erholungsfrist";
                return r;
            }

            // Even an ambitious ruler will not start an optional rebellion
            // while its observed basic supply has collapsed.
            if (x.SupplyReadiness.HasValue && x.SupplyReadiness.Value < 25)
            {
                r.Reason = "Die Versorgung reicht für eine Rebellion nicht aus";
                return r;
            }

            r.ShouldRebel = r.ScoreBasisPoints >= (x.PreviouslyAtRisk ? 5600 : 6000);
            r.Reason = r.ShouldRebel ? "Der starke, dauerhaft unzufriedene Vasall entscheidet sich zur Rebellion" : "Der Vasall bleibt trotz Unzufriedenheit vorsichtig";
            return r;
        }
    }
}
