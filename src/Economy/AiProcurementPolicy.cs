// PRIVATE SAFE STAGE: a pure suggestion engine; NO market, goods, diplomacy,
// GameAction, SaveData, Unity, Steam or original native mutators are called.
// "OriginalMarket" explicitly means: do not send a new mod trade request.
// The original game's market AI remains the only writer of its own purchases.
using System;
using System.Linq;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.TradeNegotiation
{
    // Neutral values intentionally mirror existing TradeOfferGenerosity.
    // The canonical owner must explicitly validate any future mapping.
    public enum AiSuggestedTradeTier
    {
        Grosszuegig = 0,
        Fair = 1,
        Knausrig = 2
    }

    public enum AiProcurementRoute
    {
        BlockedUnknownEvidence = 0,
        StockSufficient = 1,
        WaitForOriginalMarket = 2,
        WaitForProduction = 3,
        OriginalMarket = 4,
        DiplomaticTradeCandidate = 5,
        WaitForPartner = 6,
        WaitCooldown = 7,
        WaitAllianceRisk = 8
    }

    // The only mutable object is the ingestion DTO. An adapter must independently
    // attest all booleans. Snapshot() freezes a private copy for deterministic review.
    public sealed class AiEconomicObservation
    {
        public string MapFingerprint, OriginalLordAicFingerprint;
        public int Day, LordId, OwnerSlot, PartnerSlot, GoodId;
        public bool ScopeObserved, LordIdObserved, StockObserved, PendingObserved, PriceObserved, GoldObserved;
        public bool SessionQuarantined;
        public int Quantity, Capacity, Reserve, ConsumptionPerDay, ProductionPerDay, HorizonDays;
        public int OriginalPendingPurchase;
        public decimal OriginalMarketBuyUnitPrice, OriginalGold, EmergencyGoldBuffer;
        public bool OriginalMarketAvailable, OwnerAtWar, EssentialFood, MilitaryGood;
        public bool PartnerObserved, PartnerAlive, TradeRelationObserved, PartnerTermsObserved;
        public bool PartnerHostile, AllianceBreakPending;
        public int PartnerSurplus, OwnerFreeSpace, Goodwill, Trust, Grudge, Threat, VassalRebellionPressure;
        public bool MaintenanceDueSoon, PendingNegotiation;
        public decimal EstimatedBilateralUnitCost;
        public int ConsecutiveRejections, LastLordInitiativeDay = -1,
            LastPairInitiativeDay = -1, LastHumanInboxDay = -1;
        public int LordOffersToday, HumanInboxOffersToday, TeamVolumeBudgetRemaining;
        public int LastRoute; // 0 unknown, 1 original market, 2 diplomacy
        public int LastRouteDay = -1;

        public AiEconomicSnapshot Snapshot() { return new AiEconomicSnapshot((AiEconomicObservation)MemberwiseClone()); }
    }

    public sealed class AiEconomicSnapshot
    {
        internal readonly AiEconomicObservation Value;
        internal AiEconomicSnapshot(AiEconomicObservation frozen) { Value = frozen; }
    }

    public sealed class AiProcurementDecision
    {
        public AiProcurementRoute Route { get; internal set; }
        public string Reason { get; internal set; }
        public int DeficitBeforePending { get; internal set; }
        public int UncoveredDemand { get; internal set; }
        public int AffordableOriginalMarketUnits { get; internal set; }
        public int SuggestedOriginalMarketUnits { get; internal set; }
        public int ProposedDiplomaticUnits { get; internal set; }
        public int EarliestDiplomaticDay { get; internal set; }
        public int AlliancePressureIndex { get; internal set; }
        public int StrategicPriorityScore { get; internal set; }
        public AiSuggestedTradeTier SuggestedGenerosity { get; internal set; }
        public string PolicyHash { get; internal set; }
        public int LordId { get; internal set; }
    }

    public static class AiProcurementPolicy
    {
        static bool Percentage(int n) { return n >= 0 && n <= 100; }
        static bool Slot(int n) { return n >= 1 && n <= 8; }
        static bool DayHistory(int d, int now) { return d == -1 || (d >= 1 && d <= now); }
        static int Clamp(int value, int a, int b) { return Math.Max(a, Math.Min(value, b)); }
        static int SafeNext(int day, long delta)
        {
            return (int)Math.Min((long)int.MaxValue, (long)day + Math.Max(0L, delta));
        }
        static bool ShaScope(string h)
        {
            return h != null && h.Length == 64 && h.All(c =>
                (c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'));
        }
        static AiProcurementDecision Finish(AiProcurementDecision d, AiProcurementRoute route, string reason)
        {
            d.Route = route; d.Reason = reason; return d;
        }

        private static bool ValidCore(AiEconomicObservation x, AiLordEconomicProfile p)
        {
            return x != null && p != null && x.LordId == p.LordId &&
                x.Day >= 1 && x.Day < int.MaxValue &&
                ShaScope(x.MapFingerprint) && ShaScope(x.OriginalLordAicFingerprint) &&
                !x.SessionQuarantined && x.ScopeObserved && x.LordIdObserved &&
                x.StockObserved && x.PendingObserved &&
                x.PriceObserved && x.GoldObserved && Slot(x.OwnerSlot) &&
                x.GoodId > 0 && x.GoodId <= ushort.MaxValue &&
                GoodCatalog.Find((ushort)x.GoodId) != null &&
                x.Quantity >= 0 && x.Capacity >= 0 && x.Capacity <= 1000000 &&
                x.Quantity <= x.Capacity && x.Reserve >= 0 && x.Reserve <= x.Capacity &&
                x.ConsumptionPerDay >= 0 && x.ConsumptionPerDay <= 100000 &&
                x.ProductionPerDay >= 0 && x.ProductionPerDay <= 100000 &&
                x.HorizonDays >= 1 && x.HorizonDays <= 10 &&
                x.OriginalPendingPurchase >= 0 && x.OriginalPendingPurchase <= 1000000 &&
                x.OriginalMarketBuyUnitPrice > 0m && x.OriginalMarketBuyUnitPrice <= 100000000m &&
                x.OriginalGold >= 0m && x.OriginalGold <= 1000000000m &&
                x.EmergencyGoldBuffer >= 0m && x.EmergencyGoldBuffer <= 1000000000m &&
                x.LordOffersToday >= 0 && x.LordOffersToday <= 10000 &&
                x.HumanInboxOffersToday >= 0 && x.HumanInboxOffersToday <= 10000 &&
                x.TeamVolumeBudgetRemaining >= 0 && x.TeamVolumeBudgetRemaining <= 1000000 &&
                x.ConsecutiveRejections >= 0 && x.ConsecutiveRejections <= 100 &&
                DayHistory(x.LastLordInitiativeDay,x.Day) &&
                DayHistory(x.LastPairInitiativeDay,x.Day) &&
                DayHistory(x.LastHumanInboxDay,x.Day) &&
                DayHistory(x.LastRouteDay,x.Day) &&
                x.LastRoute >= 0 && x.LastRoute <= 2 &&
                (x.LastRouteDay != -1 || x.LastRoute == 0) &&
                (x.PartnerSlot == 0 || (Slot(x.PartnerSlot) && x.PartnerSlot != x.OwnerSlot));
        }

        private static bool ValidPartner(AiEconomicObservation x)
        {
            if (x.PartnerSlot == 0 || !x.PartnerObserved)
                return false;
            return x.PartnerAlive && !x.PartnerHostile && !x.AllianceBreakPending &&
                x.TradeRelationObserved && x.PartnerTermsObserved &&
                Percentage(x.Goodwill) && Percentage(x.Trust) &&
                Percentage(x.VassalRebellionPressure) &&
                Percentage(x.Grudge) && Percentage(x.Threat) &&
                x.PartnerSurplus >= 0 && x.PartnerSurplus <= 1000000 &&
                x.OwnerFreeSpace >= 0 && x.OwnerFreeSpace <= x.Capacity - x.Quantity &&
                x.EstimatedBilateralUnitCost > 0m && x.EstimatedBilateralUnitCost <= 100000000m;
        }

        // This is a relative, informational pressure index, NOT the probability
        // of an original alliance break. Any real break remains in diplomacy owner.
        private static int AlliancePressure(AiEconomicObservation x, AiLordEconomicProfile p)
        {
            int pressure = Math.Max(0,55 - x.Trust)*2 + Math.Max(0,50 - x.Goodwill) +
                x.Grudge/2 + x.Threat/3 + (x.MaintenanceDueSoon ? 18 : 0) +
                (x.ConsecutiveRejections*5) + x.VassalRebellionPressure/2;
            return Clamp(pressure + p.Caution/12 - p.Loyalty/10,0,100);
        }

        public static AiProcurementDecision Evaluate(AiEconomicSnapshot observed, AiLordEconomicProfile profile)
        {
            var d = new AiProcurementDecision {
                Route = AiProcurementRoute.BlockedUnknownEvidence,
                Reason = "Unverified original source or invalid economics",
                SuggestedGenerosity = AiSuggestedTradeTier.Fair,
                LordId = profile == null ? 0 : profile.LordId
            };
            if (observed == null || !ValidCore(observed.Value,profile))
                return d;
            var x = observed.Value;
            d.PolicyHash = profile.PolicyHash(x.OriginalLordAicFingerprint);
            d.EarliestDiplomaticDay = x.Day;
            // Stock inventory is physical; original pending purchases are NOT delivered.
            // All arithmetic up to the final bounded quantity uses long to avoid overflow.
            long horizonNet = Math.Max(0L,(long)x.ConsumptionPerDay-x.ProductionPerDay)*x.HorizonDays;
            long shortage = Math.Max(0L,(long)x.Reserve+horizonNet-x.Quantity);
            int availableSpace = x.Capacity - x.Quantity;
            d.DeficitBeforePending = (int)Math.Min(availableSpace,shortage);
            d.UncoveredDemand = Math.Max(0,d.DeficitBeforePending-x.OriginalPendingPurchase);
            // Priority is informational: the existing owner must still compare
            // all goods from the same verified original generation.
            int normalizedShortage = (int)Math.Min(60L,(long)d.UncoveredDemand*60L/Math.Max(1,x.Capacity));
            d.StrategicPriorityScore = Clamp(normalizedShortage +
                (x.EssentialFood ? 25 : 0) +
                (x.OwnerAtWar && x.MilitaryGood ? profile.WarFocus/2 : 0),0,100);
            if (d.DeficitBeforePending == 0)
                return Finish(d,AiProcurementRoute.StockSufficient,"No current physical shortage");
            if (d.UncoveredDemand == 0)
                return Finish(d,AiProcurementRoute.WaitForOriginalMarket,"Existing original pending market purchases cover the forecast");
            // A short natural production wait avoids creating redundant requests.
            bool emergency = x.Quantity < x.Reserve && (x.EssentialFood || (x.OwnerAtWar && x.MilitaryGood));
            if (!emergency && x.ProductionPerDay > x.ConsumptionPerDay &&
                (long)(x.ProductionPerDay-x.ConsumptionPerDay)*Math.Min(2,x.HorizonDays) >= d.UncoveredDemand)
                return Finish(d,AiProcurementRoute.WaitForProduction,"Observed production can cover demand within two days");
            decimal spendable = Math.Max(0m,x.OriginalGold-x.EmergencyGoldBuffer);
            decimal affordable = Math.Floor(spendable/x.OriginalMarketBuyUnitPrice);
            d.AffordableOriginalMarketUnits = (int)Math.Min((decimal)int.MaxValue,affordable);
            d.SuggestedOriginalMarketUnits = x.OriginalMarketAvailable ?
                Math.Min(d.UncoveredDemand,d.AffordableOriginalMarketUnits) : 0;
            bool hasMarket = d.SuggestedOriginalMarketUnits > 0;
            bool pair = ValidPartner(x);
            int physicalWithoutBudget = pair ? Math.Min(
                Math.Min(d.UncoveredDemand,x.PartnerSurplus),
                Math.Min(x.OwnerFreeSpace,profile.MaximumTradeVolume)) : 0;
            int physical = Math.Min(physicalWithoutBudget,x.TeamVolumeBudgetRemaining);
            bool partnerFeasible = pair && physical >= (emergency ? 1 : 3);
            if (pair)
                d.AlliancePressureIndex = AlliancePressure(x,profile);
            // Respect commitments and do not invent an additional market purchase.
            bool marketCovers = hasMarket && d.SuggestedOriginalMarketUnits >= d.UncoveredDemand;
            bool cheapMarket = pair && x.OriginalMarketBuyUnitPrice <=
                x.EstimatedBilateralUnitCost*
                (1.12m+profile.MarketPreference/250m-profile.Greed/700m);
            bool keepMarket = x.LastRoute == 1 && x.LastRouteDay >= 0 &&
                (long)x.Day-x.LastRouteDay <= 2;
            if (marketCovers && (cheapMarket || keepMarket ||
                profile.MarketPreference >= 65 || !partnerFeasible))
                return Finish(d,AiProcurementRoute.OriginalMarket,"Original market is affordable or preferred; original AI remains the purchaser");
            if (pair && physicalWithoutBudget >= (emergency ? 1 : 3) &&
                physical < (emergency ? 1 : 3))
            {
                d.EarliestDiplomaticDay = SafeNext(x.Day,1);
                return hasMarket
                    ? Finish(d,AiProcurementRoute.OriginalMarket,"Daily volume budget exhausted; vanilla market remains free")
                    : Finish(d,AiProcurementRoute.WaitCooldown,"Daily volume budget exhausted");
            }
            if (!partnerFeasible)
                return hasMarket
                    ? Finish(d,AiProcurementRoute.OriginalMarket,"No verified surplus/terms; original market may cover part of demand")
                    : Finish(d,AiProcurementRoute.WaitForPartner,"No eligible partner with verified stock and reciprocal terms");
            // Risk is a reason to postpone requests, never a right to terminate pacts.
            // Loyal and honorable rulers protect a fragile pact sooner;
            // opportunists tolerate more pressure. Never break a pact here.
            int threshold = Clamp(60 - profile.Loyalty/8 - profile.Honor/10 +
                profile.Opportunism/8,38,75);
            if (d.AlliancePressureIndex >= threshold)
                return hasMarket
                    ? Finish(d,AiProcurementRoute.OriginalMarket,"Alliance pressure: do not spam a fragile partner")
                    : Finish(d,AiProcurementRoute.WaitAllianceRisk,"Alliance pressure: postpone rather than harm relations");
            long interval = profile.MinimumRequestIntervalDays +
                x.ConsecutiveRejections*2L + d.AlliancePressureIndex/30 +
                (100-profile.Initiative)/35 + profile.Patience/45;
            if (x.LastLordInitiativeDay >= 1)
                d.EarliestDiplomaticDay = Math.Max(d.EarliestDiplomaticDay,
                    SafeNext(x.LastLordInitiativeDay,interval));
            if (x.LastPairInitiativeDay >= 1)
                d.EarliestDiplomaticDay = Math.Max(d.EarliestDiplomaticDay,
                    SafeNext(x.LastPairInitiativeDay,interval));
            if (x.LastHumanInboxDay >= 1)
                d.EarliestDiplomaticDay = Math.Max(d.EarliestDiplomaticDay,
                    SafeNext(x.LastHumanInboxDay,2));
            if (x.LordOffersToday > 0 || x.HumanInboxOffersToday >= 2 || x.PendingNegotiation)
                d.EarliestDiplomaticDay = Math.Max(d.EarliestDiplomaticDay,SafeNext(x.Day,1));
            if (d.EarliestDiplomaticDay > x.Day)
                return hasMarket
                    ? Finish(d,AiProcurementRoute.OriginalMarket,"Diplomatic cooldown; leave affordable original market to vanilla")
                    : Finish(d,AiProcurementRoute.WaitCooldown,"Existing request, per-lord/inbox budget or relation cooldown");
            if (marketCovers && !emergency &&
                (profile.MarketPreference+profile.Caution+profile.Patience)/3 >= 46)
                return Finish(d,AiProcurementRoute.OriginalMarket,"Personality favours predictable original market supply");
            d.ProposedDiplomaticUnits = physical;
            // Only a proposal tier; actual game acceptance and both sides'
            // original economic terms stay in the existing negotiation owner.
            d.SuggestedGenerosity =
                profile.Generosity >= 65 && profile.Honor >= 70
                    ? AiSuggestedTradeTier.Grosszuegig
                    : profile.Greed >= 75 && profile.Generosity < 30 && x.Goodwill >= 70
                        ? AiSuggestedTradeTier.Knausrig
                        : AiSuggestedTradeTier.Fair;
            return Finish(d,AiProcurementRoute.DiplomaticTradeCandidate,
                "Verified physical need, surplus, observed relation and request budget; native consent still required");
        }
    }
}
