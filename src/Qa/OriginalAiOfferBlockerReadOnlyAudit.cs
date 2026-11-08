// original offer blocker: strict private-QA, read-only explanation of why the REAL original
// two-snapshot detached MOD-AI economic scheduler generated zero offers.
// NO synthetic game state, no save, GUI/Steam command or native goods write.
// Source invokes the very same existing AI demand-bound and economy reads.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        internal string ReadOnlyOriginalOfferBlockerAudit(
            CanonicalTradeSnapshot world)
        {
            if (world == null || _model == null)
                return "audit=refused|reason=missing_native_snapshot";
            CanonicalModel model = _model;
            if (model.Archive == null || model.Mailbox == null ||
                model.Quarantined || model.InputEpoch == 0 ||
                model.Day != world.Day ||
                model.LastSharedTick != world.SharedTick ||
                !String.Equals(world.MapFingerprint,
                    model.Archive.MapFingerprint,StringComparison.Ordinal) ||
                !String.Equals(world.RosterFingerprint,
                    model.Archive.RosterFingerprint,StringComparison.Ordinal) ||
                world.MapGeneration <= 0 ||
                world.LocalOfflineHumanSlot < 1 ||
                world.LocalOfflineHumanSlot > 8)
                return "audit=refused|reason=noncurrent_original_map_or_epoch";

            var ai = world.Players().Where(x => !x.Human && x.Alive)
                .OrderBy(x => x.Slot).ToArray();
            var humans = world.Players().Where(x => x.Human && x.Alive)
                .OrderBy(x => x.Slot).ToArray();
            if (humans.Length != 1 ||
                humans[0].Slot != world.LocalOfflineHumanSlot ||
                ai.Length == 0)
                return "audit=refused|reason=not_original_solo_human_and_live_ai";

            int nativeAicLords = 0, marketBuildingLords = 0;
            int tradeposts = 0, totalPairs = 0, pairObserved = 0;
            int pairEligible = 0, pairHostile = 0, pairNoPact = 0;
            int pairVassal = 0, pairNotAuthoritative = 0;
            int positiveAiNeeds = 0, aiLowStockNeeds = 0;
            int originalAicUncovered = 0, humanDonorSurplus = 0;
            int ownerFreeCapacity = 0, eligibleQuantityAtLeastThree = 0;
            int modCandidateSourceSignals = 0;
            // Existing BuildWorld uses ONLY the fully copied same-tick
            // canonical stock+market snapshot. No Native setter or save owner.
            ManagedGoodsSnapshotPort detachedStocks;
            DiplomaticTradeNegotiator economy =
                BuildWorld(model,world,false,out detachedStocks);
            if (economy == null)
                return "audit=refused|reason=detached_economy_not_built";

            foreach (var lord in ai)
            {
                CanonicalOriginalAiEconomy original;
                if (!world.TryOriginalAiEconomy(lord.Slot,out original) ||
                    original == null)
                    continue;
                nativeAicLords++;
                if (original.OriginalMarketBuildingObserved) marketBuildingLords++;
                if (original.OriginalTradepostPresent) tradeposts++;
                foreach (var human in humans)
                {
                    totalPairs++;
                    ObservedTradePairState direct,reverse;
                    if (!world.TryGetPair(lord.Slot,human.Slot,model.Day,out direct) ||
                        !world.TryGetPair(human.Slot,lord.Slot,model.Day,out reverse))
                        continue;
                    pairObserved++;
                    if (!direct.Authoritative)pairNotAuthoritative++;
                    if (direct.Hostile)pairHostile++;
                    if (!direct.HasTradeRelation)pairNoPact++;
                    if (direct.Vassal!=0||direct.Overlord!=0)pairVassal++;
                    string pairReason;
                    bool eligible=TradePairEligibilityRules.CanTrade(
                        direct,lord.Slot,human.Slot,model.Day,out pairReason);
                    if (eligible)pairEligible++;
                    foreach (Good good in GoodCatalog.All)
                    {
                        StockCell aiStock=world.Read(lord.Slot,good.Id);
                        StockCell donorStock=world.Read(human.Slot,good.Id);
                        if (aiStock==null || donorStock==null) continue;
                        int nativeBound=AiMaximumDraftQuantity(
                            world,lord.Slot,good.Id);
                        bool need=economy.NeedScore(lord.Slot,good.Id)>0;
                        bool low=(long)aiStock.Quantity<
                            (long)aiStock.Reserve()+30L;
                        int donor=donorStock.Surplus;
                        int free=aiStock.Space;
                        if (need)positiveAiNeeds++;
                        if (need && low)aiLowStockNeeds++;
                        if (nativeBound>0)originalAicUncovered++;
                        if (donor>=3)humanDonorSurplus++;
                        if (free>=3)ownerFreeCapacity++;
                        if (need && low && nativeBound>=3 &&
                            donor>=3 && free>=3)
                        {
                            eligibleQuantityAtLeastThree++;
                            if (eligible)modCandidateSourceSignals++;
                        }
                    }
                }
            }

            // Evidence describes factors, NOT a vanilla AI trade order and
            // never states that a human accepted this MOD-generated offer.
            string reason=pairEligible==0
                ? "no_current_original_diplomatic_trade_relation"
                : originalAicUncovered==0
                ? "no_uncovered_current_original_aic_demand"
                : eligibleQuantityAtLeastThree==0
                ? "need_stock_donor_or_original_capacity_short"
                : modCandidateSourceSignals==0
                ? "candidate_exists_but_trade_pair_not_eligible"
                : "downstream_quote_fairness_budget_or_policy_check";
            return "audit=original_readonly_success" +
                "|reason="+reason +
                "|original_tick="+world.SharedTick +
                "|ai_lords="+ai.Length +
                "|original_aic_lords="+nativeAicLords +
                "|market_building_lords="+marketBuildingLords +
                "|original_tradepost_lords="+tradeposts +
                "|total_pairs="+totalPairs +
                "|pairs_observed="+pairObserved +
                "|pairs_eligible="+pairEligible +
                "|pairs_hostile="+pairHostile +
                "|pairs_without_trade_pact="+pairNoPact +
                "|pairs_with_vassal="+pairVassal +
                "|pairs_unattested="+pairNotAuthoritative +
                "|positive_ai_need_good_cells="+positiveAiNeeds +
                "|ai_low_stock_need_cells="+aiLowStockNeeds +
                "|original_aic_uncovered_good_cells="+originalAicUncovered +
                "|human_surplus_good_cells="+humanDonorSurplus +
                "|owner_capacity_good_cells="+ownerFreeCapacity +
                "|eligible_stock_quantity_good_cells="+eligibleQuantityAtLeastThree +
                "|eligible_pair_and_stock_candidate_cells="+modCandidateSourceSignals +
                "|original_cpu_diplomatic_chore=false" +
                "|original_human_consent=false" +
                "|native_goods_writes=0";
        }
    }
}
#endif
