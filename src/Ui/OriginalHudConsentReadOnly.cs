// original hud – normal Release + QA original Noesis UI observation ONLY.
// A method invocation from a QA driver is NOT a real physical HUD click.
// Only an existing, current offer that was actually shown on an original
// Noesis panel may produce a diagnostic. This never enqueues accepted
// input, never changes native goods and never grants a commit permission.
using System;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private long _hudObservationMapGeneration=-1;
        private ulong _hudObservationQuoteId;
        private int _hudObservationFrame=-1;

        private void ObserveHudConsentCandidate(
            CanonicalHumanOffer existingOffer,int originalPartner,object noesisPanel)
        {
            if (existingOffer==null || noesisPanel==null ||
                !_mapActive || _mapDisabled || _mapBootstrapPending ||
                _mpConsentMode || _mpBootstrapRequested || _mpQuarantined ||
                _goodsTransfersUnavailable || _inventoryRestorePending ||
                _tradeFaulted || _nativeTransferProbe!=null ||
                _pendingPackage!=null || _pendingTeamTransition!=null ||
                _localPlayer<1 || _localPlayer>8 || _roster==null ||
                !_roster.Contains(_localPlayer) ||
                _sessionGeneration<1 || _mapEpoch<1 ||
                _mapEpoch!=_sessionGeneration ||
                existingOffer.Id<1001 ||
                existingOffer.From!=originalPartner ||
                existingOffer.To!=_localPlayer ||
                existingOffer.State!=CanonicalHumanConsentState.Pending ||
                existingOffer.OfferedGood==existingOffer.RequestedGood ||
                existingOffer.RequestedAmount<1 ||
                existingOffer.OfferedAmount<1 ||
                !CanPresentDiplomacyUi)
                return;
            // A pending offer and an actual Noesis original callback are only
            // UI provenance hints. Mouse events can be synthesized by
            // operating systems, and no private synthetic test can prove a
            // PHYSICAL original human. Never derive Native authority here.
            int frame=Time.frameCount;
            if (_hudObservationMapGeneration==_sessionGeneration &&
                _hudObservationQuoteId==existingOffer.Id &&
                _hudObservationFrame==frame)
                return;
            _hudObservationMapGeneration=_sessionGeneration;
            _hudObservationQuoteId=existingOffer.Id;
            _hudObservationFrame=frame;
            try
            {
            // The exact pinned Unity public ABI has no directly linked
            // UnityEngine.Input class; focused Noesis callback is not
            // a physical human click or a trusted consent.
            bool focusedUiCallback=Application.isFocused;
            Log("original_hud", 
                "status=original_Noesis_accept_callback_observed_only"+
                "|same_original_map_generation=true"+
                "|pending_quote_id="+existingOffer.Id+
                "|original_partner_slot="+originalPartner+
                "|original_local_human_slot="+_localPlayer+
                "|unity_input_frame="+frame+
                "|noesis_ui_focused="+focusedUiCallback+
                "|physical_human_input_attested=false"+
                "|manual_human_identity_authenticated=false"+
                "|original_save_cold_for_this_quote_unproven=true"+
                "|no_accepted_consent_queued_by_observer=true"+
                "|native_goods_authorized=false"+
                "|steam_MP_authorized=false");
            }
            catch(Exception) { /* observer must never affect original UI */ }
        }
    }
}
