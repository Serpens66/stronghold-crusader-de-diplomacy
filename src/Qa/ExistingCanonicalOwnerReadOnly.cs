// existing canonical owner PRIVATE TEST ONLY: read the existing canonical owner from a real
// original-game save WITHOUT creating another CanonicalTradeReducer instance.
// Never adopt/modify archive, mailbox, inventory, consent, original native
// resources, Steam state or current SaveData owner.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // Entry requires original single-human engine section and _gate.
        // This is a witness of a saved owner, NOT of normal release authority,
        // exact native durability, a current trade quote or human acceptance.
        private bool ObserveExistingCanonicalOwnerReadOnly(out string note)
        {
            note = "status=existing_owner_readonly_refused|reason=unavailable";
            if (!Monitor.IsEntered(_gate) || !IsNativeDiplomacySectionHeld)
            {
                note="status=existing_owner_readonly_refused|reason=original_engine_or_feature_monitor_missing";
                return false;
            }
            if (!_mapActive || _mapDisabled || _mapBootstrapPending ||
                !_privateSaveScope || _mpConsentMode || _mpQuarantined ||
                _tradeFaulted || _goodsTransfersUnavailable ||
                _canonicalStopped || _pendingPackage != null ||
                _pendingTeamTransition != null ||
                _nativeTransferProbe != null)
            {
                note="status=existing_owner_readonly_refused|reason=private_original_owner_scope_or_quarantine";
                return false;
            }
            if (_canonicalTransition != null)
            {
                note="status=existing_owner_readonly_refused|reason=live_prepared_native_transition";
                return false;
            }
            if (_canonical == null && _canonicalSaved == null)
            {
                note="status=existing_owner_readonly_refused|reason=no_preexisting_canonical_owner";
                return false;
            }
            if (_roster == null || _roster.Count < 2 || _roster.Count > 8 ||
                _localPlayer < 1 || _localPlayer > 8 ||
                String.IsNullOrEmpty(_mapFingerprint) ||
                String.IsNullOrEmpty(_rosterFingerprint) ||
                _sessionGeneration <= 0 || _mapEpoch <= 0)
            {
                note="status=existing_owner_readonly_refused|reason=original_roster_or_map_generation";
                return false;
            }
            try
            {
                // All bytes are cloned from the one existing owner, under its
                // existing original monitor. Never call CanonicalSaveBytes:
                // it can Observe/Rebase the original saved mailbox.
                byte[] saved = _canonicalSaved == null ?
                    null : (byte[])_canonicalSaved.Clone();
                byte[] live = _canonical == null ?
                    null : _canonical.SaveBytes();
                if (saved != null && live != null &&
                    !ExistingSaveReceiptGate.ExactBytes(saved,live))
                {
                    // Two diverging views are NOT silently reconciled or
                    // published. This must remain a safe negative witness.
                    note="status=existing_owner_readonly_refused|reason=serialized_live_canonical_divergence";
                    return false;
                }
                byte[] selected = live ?? saved;
                if (selected == null || selected.Length < 64 ||
                    selected.Length > 512 * 1024)
                {
                    note="status=existing_owner_readonly_refused|reason=canonical_payload_bounds";
                    return false;
                }
                // Decoder independently verifies embedded SHA256 checksum,
                // canonical schema, record IDs, mailbox and pending native
                // state. It creates a *detached data record*, not a Reducer.
                CanonicalModel model = CanonicalTradeCodec.Decode(selected);
                if (model == null || model.Archive == null ||
                    model.Mailbox == null ||
                    model.MatchScope !=
                        MapGuid(_mapFingerprint,_rosterFingerprint) ||
                    model.MatchTerm != SinglePlayerTerm ||
                    !String.Equals(model.Archive.MapFingerprint,
                        _mapFingerprint,StringComparison.Ordinal) ||
                    !String.Equals(model.Archive.RosterFingerprint,
                        _rosterFingerprint,StringComparison.Ordinal))
                {
                    note="status=existing_owner_readonly_refused|reason=canonical_saved_map_or_match_scope";
                    return false;
                }
                int actualRosterMask=0;
                foreach(int player in _roster)
                {
                    if (player < 1 || player > 8 ||
                        (actualRosterMask & (1 << (player - 1))) != 0)
                    {
                        note="status=existing_owner_readonly_refused|reason=invalid_original_roster_members";
                        return false;
                    }
                    actualRosterMask |= 1 << (player - 1);
                }
                if (model.RosterMask != actualRosterMask ||
                    model.HumanMask != (1 << (_localPlayer - 1)) ||
                    model.Day < 1 || model.Revision < 1 ||
                    model.Archive.NextId < TradeHistoryArchive.MinimumTradeId)
                {
                    note="status=existing_owner_readonly_refused|reason=canonical_original_roster_or_local_human";
                    return false;
                }
                int preparing = model.Archive.Records.Count(x =>
                    x.State == TradeDisposition.PreparingNative);
                int committed = model.Archive.Records.Count(x =>
                    x.State == TradeDisposition.Committed);
                int pendingCounter = model.Archive.Records.Count(x =>
                    x.State == TradeDisposition.PendingCounter);
                int waitingIntents = model.Mailbox.Intents.Count(x =>
                    x.State == TradeIntentStatus.Waiting);
                int processingIntents = model.Mailbox.Intents.Count(x =>
                    x.State == TradeIntentStatus.Processing);
                int pendingHumanOffers = model.HumanOffers.Count(x =>
                    x.State == CanonicalHumanConsentState.Pending);
                // Report recovery/quarantine facts honestly. A positive
                // READ-ONLY readback does NOT grant native commit permission.
                string digest;
                using(SHA256 sha = SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(selected))
                        .Replace("-","").ToLowerInvariant();
                note = "status=existing_owner_readonly_verified" +
                    "|source="+(live == null ?
                        "existing_serialized_owner" :
                        (saved == null ?
                            "existing_live_owner" :
                            "matched_serialized_and_live_owner")) +
                    "|canonical_sha256="+digest +
                    "|canonical_bytes="+selected.Length +
                    "|canonical_revision="+model.Revision +
                    "|canonical_saved_day="+model.Day +
                    "|original_current_day="+Math.Max(1,_lastProcessedDay) +
                    "|canonical_saved_tick="+model.LastSharedTick +
                    "|original_current_tick="+ReadSimulationTick() +
                    "|original_map_roster_and_solo_human_match=true" +
                    "|original_map_generation="+_sessionGeneration +
                    "|archive_records="+model.Archive.Records.Count +
                    "|archive_committed="+committed +
                    "|archive_pending_counter="+pendingCounter +
                    "|archive_preparing_native="+preparing +
                    "|archive_pending_native_id_present="+
                        (model.Archive.PendingNativeId!=0) +
                    "|mailbox_clock="+model.Mailbox.Clock +
                    "|mailbox_waiting="+waitingIntents +
                    "|mailbox_processing="+processingIntents +
                    "|human_offers_total="+model.HumanOffers.Count +
                    "|human_offers_pending="+pendingHumanOffers +
                    "|canonical_quarantined="+model.Quarantined +
                    "|native_recovery_or_processing_present="+
                        (model.Archive.PendingNativeId != 0 ||
                         preparing != 0 || processingIntents != 0) +
                    "|duplicate_canonical_reducer_created=false" +
                    "|canonical_gameplay_owner_unchanged=true" +
                    "|original_cpu_diplomatic_chore_proven=false" +
                    "|human_ui_consent_observed=false" +
                    "|native_goods_writes=0" +
                    "|normal_native_goods_authorized=false" +
                    "|steam_mp_authorized=false";
                return true;
            }
            catch(Exception e)
            {
                note="status=existing_owner_readonly_refused|reason=validated_saved_canonical_"+e.GetType().Name;
                return false;
            }
        }
    }
}
#endif
