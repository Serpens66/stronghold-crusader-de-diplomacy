// visible trade panel original Noesis trade-panel VISIBILITY witness, normal+QA build.
// Only records a true visible original HUD panel with the exact current
// partner, a real Noesis bound preview and disabled Send button. Neither
// a visual panel nor an automated GUI event proves a human consent.
// No native goods/SaveData writes, Steam network, new canonical Reducer
// or manual-authentication claim. Private headless original LOAD_PROBE
// must not be mislabeled as a visible human HUD session.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private long _visiblePanelGeneration = -1;
        private int _visiblePanelPartner;

        private void ObserveActualVisibleTradePanel(
            object originalPanel, int originalPartner, bool nativeAuthorized)
        {
            if (originalPanel == null || nativeAuthorized ||
                !_mapActive || _mapDisabled || _mapBootstrapPending ||
                _mpConsentMode || _mpBootstrapRequested || _mpQuarantined ||
                _tradeFaulted || _nativeTransferProbe != null ||
                !_tradeTab || !CanPresentDiplomacyUi ||
                _roster == null || _localPlayer < 1 || _localPlayer > 8 ||
                originalPartner < 1 || originalPartner > 8 ||
                originalPartner == _localPlayer ||
                !_roster.Contains(_localPlayer) ||
                !_roster.Contains(originalPartner) ||
                _sessionGeneration < 1 || _mapEpoch != _sessionGeneration ||
                _visiblePanelGeneration == _sessionGeneration &&
                _visiblePanelPartner == originalPartner)
                return;
            try
            {
                object content = FindElement(originalPanel, "TradePanel");
                object economy = FindElement(originalPanel, "TradeEconomyPreview");
                object send = FindElement(originalPanel, "TradeSend");
                object caption = FindElement(originalPanel, "TradeSendCaption");
                object quote = FindElement(originalPanel, "TradePreview");
                if (content == null || economy == null || send == null ||
                    caption == null || quote == null)
                    return;
                object visible = ReadControlProperty(content, "Visibility");
                object sendEnabled = ReadControlProperty(send, "IsEnabled");
                object actualPreview = ReadControlProperty(economy, "Text");
                object actualCaption = ReadControlProperty(caption, "Text");
                if (visible == null || visible.ToString() != "Visible" ||
                    !(sendEnabled is bool) || (bool)sendEnabled ||
                    !(actualPreview is string) ||
                    String.IsNullOrWhiteSpace((string)actualPreview) ||
                    !(actualCaption is string) ||
                    !((string)actualCaption).Contains("VORSCHAU") &&
                    !((string)actualCaption).Contains("PREVIEW"))
                    return;
                // A successful witness cannot become a writer. No save,
                // goods, UI event injection, or quote activation occurs.
                _visiblePanelGeneration = _sessionGeneration;
                _visiblePanelPartner = originalPartner;
                Log("visible_noesis_trade_panel",
                    "status=original_trade_panel_current_visible_readonly" +
                    "|original_map_generation=" + _sessionGeneration +
                    "|original_partner_slot=" + originalPartner +
                    "|original_local_human_slot=" + _localPlayer +
                    "|original_ui_trade_preview_text_present=true" +
                    "|normal_native_send_button_disabled=true" +
                    "|explicit_preview_only_caption=true" +
                    "|actual_human_click_not_proven=true" +
                    "|observer_native_goods_writes=0" +
                    "|normal_native_goods_authorized=false" +
                    "|steam_multiplayer_authorized=false");
            }
            catch (Exception)
            {
                // Diagnostic must never disable a live original HUD.
            }
        }
    }
}
