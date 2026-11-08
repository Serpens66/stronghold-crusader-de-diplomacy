// A modal trade window owns its own partner. The vanilla allies
// portrait can change independently without redirecting a pending offer.
// Keep at most seven bounded, map-local draft states (one per other lord).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private sealed class TradePartnerDraft
        {
            // Transient MP editor identity, kept in the existing bounded partner draft.
            internal ulong MpCounterOfferId;
            internal int Wanted = 1, Offered = 0, Quantity = 10;
            internal TradeOfferGenerosity Tone = TradeOfferGenerosity.Fair;
            internal string AmountText = "10", Status = string.Empty;
        }

        private readonly Dictionary<int, TradePartnerDraft> _partnerDrafts = new Dictionary<int, TradePartnerDraft>();
        private void ClearPartnerDrafts()
        {
            _partnerDrafts.Clear();
        }

        private TradePartnerDraft DraftFor(int partner)
        {
            if (partner < 1 || partner > 8 || partner == _localPlayer)
                throw new ArgumentOutOfRangeException("partner");
            TradePartnerDraft draft;
            if (!_partnerDrafts.TryGetValue(partner, out draft))
            {
                if (_partnerDrafts.Count >= 7)
                    throw new InvalidOperationException("Too many independent trade drafts");
                draft = new TradePartnerDraft();
                _partnerDrafts.Add(partner, draft);
            }

            return draft;
        }

        private void RememberDraft(int partner, object panel)
        {
            if (partner < 1 || partner > 8 || partner == _localPlayer || !_tradeTab)
                return;
            var d = DraftFor(partner);
            d.Wanted = _tradeWantedIndex;
            d.Offered = _tradeOfferedIndex;
            d.Quantity = _tradeRequestedAmount;
            d.Tone = _tradeGenerosity;
            object textbox = TradeElement(panel, "TradeAmountInput");
            string typed = ReadControlProperty(textbox, "Text") as string;
            // A temporarily invalid text value belongs to the active draft only.
            // It must never be submitted using another partner's last valid quantity.
            if (typed != null && typed.Length <= 24)
                d.AmountText = typed;
            else
                d.AmountText = d.Quantity.ToString(CultureInfo.InvariantCulture);
            object slider = TradeElement(panel, "TradeGenerositySlider");
            object sliderValue = ReadControlProperty(slider, "Value");
            if (sliderValue is int || sliderValue is double || sliderValue is float)
            {
                TradeOfferGenerosity tone;
                if (TrySliderTier(Convert.ToDouble(sliderValue, CultureInfo.InvariantCulture), out tone))
                    d.Tone = tone;
            }

            d.Status = string.IsNullOrEmpty(_tradeStatus) ? string.Empty : _tradeStatus.Length > 240 ? _tradeStatus.Substring(0, 240) : _tradeStatus;
        }

        private void LoadDraft(int partner, object panel)
        {
            var d = DraftFor(partner);
            _tradeWantedIndex = d.Wanted;
            _tradeOfferedIndex = d.Offered;
            _tradeRequestedAmount = d.Quantity;
            _tradeGenerosity = d.Tone;
            _tradeStatus = d.Status;
            _draftPartner = partner;
            _statusPartner = partner;
            _lastIconSelection = -1;
            _draftControlsReady = false;
            _draftControlsOwner = null;
            string pending = PendingTradeStatus(partner);
            if (!string.IsNullOrEmpty(pending))
                _tradeStatus = pending;
            InitializeDraftControls(panel);
        }

        private string CurrentTextAmount()
        {
            TradePartnerDraft d;
            return _draftPartner >= 1 && _partnerDrafts.TryGetValue(_draftPartner, out d) ? d.AmountText : _tradeRequestedAmount.ToString(CultureInfo.InvariantCulture);
        }

        private bool FindNextTradingPartner(int current, bool forward, out int next)
        {
            next = 0;
            if (!_mapActive || _mapDisabled || current < 1 || current > 8)
                return false;
            // The direction and wrap are deterministic; do not expose an unknown AI
            // or a forbidden foreign player as an executable trade counterpart.
            var all = _roster.Where(p => p >= 1 && p <= 8 && p != _localPlayer).OrderBy(p => p).ToArray();
            if (all.Length == 0)
                return false;
            int start = Array.IndexOf(all, current);
            if (start < 0)
                return false;
            for (int step = 1; step <= all.Length; step++)
            {
                int index = (start + (forward ? step : -step) + all.Length * 2) % all.Length;
                int candidate = all[index];
                string reason;
                if (candidate != current && CanTradeWith(candidate, out reason))
                {
                    next = candidate;
                    return true;
                }
            }

            return false;
        }

        private void SwitchTradePartner(int partner, object panel)
        {
            if (!_mapActive || _mapDisabled || partner < 1 || partner > 8 || partner == _localPlayer || !_roster.Contains(partner))
                throw new InvalidOperationException("Trade counterpart is not in this match");
            string permission;
            if (!CanTradeWith(partner, out permission))
                throw new InvalidOperationException(permission);
            if (_tradeTab && _draftPartner >= 1)
                RememberDraft(_draftPartner, panel);
            _tradeTab = true;
            _overviewVisible = false;
            LoadDraft(partner, panel);
        }
    }
}
