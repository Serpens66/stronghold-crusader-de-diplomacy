// exact existing Noesis trade button route, separate from
// inactive legacy diplomacy and entirely separate from native stock gates.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private Bytes _pendingLocalIntent;
        private ulong _localNonce, _localSequence, _localEpoch;
        private byte _localActor, _localRecipient;
        // MUST assign this to the EXISTING official GameNetworkAPI sender to
        // host, or verified host-local inbox. True means QUEUED only, never signed.
        private Func<Bytes, byte, bool> _existingEngineSubmitIntent;
        private void ResetPendingLocalTransport()
        {
            _pendingLocalIntent = null;
            _localNonce = 0;
            _localSequence = 0;
            _localEpoch = 0;
            _localActor = 0;
            _localRecipient = 0;
        }

        private void ClearPendingLocalUiIntent()
        {
            ResetPendingLocalTransport();
            _consentCounterDrafts.Clear();
        }

        private void CompletePendingLocalUiIntent()
        {
            byte completedPartner = _localRecipient;
            ResetPendingLocalTransport();
            // Acknowledging A must not discard an unfinished counteroffer to B.
            _consentCounterDrafts.Remove(completedPartner);
        }

        // Counter edits belong to the exact immutable incoming quote, not merely
        // its partner. Expiry/replacement must require a fresh explicit decision.
        private bool BeginMpCounterDraft(int partner)
        {
            var incoming = FindHumanOffer(partner, true);
            if (incoming == null || incoming.Depth >= 8)
                return false;
            DraftFor(partner).MpCounterOfferId = incoming.Id;
            _consentCounterDrafts.Add(partner);
            return true;
        }

        private bool HasCurrentMpCounterDraft(int partner, CanonicalHumanOffer incoming)
        {
            TradePartnerDraft draft;
            bool known = _partnerDrafts.TryGetValue(partner, out draft);
            if (!_consentCounterDrafts.Contains(partner))
            {
                if (known)
                    draft.MpCounterOfferId = 0;
                return false;
            }

            if (known && incoming != null && draft.MpCounterOfferId == incoming.Id)
                return true;
            _consentCounterDrafts.Remove(partner);
            if (known)
                draft.MpCounterOfferId = 0;
            return false;
        }

        private bool MpUiPermitted(int partner)
        {
            string reason;
            return _mpConsentMode && _latestOriginalFrame != null && _canonical != null && _signed != null && _signed.Live && _signed.Ready && !_mpQuarantined && !OriginalCaptureRevocationPending && _ownerGeneration == _mapEpoch && CanPresentDiplomacyUi && partner >= 1 && partner <= 8 && partner != _latestOriginalFrame.LocalOriginalHumanSlot && _latestOriginalFrame.TryAuthoritativeHumanActor(_latestOriginalFrame.LocalOriginalHumanSlot, _latestOriginalFrame.OriginalLocalSteamId) && _latestOriginalFrame.TryConsentPair(_latestOriginalFrame.LocalOriginalHumanSlot, (byte)partner, out reason);
        }

        private CanonicalHumanOffer FindHumanOffer(int partner, bool incoming)
        {
            if (!MpUiPermitted(partner))
                return null;
            var m = CanonicalTradeCodec.Decode(_canonical.SaveBytes());
            byte me = _latestOriginalFrame.LocalOriginalHumanSlot;
            return _canonical.HumanOffersFor(me).Where(x => x.State == CanonicalHumanConsentState.Pending && x.ExpiresDay >= m.Day && x.ExpiresClock >= m.Mailbox.Clock && (incoming ? (x.From == partner && x.To == me) : (x.From == me && x.To == partner))).OrderByDescending(x => x.Id).FirstOrDefault();
        }

        private bool OriginalMpReadDraft(object panel, out ushort give, out ushort want, out int count, out int literal)
        {
            give = want = 0;
            count = literal = 0;
            string reason;
            if (!ReadDraftControls(panel, out reason) || _tradeWantedIndex < 0 || _tradeOfferedIndex < 0 || _tradeWantedIndex >= TradeAllowedGoods.Length || _tradeOfferedIndex >= TradeAllowedGoods.Length)
                return false;
            give = (ushort)TradeAllowedGoods[_tradeOfferedIndex];
            want = (ushort)TradeAllowedGoods[_tradeWantedIndex];
            count = _tradeRequestedAmount;
            if (give == want || count < 1 || count > 100000)
                return false;
            var market = new ObservedTradeMarketSnapshot(_latestOriginalFrame.Market());
            literal = IndicativeQuote(want, give, count, _tradeGenerosity, market.Find(want), market.Find(give));
            return literal >= 1 && literal <= 100000;
        }

        private bool SubmitMpHumanDraft(string verb, int partner, object panel)
        {
            // While an exact intent awaits its shared acknowledgement, another UI
            // verb/partner must not be reported as sent by requeueing the old action.
            // The existing owner pump alone retries the retained immutable packet.
            if (_pendingLocalIntent != null)
                return false;
            if (!MpUiPermitted(partner))
                return false;
            var current = CanonicalTradeCodec.Decode(_canonical.SaveBytes());
            byte me = _latestOriginalFrame.LocalOriginalHumanSlot;
            if (current.InputEpoch < 1 || current.InputSequence[me] >= ulong.MaxValue - 2 || current.InputNonce[me] >= ulong.MaxValue - 2)
                return false;
            var recipient = _latestOriginalFrame.Players().Single(x => x.Slot == partner);
            if (!recipient.Human)
                return SubmitMpAiDraft(verb, partner, panel, current, me);
            var incoming = FindHumanOffer(partner, true);
            var outgoing = FindHumanOffer(partner, false);
            bool wasCounterDraft = _consentCounterDrafts.Contains(partner);
            bool editing = HasCurrentMpCounterDraft(partner, incoming);
            if (verb == "Send" && wasCounterDraft && !editing)
                return false;
            CanonicalHumanOffer reference = null;
            HumanTradeActionKind action;
            ushort give, want;
            int qty, literal;
            if (verb == "Send" && editing)
            {
                if (incoming == null)
                    return false;
                reference = incoming;
                action = HumanTradeActionKind.CounterHuman;
                if (!OriginalMpReadDraft(panel, out give, out want, out qty, out literal))
                    return false;
            }
            else if (verb == "Send")
            {
                if (incoming != null || outgoing != null)
                    return false;
                action = HumanTradeActionKind.ProposeHuman;
                if (!OriginalMpReadDraft(panel, out give, out want, out qty, out literal))
                    return false;
            }
            else if (verb == "Accept" || verb == "Decline")
            {
                if (incoming == null)
                    return false;
                reference = incoming;
                action = verb == "Accept" ? HumanTradeActionKind.AcceptHuman : HumanTradeActionKind.DeclineHuman;
                give = reference.OfferedGood;
                want = reference.RequestedGood;
                qty = reference.RequestedAmount;
                literal = reference.OfferedAmount;
            }
            else if (verb == "Withdraw")
            {
                if (outgoing == null)
                    return false;
                reference = outgoing;
                action = HumanTradeActionKind.WithdrawHuman;
                give = reference.OfferedGood;
                want = reference.RequestedGood;
                qty = reference.RequestedAmount;
                literal = reference.OfferedAmount;
            }
            else
            {
                return false;
            }

            byte tier = reference == null || (action == HumanTradeActionKind.CounterHuman) ? (byte)_tradeGenerosity : reference.Tier;
            var proposed = new HumanTradeActionEnvelope(_signed.Descriptor.Id, 0, 0, me, (byte)partner, action, tier, give, want, qty, literal, reference == null ? 0 : reference.Id, reference == null ? 0 : reference.RootId, reference == null ? 0 : reference.OfferedAmount, reference == null ? 0 : reference.IssuedDay, reference == null ? 0 : reference.ExpiresDay, reference == null ? (ushort)0 : reference.OfferedGood, reference == null ? (ushort)0 : reference.RequestedGood, reference == null ? 0 : reference.RequestedAmount, current.InputEpoch, current.InputSequence[me] + 1, current.InputNonce[me] + 1);
            return FreezeAndQueueUiIntent(proposed);
        }

        private bool FreezeAndQueueUiIntent(HumanTradeActionEnvelope proposed)
        {
            _pendingLocalIntent = proposed.ClientIntentPacket();
            _localActor = proposed.ActorSlot;
            _localRecipient = proposed.RecipientSlot;
            _localEpoch = proposed.Epoch;
            _localSequence = proposed.ActorSequence;
            _localNonce = proposed.ActorNonce;
            return QueueFrozenUiIntent();
        }

        private bool QueueFrozenUiIntent()
        {
            if (_pendingLocalIntent == null || _existingEngineSubmitIntent == null)
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            using (lease)
                return _existingEngineSubmitIntent(new Bytes(_pendingLocalIntent.Copy()), _localActor);
        }

        private TradeRecord PendingAiCounter(int partner)
        {
            if (_archive == null || _latestOriginalFrame == null)
                return null;
            return _archive.Records.Where(x => x.Requester == _latestOriginalFrame.LocalOriginalHumanSlot && x.Recipient == partner && !x.AiToAi && x.State == TradeDisposition.PendingCounter && x.ExpiryDay >= CanonicalTradeCodec.Decode(_canonicalSaved).Day).OrderByDescending(x => x.Id).FirstOrDefault();
        }

        private bool SubmitMpAiDraft(string verb, int partner, object panel, CanonicalModel current, byte me)
        {
            var counter = PendingAiCounter(partner);
            HumanTradeActionKind action;
            ushort give, want;
            int count, literal;
            if (verb == "Send")
            {
                if (counter != null || !OriginalMpReadDraft(panel, out give, out want, out count, out literal))
                    return false;
                action = HumanTradeActionKind.ProposeAi;
            }
            else if ((verb == "Accept" || verb == "Decline") && counter != null)
            {
                action = verb == "Accept" ? HumanTradeActionKind.AcceptAiCounter : HumanTradeActionKind.DeclineAiCounter;
                give = counter.OfferedGood;
                want = counter.RequestedGood;
                count = counter.RequestedAmount;
            }
            else
                return false;
            var proposed = new HumanTradeActionEnvelope(_signed.Descriptor.Id, 0, 0, me, (byte)partner, action, counter == null ? (byte)_tradeGenerosity : counter.Generosity, give, want, count, 0, counter == null ? 0 : counter.Id, counter == null ? 0 : counter.RootId, counter == null ? 0 : counter.OfferedAmount, counter == null ? 0 : counter.IssuedDay, counter == null ? 0 : counter.ExpiryDay, 0, 0, 0, current.InputEpoch, current.InputSequence[me] + 1, current.InputNonce[me] + 1);
            return FreezeAndQueueUiIntent(proposed);
        }

        // Invoked by the EXISTING allies-button prefix ONLY BEFORE the solo
        // _mapActive guard, never as a new Harmony/Noesis event subscription.
        private bool HandleMpTradeOnlyButton(object panel, string param)
        {
            if (!_mpConsentMode || string.IsNullOrEmpty(param) || !param.StartsWith(TradeCommandPrefix, StringComparison.Ordinal))
                return false;
            var cmd = param.Substring(TradeCommandPrefix.Length);
            if (cmd == "Close")
            {
                CloseTradeWindow(panel);
                return true;
            }

            if (!CanPresentDiplomacyUi || _latestOriginalFrame == null)
                return true;
            int partner = cmd == "Open" ? SelectedPlayer(panel) : _draftPartner;
            if (!MpUiPermitted(partner))
            {
                _tradeStatus = Text("Menschlicher Handelspartner oder Originalzustand nicht bestätigt.", "Human trade counterpart or original game state not verified.");
                return true;
            }

            if (cmd == "Open")
            {
                if (_tradeTab && _draftPartner >= 1)
                    RememberDraft(_draftPartner, panel);
                _draftPartner = partner;
                _tradeTab = true;
                LoadDraft(partner, panel);
                SetVisibilityIfPresent(TradeElement(panel, "TradeModal"), true);
                SetVisibilityIfPresent(TradeElement(panel, "TradePanel"), true);
            }
            else if (cmd == "Partner:Previous" || cmd == "Partner:Next")
            {
                var peers = _latestOriginalFrame.Players().Select(x => (int)x.Slot).Where(x => MpUiPermitted(x)).OrderBy(x => x).ToArray();
                int index = Array.IndexOf(peers, partner);
                if (index >= 0 && peers.Length > 1)
                {
                    RememberDraft(partner, panel);
                    partner = peers[(index + (cmd == "Partner:Next" ? 1 : peers.Length - 1)) % peers.Length];
                    LoadDraft(partner, panel);
                }
            }
            else if (cmd == "Counter")
            {
                BeginMpCounterDraft(partner);
            }
            else if (cmd == "Offered:Auto" || cmd == "Wanted:Auto")
            {
                // Human signed literal terms must be explicit and cannot choose Auto.
                _tradeStatus = Text("Für menschliche Angebote beide Waren ausdrücklich auswählen.", "Choose both goods explicitly for a human offer.");
            }
            else if (cmd.StartsWith("Wanted:", StringComparison.Ordinal))
            {
                int index;
                if (TryIconSelection(cmd, "Wanted:", out index))
                    _tradeWantedIndex = index;
            }
            else if (cmd.StartsWith("Offered:", StringComparison.Ordinal))
            {
                int index;
                if (TryIconSelection(cmd, "Offered:", out index))
                    _tradeOfferedIndex = index;
            }
            else if (cmd.StartsWith("Amount:Set:", StringComparison.Ordinal))
            {
                int count;
                if (TryQuantityPreset(cmd, out count))
                    _tradeRequestedAmount = count;
            }
            else if (cmd == "Amount:Apply")
            {
                string reason;
                if (!ReadDraftControls(panel, out reason))
                    _tradeStatus = reason;
            }
            else if (cmd == "Tier:0" || cmd == "Tier:1" || cmd == "Tier:2")
            {
                int parsed = cmd[cmd.Length - 1] - '0';
                SetDraftTier(panel, (TradeOfferGenerosity)parsed);
            }
            else if (cmd == "Send" || cmd == "Accept" || cmd == "Decline" || cmd == "Withdraw")
            {
                bool queued = SubmitMpHumanDraft(cmd, partner, panel);
                _tradeStatus = queued ? Text("Eure Handelsanfrage wird übermittelt.", "Your trade request is being delivered.") : Text("Anfrage nicht bestätigt; gleicher Entwurf bleibt erhalten.", "Intent not confirmed; exact draft retained for retry.");
            }
            else
            {
                _tradeStatus = Text("Keine native Handelsaktion erlaubt.", "No native trade action permitted.");
            }

            RefreshMpConsentUi(panel, partner);
            return true;
        }

        private void RefreshMpConsentUi(object panel, int partner)
        {
            var incoming = FindHumanOffer(partner, true);
            var outgoing = FindHumanOffer(partner, false);
            var aiCounter = PendingAiCounter(partner);
            bool pairWaiting = _mailbox != null && _mailbox.HasPending(_localPlayer, partner);
            bool waiting = _pendingLocalIntent != null;
            bool editing = HasCurrentMpCounterDraft(partner, incoming);
            SetTextIfPresent(panel, "TradeTitle", Text("Handel mit Spieler ", "Trade with player ") + partner);
            SetTextIfPresent(panel, "TradeEconomyPreview", Text("Nur Zustimmung; Warentransfer nicht freigegeben.", "Consent only; goods transfer is unavailable."));
            foreach (string verb in new[]
            {
                "Accept",
                "Decline"
            }

            )
            {
                bool show = (incoming != null || aiCounter != null) && !editing;
                SetVisibilityIfPresent(TradeElement(panel, "TradeCounter" + verb), show);
                SetVisibilityIfPresent(TradeElement(panel, "TradeCounter" + verb + "Label"), show);
            }

            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentCounterLabel"), incoming != null && !editing && incoming.Depth < 8);
            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentWithdrawLabel"), outgoing != null);
            SetVisibilityIfPresent(TradeElement(panel, "TradeSend"), incoming == null && outgoing == null && aiCounter == null || editing);
            SetVisibilityIfPresent(TradeElement(panel, "TradeSendCaption"), incoming == null && outgoing == null && aiCounter == null || editing);
            SetTextIfPresent(panel, "TradeSendCaption", editing ? Text("GEGENANGEBOT SENDEN", "SEND COUNTEROFFER") : Text("HANDELSANFRAGE SENDEN", "SEND TRADE REQUEST"));
            if (incoming != null && !editing)
            {
                SetTextIfPresent(panel, "TradePreview", incoming.RequestedAmount + " " + GoodName(incoming.RequestedGood, incoming.RequestedAmount) + " -> " + incoming.OfferedAmount + " " + GoodName(incoming.OfferedGood, incoming.OfferedAmount));
            }
            else if (aiCounter != null)
            {
                SetTextIfPresent(panel, "TradePreview", aiCounter.OfferedAmount + " " + GoodName(aiCounter.OfferedGood, aiCounter.OfferedAmount) + " -> " + aiCounter.RequestedAmount + " " + GoodName(aiCounter.RequestedGood, aiCounter.RequestedAmount));
            }
            else
            {
                if (_tradeWantedIndex >= 0 && _tradeWantedIndex < TradeAllowedGoods.Length)
                    SetTextIfPresent(panel, "TradeWantedValue", GoodName(TradeAllowedGoods[_tradeWantedIndex], _tradeRequestedAmount));
                if (_tradeOfferedIndex >= 0 && _tradeOfferedIndex < TradeAllowedGoods.Length)
                    SetTextIfPresent(panel, "TradeOfferedValue", GoodName(TradeAllowedGoods[_tradeOfferedIndex], 1));
                SetTextIfPresent(panel, "TradePreview", Text("Verbindliche Waren und Menge auswählen.", "Choose literal goods and amount."));
            }

            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentCounter"), incoming != null && !editing);
            SetVisibilityIfPresent(TradeElement(panel, "TradeConsentWithdraw"), outgoing != null);
            object send = TradeElement(panel, "TradeSend");
            if (send != null)
                SetProperty(send, "IsEnabled", MpUiPermitted(partner) && !pairWaiting && !waiting && ((incoming == null && outgoing == null && aiCounter == null) || editing));
            foreach (var cmd in new[]
            {
                "Accept",
                "Decline"
            }

            )
            {
                var element = TradeElement(panel, "TradeCounter" + cmd);
                if (element != null)
                    SetProperty(element, "IsEnabled", (incoming != null || aiCounter != null) && !waiting && !pairWaiting);
            }

            SetTextIfPresent(panel, "TradeStatus", waiting ? Text("Anfrage wartet auf die geordnete Bestätigung.", "Intent awaiting ordered confirmation.") : outgoing != null ? Text("Angebot bestätigt. Entscheidung des anderen Spielers ausstehend.", "Offer confirmed; awaiting the other player's decision.") : incoming != null ? Text("Ein Angebot wartet auf eure Entscheidung.", "An offer is waiting for your decision.") : pairWaiting ? Text("Anfrage wartet auf bestätigte Lagerkapazität.", "Request waiting for verified storage capacity.") : _tradeStatus);
            RefreshVanillaIconSelection(panel);
        }

        // Call ONLY after the single Format3 owner has confirmed the 
        // signed Chore highwater in actual existing Schema19 SaveData.
        private void AcknowledgeUiFromOriginalSavedCanonical()
        {
            if (_pendingLocalIntent == null || _canonical == null)
                return;
            CanonicalModel m = CanonicalTradeCodec.Decode(_canonical.SaveBytes());
            if (m.InputEpoch == _localEpoch && _localEpoch > 0 && m.InputSequence[_localActor] >= _localSequence && m.InputNonce[_localActor] >= _localNonce)
                CompletePendingLocalUiIntent();
        }
    }
}
