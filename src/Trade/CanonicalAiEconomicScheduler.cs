// One-OWNER bridge: original snapshot -> pure AI economic policy -> existing
// saved canonical human-offer / AI-intent lanes. Never a new market/native writer.
// The current original-game AI retains sole authority to purchase at market.
using System;
using System.Collections.Generic;
using System.Linq;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        // Budget recovery is derived entirely from already-canonical records.
        // Same global ID in mailbox and archive is counted once. Every generated
        // root has one saved issue day, so normal Save/Cold needs no schema bump.
        private static CanonicalAiInitiativeBudget SavedAiBudget(
            CanonicalModel model, CanonicalTradeSnapshot snapshot)
        {
            var originals = new List<AiInitiativeFact>();
            foreach (var offer in model.HumanOffers)
                if (offer.AiGenerated && offer.RootId == 0 && offer.ParentId == 0)
                    originals.Add(new AiInitiativeFact(offer.Id, offer.From,
                        offer.To, offer.IssuedDay, offer.RequestedAmount, true));
            foreach (var intent in model.Mailbox.Intents)
                if (intent.AiToAi && intent.Action == TradeIntentAction.Propose)
                    originals.Add(new AiInitiativeFact(intent.Id, intent.Requester,
                        intent.Recipient, intent.IssuedDay, intent.RequestedAmount, false));
            foreach (var receipt in model.Archive.Records)
                if (receipt.AiToAi && receipt.RootId == 0)
                    originals.Add(new AiInitiativeFact(receipt.Id, receipt.Requester,
                        receipt.Recipient, receipt.IssuedDay, receipt.RequestedAmount, false));
            return new CanonicalAiInitiativeBudget(model.Day, originals);
        }

        private static int AiMaximumDraftQuantity(
            CanonicalTradeSnapshot world, byte lordSlot, ushort good)
        {
            CanonicalOriginalAiEconomy original;
            AiLordEconomicProfile profile;
            if (!world.TryOriginalAiEconomy(lordSlot, out original) ||
                !AiLordEconomicCatalog.TryGet(original.OriginalLordId,out profile))
                return 0; // missing original authority does not inherit a slot-based personality
            StockCell cell = world.Read(lordSlot,good);
            if (cell == null || cell.Capacity < 0 || cell.Quantity > cell.Capacity)
                return 0;
            StockCell baseStock = cell.Copy();
            baseStock.Buildings = 0; // original pending is accounted for separately
            baseStock.Validate();
            long target = Math.Min(cell.Capacity,
                Math.Max(baseStock.Reserve(),original.NativeAicDemandSignal(good)));
            long uncovered = Math.Max(0L,target-cell.Quantity-original.PendingOriginalPurchase(good));
            return (int)Math.Min(uncovered,Math.Min(profile.MaximumTradeVolume,
                CanonicalAiInitiativeBudget.MaxLordUnitsPerDay));
        }

        private static bool CanScheduleAiEconomicCandidate(
            CanonicalModel model, CanonicalTradeSnapshot snapshot,
            CanonicalAiInitiativeBudget budget, byte requester, byte recipient,
            ushort give, ushort want, int requested, int offered,
            out AiProcurementDecision decision)
        {
            decision = null;
            if (model.Quarantined || budget == null || requested < 1 ||
                offered < 1 || give == want || GoodCatalog.Find(give) == null ||
                GoodCatalog.Find(want) == null || Human(model,requester) ||
                !Member(model,requester) || !Member(model,recipient))
                return false;
            CanonicalOriginalAiEconomy native;
            AiLordEconomicProfile personality;
            if (!snapshot.TryOriginalAiEconomy(requester, out native) ||
                !AiLordEconomicCatalog.TryGet(native.OriginalLordId,out personality))
                return false;
            ObservedTradePairState relation;
            if (!snapshot.TryGetPair(requester,recipient,model.Day,out relation))
                return false;
            // Original military/vassal pressure is not in the observed economic
            // frame yet. Do not replace it with a fabricated "zero risk."
            if (relation.Vassal != 0 || relation.Overlord != 0)
                return false;
            CanonicalPlayer partner = snapshot.Player(recipient);
            StockCell owner = snapshot.Read(requester,want);
            StockCell donor = snapshot.Read(recipient,want);
            if (partner == null || !partner.Alive || owner == null ||
                donor == null || requested > owner.Space ||
                requested > donor.Surplus)
                return false;
            StockCell withoutPending = owner.Copy();
            withoutPending.Buildings = 0;
            withoutPending.Validate();
            int signal = native.NativeAicDemandSignal(want);
            // "buy_*_at" / "trade_*_at" are ACTUAL AIC numeric hints,
            // not an executed vanilla purchase, real consumption forecast
            // or proof that an original market order will be placed.
            int target = Math.Min(owner.Capacity,
                Math.Max(withoutPending.Reserve(), signal));
            var prices = snapshot.Prices();
            Good requestedGood = prices.FirstOrDefault(g => g.Id == want);
            Good offeredGood = prices.FirstOrDefault(g => g.Id == give);
            if (requestedGood == null || offeredGood == null ||
                requestedGood.MarketBuy <= 0m || offeredGood.MarketSell <= 0m)
                return false;
            var history = budget.View(requester,recipient);
            TradeRelationship old = model.Archive.Relationships.FirstOrDefault(
                r => TradeHistoryArchive.PairKey(r.A,r.B) ==
                    TradeHistoryArchive.PairKey(requester,recipient));
            int consecutiveRejected = old == null ? 0 :
                Math.Min(100,Math.Max(0,old.RejectionStreak));
            var ruler = snapshot.Rulers(false).FirstOrDefault(r => r.PlayerId == requester);
            if (ruler == null)
                return false;
            var x = new AiEconomicObservation
            {
                MapFingerprint = snapshot.MapFingerprint,
                OriginalLordAicFingerprint = native.OriginalAicSignalFingerprint,
                Day = model.Day, LordId = native.OriginalLordId,
                OwnerSlot = requester, PartnerSlot = recipient, GoodId = want,
                ScopeObserved = true, LordIdObserved = true, StockObserved = true,
                PendingObserved = true, PriceObserved = true, GoldObserved = true,
                SessionQuarantined = model.Quarantined,
                Quantity = owner.Quantity, Capacity = owner.Capacity,
                Reserve = target,
                // Original game exposes producer PRESENCE, but not an
                // independently attested per-good units/day forecast.
                ConsumptionPerDay = 0, ProductionPerDay = 0, HorizonDays = 1,
                OriginalPendingPurchase = native.PendingOriginalPurchase(want),
                OriginalMarketBuyUnitPrice = requestedGood.MarketBuy,
                OriginalGold = native.OriginalGold,
                EmergencyGoldBuffer = Math.Max(100m,native.OriginalAicGoldReserveSignal),
                // A good's listed price does not prove this AI owns an actual
                // market building. A same-frame alive STRUCT_TRADEPOST is
                // required before classifying the market as feasible. Vanilla
                // remains free to refuse any real market purchase.
                OriginalMarketAvailable = native.OriginalMarketBuildingObserved &&
                    native.OriginalTradepostPresent,
                OwnerAtWar = ruler.AtWar,
                EssentialFood = want >= 10 && want <= 13,
                MilitaryGood = want >= 17 && want <= 24,
                PartnerObserved = true, PartnerAlive = partner.Alive,
                TradeRelationObserved = true,
                PartnerTermsObserved = true,
                PartnerHostile = relation.Hostile,
                AllianceBreakPending = false, // real break not inferred
                PartnerSurplus = donor.Surplus, OwnerFreeSpace = owner.Space,
                Goodwill = relation.Goodwill, Trust = relation.Trust,
                Grudge = relation.Grudge, Threat = relation.Threat,
                // No independent verified vassal pressure is available; those
                // relations were rejected above rather than defaulting to zero.
                VassalRebellionPressure = 0, MaintenanceDueSoon = false,
                PendingNegotiation = Busy(model,requester,recipient),
                EstimatedBilateralUnitCost =
                    (decimal)offered * offeredGood.MarketSell / requested,
                ConsecutiveRejections = consecutiveRejected,
                LastLordInitiativeDay = history.LastLordDay,
                LastPairInitiativeDay = history.LastPairDay,
                LastHumanInboxDay = partner.Human ? history.LastInboxDay : -1,
                LordOffersToday = history.LordOffersToday,
                HumanInboxOffersToday = partner.Human ? history.InboxOffersToday : 0,
                TeamVolumeBudgetRemaining = history.RemainingUnits,
                LastRoute = 0, LastRouteDay = -1
            };
            decision = AiProcurementPolicy.Evaluate(x.Snapshot(),personality);
            // Pure route only gates a newly created proposal. The existing
            // native stock/cell + original AI acceptance are never overwritten.
            return decision.Route == AiProcurementRoute.DiplomaticTradeCandidate &&
                decision.ProposedDiplomaticUnits >= requested;
        }
    }
}
