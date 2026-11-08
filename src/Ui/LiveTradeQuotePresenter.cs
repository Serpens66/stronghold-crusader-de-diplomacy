//  trade preview uses actual live original GamePlayerManagerAPI prices
// and inventories, never hardcoded vanilla economy or fictional warehouse
// space. Purely presentational: an accepted trade rechecks live Native state.
using System;
using System.Globalization;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private static int IndicativeQuote(int wanted, int offered, int quantity, TradeOfferGenerosity tone, Good requested, Good given)
        {
            if (requested == null || given == null || wanted == offered || quantity < 1 || quantity > 100000)
                return 0;
            try
            {
                decimal sell = requested.MarketSell * quantity;
                decimal buy = requested.MarketBuy * quantity;
                decimal spread = buy - sell;
                decimal mid = given.Mid;
                if (mid <= 0 || spread <= 0)
                    return 0;
                decimal low = Math.Max(1, decimal.Floor((sell + spread / 2m) / mid) + 1m);
                decimal high = Math.Min(100000, decimal.Ceiling(buy / mid) - 1m);
                if (low > high)
                    return 0;
                decimal share = DiplomaticTradeNegotiator.RecipientSpreadShare(tone);
                decimal nearest = decimal.Round((sell + share * spread) / mid, 0, MidpointRounding.AwayFromZero);
                int result = (int)Math.Min(high, Math.Max(low, nearest));
                decimal value = mid * result;
                return value - sell > buy - value && buy - value > 0m ? result : 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        private string ReadTradePreview(int partner, object panel)
        {
            if (!_mapActive || _mapDisabled || partner < 1 || partner > 8 || !_roster.Contains(partner) || partner == _localPlayer)
                return string.Empty;
            if (_tradeWantedIndex >= TradeAllowedGoods.Length || _tradeOfferedIndex >= TradeAllowedGoods.Length)
                return Text("Die KI wählt passende Waren und prüft den aktuellen Markt.", "AI chooses suitable goods and checks current market prices.");
            if (_tradeWantedIndex < 0 || _tradeOfferedIndex < 0 || _tradeWantedIndex == _tradeOfferedIndex)
                return string.Empty;
            string input = ReadControlProperty(TradeElement(panel, "TradeAmountInput"), "Text") as string;
            int wantedAmount;
            if (!TryCustomAmount(input, out wantedAmount))
                return Text("Bitte eine gültige Menge eingeben.", "Enter a valid amount.");
            int wanted = TradeAllowedGoods[_tradeWantedIndex];
            int offered = TradeAllowedGoods[_tradeOfferedIndex];
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return Text("Bestände werden aktualisiert.", "Updating inventories.");
            try
            {
                using (lease)
                {
                    var api = GamePlayerManagerAPI.Instance;
                    if (api == null)
                        return string.Empty;
                    PackedGoodPrice requested = api.GetTradeBasePrice((eGoods)wanted);
                    PackedGoodPrice given = api.GetTradeBasePrice((eGoods)offered);
                    Good priceWant = ObservedTradeMarketSnapshot.FromOriginalPacked((ushort)wanted, GoodName(wanted, 1), requested.BuyPrice, requested.SellPrice);
                    Good priceOffer = ObservedTradeMarketSnapshot.FromOriginalPacked((ushort)offered, GoodName(offered, 1), given.BuyPrice, given.SellPrice);
                    int suggested = IndicativeQuote(wanted, offered, wantedAmount, _tradeGenerosity, priceWant, priceOffer);
                    if (suggested == 0)
                        return Text("Zu diesen Marktpreisen gibt es kein faires ganzzahliges Tauschangebot.", "There is no fair whole-unit barter at current market prices.");
                    int mine = GoodAmount(_localPlayer, offered);
                    int his = GoodAmount(partner, wanted);
                    if (mine < 0 || his < 0)
                        return string.Empty;
                    // trade readiness: THIS is original-engine GROSS stock, not
                    // provably sellable surplus or native free warehouse
                    // capacity. The official 0.16.3 normal release does not
                    // attest the original C5AD0 capacity/Reserve interface,
                    // and this UI must never fake that approval.
                    BarterPreviewState readiness =
                        ReadOnlyTradeReadiness.Assess(
                            new OriginalBarterFacts {
                                NativeReadSectionHeld = IsNativeDiplomacySectionHeld,
                                HumanCurrentRawStock = mine,
                                AiCurrentRawStock = his,
                                HumanGives = suggested,
                                HumanReceives = wantedAmount,
                                ExactOriginalStockAndStorageAttested = false,
                                ExactOriginalAiNeedAttested = false
                            });
                    // trade preview: four short lines in a dedicated, non-overlapping
                    // Noesis field. The pure formatter is only a UI projection
                    // of the same original trade readiness read-only stock observation.
                    return TradePreviewLayout.FourLines(
                        mine, his, suggested, wantedAmount,
                        GoodName(offered, 1), GoodName(wanted, 1),
                        readiness, Text("DE", "EN") == "DE");
                }
            }
            catch (Exception e)
            {
                try
                {
                    Log("trade_session", "status=price_preview_unavailable|reason=" + Safe(e.GetType().Name));
                }
                catch (Exception)
                {
                }

                return Text("Handelsvorschau wird neu berechnet.", "Recalculating trade preview.");
            }
        }
    }
}
