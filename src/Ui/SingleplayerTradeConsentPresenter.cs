using System;
using System.Collections.Generic;
using System.Linq;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private readonly HashSet<int> _consentCounterDrafts = new HashSet<int>();
        private CanonicalHumanOffer PendingConsent(int partner, bool incoming)
        {
            if (!CanonicalOwnsTradeLane || _canonical == null)
                return null;
            return _canonical.HumanOffersFor((byte)_localPlayer).Where(x => x.State == CanonicalHumanConsentState.Pending && x.ExpiresDay >= Math.Max(1, _lastProcessedDay) && x.ExpiresClock >= (_mailbox == null ? 0 : _mailbox.Clock) && (incoming ? x.To == _localPlayer && x.From == partner : x.From == _localPlayer && x.To == partner)).OrderByDescending(x => x.Id).FirstOrDefault();
        }

        private TradeRecord ConsentDisplayCounter(int partner)
        {
            var offer = PendingConsent(partner, true);
            if (offer == null || _consentCounterDrafts.Contains(partner))
                return null;
            // Read-only orientation for the existing "I give / I receive" labels.
            // This projection is never inserted into  or accepted as an AI quote.
            return new TradeRecord
            {
                Id = offer.Id,
                Requester = (byte)_localPlayer,
                Recipient = (byte)partner,
                OfferedGood = offer.RequestedGood,
                OfferedAmount = offer.RequestedAmount,
                RequestedGood = offer.OfferedGood,
                RequestedAmount = offer.OfferedAmount,
                IssuedDay = offer.IssuedDay,
                ExpiryDay = offer.ExpiresDay,
                Generosity = offer.Tier,
                State = TradeDisposition.PendingCounter
            };
        }

        private bool HandleConsentUi(string action, int partner, object panel)
        {
            if (!CanonicalOwnsTradeLane)
                return false;
            var incoming = PendingConsent(partner, true);
            var outgoing = PendingConsent(partner, false);
            bool editing = _consentCounterDrafts.Contains(partner);
            if (action == "Counter")
            {
                if (incoming == null || incoming.Depth >= 8)
                    return true;
                _consentCounterDrafts.Add(partner);
                _tradeStatus = Text("Wählt die Waren und die Menge für Euer Gegenangebot.", "Choose goods and quantity for your counteroffer.");
                return true;
            }

            CanonicalTradeAction requested;
            CanonicalHumanOffer offer;
            if (action == "Withdraw")
            {
                offer = outgoing;
                requested = CanonicalTradeAction.WithdrawHuman;
            }
            else if ((action == "Accept" || action == "Decline") && incoming != null)
            {
                offer = incoming;
                requested = action == "Accept" ? CanonicalTradeAction.AcceptHuman : CanonicalTradeAction.DeclineHuman;
            }
            else if (action == "Send" && editing)
            {
                offer = incoming;
                requested = CanonicalTradeAction.CounterHuman;
            }
            else
                return false;
            if (offer == null)
                return true;
            // original hud – observe the original Noesis UI acceptance callback
            // in the NORMAL release even when goods authority is CLOSED.
            // The callback and a local mouse event are NOT a cryptographic
            // human identity, not an accepted quote and not a native grant.
            // In headless ui observation headless QA, panel==null and no HUD evidence is emitted.
            if (requested == CanonicalTradeAction.AcceptHuman)
                ObserveHudConsentCandidate(offer,partner,panel);
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
            {
                _tradeStatus = Text("Die Antwort konnte noch nicht gesendet werden. Bitte erneut versuchen.", "The reply could not be sent yet. Please try again.");
                return true;
            }

            using (lease)
            {
                string reason;
                if (!IsSoloEngineSession(out reason) || !NativeAuthorized() || _canonicalStopped || _canonicalInputs.Count >= 16 || _canonicalInputs.Any(x => x.A == _localPlayer && x.B == partner))
                    return true;
                var input = new LocalTradeCommand
                {
                    A = (byte)_localPlayer,
                    B = (byte)partner,
                    Consent = offer.Copy(),
                    ConsentAction = requested
                };
                if (requested == CanonicalTradeAction.CounterHuman)
                {
                    if (!ReadDraftControls(panel, out reason) || _tradeWantedIndex < 0 || _tradeOfferedIndex < 0 || _tradeWantedIndex >= TradeAllowedGoods.Length || _tradeOfferedIndex >= TradeAllowedGoods.Length)
                    {
                        _tradeStatus = Text("Für ein Gegenangebot bitte beide Waren und eine gültige Menge wählen.", "Choose both goods and a valid quantity for a counteroffer.");
                        return true;
                    }

                    input.Want = (ushort)TradeAllowedGoods[_tradeWantedIndex];
                    input.Give = (ushort)TradeAllowedGoods[_tradeOfferedIndex];
                    input.Amount = _tradeRequestedAmount;
                    input.Tier = (byte)_tradeGenerosity;
                    var api = GamePlayerManagerAPI.Instance;
                    if (api == null)
                        return true;
                    var want = api.GetTradeBasePrice((eGoods)input.Want);
                    var give = api.GetTradeBasePrice((eGoods)input.Give);
                    input.LiteralOfferedAmount = IndicativeQuote(input.Want, input.Give, input.Amount, _tradeGenerosity, ObservedTradeMarketSnapshot.FromOriginalPacked(input.Want, GoodName(input.Want, 1), want.BuyPrice, want.SellPrice), ObservedTradeMarketSnapshot.FromOriginalPacked(input.Give, GoodName(input.Give, 1), give.BuyPrice, give.SellPrice));
                    if (input.LiteralOfferedAmount < 1)
                        return true;
                }

                _canonicalInputs.Add(input);
                _consentCounterDrafts.Remove(partner);
                _tradeStatus = Text("Eure Antwort wird übermittelt.", "Your reply is being delivered.");
            }

            return true;
        }

        private void RefreshConsentControls(object panel, int partner)
        {
            var incoming = PendingConsent(partner, true);
            var outgoing = PendingConsent(partner, false);
            bool editing = _consentCounterDrafts.Contains(partner);
            bool showCounter = incoming != null && !editing && incoming.Depth < 8;
            bool queued = _canonicalInputs.Any(x => x.A == _localPlayer && x.B == partner);
            // Reuse the same Native availability for the controls and caption.
            // In normal release Preview must not advertise an executable Send.
            bool nativeAvailable = NativeAuthorized();
            bool allowed = !queued && !_canonicalStopped && nativeAvailable;
            object counter = TradeElement(panel, "TradeConsentCounter");
            object withdraw = TradeElement(panel, "TradeConsentWithdraw");
            SetVisibilityIfPresent(counter, showCounter);
            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentCounterLabel"), showCounter);
            SetVisibilityIfPresent(withdraw, outgoing != null);
            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentWithdrawLabel"), outgoing != null);
            SetVisibilityIfPresent(TradeElement(panel, "TradeChronicle"), !showCounter);
            SetTextIfPresent(panel, "TradeConsentCounterLabel", Text("GEGENANGEBOT", "COUNTEROFFER"));
            SetTextIfPresent(panel, "TradeConsentWithdrawLabel", Text("ZURÜCKZIEHEN", "WITHDRAW"));
            if (counter != null)
                SetProperty(counter, "IsEnabled", allowed);
            if (withdraw != null)
                SetProperty(withdraw, "IsEnabled", allowed);
            if (outgoing != null)
            {
                SetVisibilityIfPresent(TradeElement(panel, "TradeSend"), false);
                SetVisibilityIfPresent(TradeElement(panel, "TradeSendCaption"), false);
                SetTextIfPresent(panel, "TradeStatus", Text("Angebot übermittelt. Die Entscheidung des Empfängers steht aus.", "Offer delivered. Awaiting the recipient's decision."));
            }

            // This is the FINAL writer for the caption, after the trade view
            // already set its preview text. Never overwrite its fail-closed
            // 'no send' message when the native goods boundary is closed.
            if (!nativeAvailable)
                SetTextIfPresent(panel, "TradeSendCaption",
                    Text("VORSCHAU – NICHT SENDEN", "PREVIEW ONLY – NO SEND"));
            else if (editing)
                SetTextIfPresent(panel, "TradeSendCaption", Text("GEGENANGEBOT SENDEN", "SEND COUNTEROFFER"));
            else
                SetTextIfPresent(panel, "TradeSendCaption", Text("HANDELSANFRAGE SENDEN", "SEND TRADE REQUEST"));
        }
    }
}
