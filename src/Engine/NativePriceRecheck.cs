using System;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class NativePricePolicy
    {
        internal static bool ExactTermsStillBeneficial(BarterQuote q, int offeredBuy, int offeredSell, int requestedBuy, int requestedSell)
        {
            if (q == null || q.Requester < 1 || q.Requester > 8 || q.Recipient < 1 || q.Recipient > 8 || q.Requester == q.Recipient || q.OfferedGood == q.RequestedGood || q.OfferedAmount < 1 || q.OfferedAmount > 100000 || q.RequestedAmount < 1 || q.RequestedAmount > 100000)
                return false;
            try
            {
                Good offered = ObservedTradeMarketSnapshot.FromOriginalPacked(q.OfferedGood, "offered", offeredBuy, offeredSell);
                Good requested = ObservedTradeMarketSnapshot.FromOriginalPacked(q.RequestedGood, "requested", requestedBuy, requestedSell);
                decimal offeredValue = offered.Mid * q.OfferedAmount;
                decimal recipientGain = offeredValue - requested.MarketSell * q.RequestedAmount;
                decimal requesterSaving = requested.MarketBuy * q.RequestedAmount - offeredValue;
                // Identical invariant to BarterEngine.EconomicAdvantage, applied to the
                // actual current native market rather than the earlier detached snapshot.
                return recipientGain > requesterSaving && requesterSaving > 0m;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private bool CurrentNativeEconomics(BarterQuote quote)
        {
            RequireNativeDiplomacySection();
            if (quote == null)
                return false;
            var api = GamePlayerManagerAPI.Instance;
            if (api == null || !api.IsTradeGoodAllowed((eGoods)quote.OfferedGood) || !api.IsTradeGoodAllowed((eGoods)quote.RequestedGood))
                return false;
            PackedGoodPrice give = api.GetTradeBasePrice((eGoods)quote.OfferedGood);
            PackedGoodPrice want = api.GetTradeBasePrice((eGoods)quote.RequestedGood);
            return NativePricePolicy.ExactTermsStillBeneficial(quote, give.BuyPrice, give.SellPrice, want.BuyPrice, want.SellPrice);
        }
    }
}
