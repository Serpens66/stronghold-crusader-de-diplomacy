// real existing feature-owner glue. Legacy diplomacy stays inactive.
// Native map/team/price facts are read under the original monitor. legacy/ExtendedState
// compatibility relation defaults are inert: never AI evidence or MP authority.
using System;
using DiplomacyPersistenceCodec = StrongholdEuropeDE.Runtime.DiplomacyStateCodec;
using System.IO;
using System.Linq;
using System.Threading;
using SHCDESE.API;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private bool _mpBootstrapRequested, _mpQuarantined;
        private LegacyDiplomacySave _originalOuterSeed;
        private long _ownerGeneration = -1;
        private bool BootstrapOriginalMpTradeOnly(byte[] primary, byte[] legacy)
        {
            RequireNativeDiplomacySection();
            TrustedContext context;
            string key, why;
            if (!ReadTrustedNetworkContext(out context, out key, out why))
                return false;
            if (!OriginalMpSaveFenceReady)
            {
                ProtocolNote("status=mp_consent_withheld|reason=whole_save_fence_unavailable");
                return false;
            }

            // ClearMapState has just reset the old map. No broad _mapActive enable.
            _mapActive = false;
            _mapDisabled = true;
            _mpBootstrapRequested = true;
            _mpQuarantined = false;
            _originalOuterSeed = null;
            _confirmedOuterSaveBytes = null;
            _ownerGeneration = _mapEpoch;
            ObserveStagedOriginalLoad(primary, true);
            // A legacy fallback cannot authenticate Format3 MP epoch/actor cursors.
            if (primary == null && legacy != null && !DiplomacyPersistenceCodec.IsInactive(legacy))
                _mpQuarantined = true;
            return true;
        }

        // A raw/finalizer fault is deferred to the managed owner. In the interval
        // before that cleanup, the signed object can still report Live; saves and
        // new UI intent must already fail closed on the actual atomic fault marker.
        private bool OriginalCaptureRevocationPending
        {
            get
            {
                if (Volatile.Read(ref _revokePending) || _managedCapture == null)
                    return true;
                var lifetime = Volatile.Read(ref _managedCapture.CurrentLifetime);
                return lifetime == null || Volatile.Read(ref lifetime.Retired) != 0 || Volatile.Read(ref lifetime.RevokeRequested) != 0 || Volatile.Read(ref _unownedCaptureFault) != 0;
            }
        }

        private bool MustDelayExternalGameSave
        {
            get
            {
                return _mpQuarantined || _pendingLocalIntent != null || // A completed local projection cannot outlive its actual MP session.
 // Scope this guard to MP ownership: solo canonical state remains separate.
                ((_mpBootstrapRequested || _mpConsentMode || _pendingOriginalMpLoadBytes != null) && (!OriginalMpSaveFenceReady || OriginalCaptureRevocationPending || _signed == null || !_signed.Live || !_signed.Ready || _signed.WaitingForOrderedPublicationAcks)) || (_mpBootstrapRequested && _confirmedOuterSaveBytes == null) || (_consentCollector != null && _consentCollector.MustDelayOriginalSave) || (_canonical != null && _canonical.HasPendingMpConsentOnly) || _stagedConsent != null || (_publication != null && (_localReceipt == null || _localReceipt.Phase != PublicationReceiptPhase.OwnerReadbackApplied || (_signed != null && _signed.IsHost && (_publicationQuorum == null || !_publicationQuorum.AllActualOriginalHumansApplied))));
            }
        }

        // Called either by the existing feature pump or managed engine owner,
        // with BOTH existing monitors held. No new hook/subscription.
        private void EnsureOriginalMpConsentOwner(OriginalMultiplayerConsentFrame observed)
        {
            RequireNativeDiplomacySection();
            if (!Monitor.IsEntered(_gate) || !OriginalMpSaveFenceReady || observed == null || _signed == null || !_signed.Live || !_signed.Ready || _mpQuarantined || observed.LocalOriginalMapGeneration != _mapEpoch || !observed.OriginalPinnedSignedSessionId.Same(_signed.Descriptor.Id))
                throw new InvalidOperationException("Original MP owner scope unavailable");
            if (_consentCollector == null)
                _consentCollector = new SignedTradeActionBatchCollector(_signed.Descriptor.Id);
            _latestOriginalFrame = observed;
            _mpConsentMode = true;
            _mpBootstrapRequested = true;
            _ownerGeneration = _mapEpoch;
            if (_canonical == null)
            {
                if (_pendingOriginalMpLoadBytes != null)
                    RestoreProvenOriginalMpLoad(observed, MapGuid(observed.SharedMapFingerprint, observed.SharedRosterFingerprint), GlobalHostTerm(observed));
                else
                {
                    if (_canonicalSaved != null)
                        throw new InvalidDataException("Unverified canonical owner already exists");
                    _canonical = CanonicalTradeReducer.CreateOriginalMpConsentOnly(observed, MapGuid(observed.SharedMapFingerprint, observed.SharedRosterFingerprint), GlobalHostTerm(observed));
                    _canonicalSaved = _canonical.SaveBytes();
                    var m = CanonicalTradeCodec.Decode(_canonicalSaved);
                    _archive = m.Archive.Copy();
                    _mailbox = m.Mailbox.Copy();
                }
            }

            if (_originalOuterSeed == null)
                InitializeOriginalMpOuter(observed);
            _existingEngineSubmitIntent = SubmitOriginalHumanIntent;
        }

        private PersistedHumanActionCursor ReadOriginalSavedActor(byte actor)
        {
            if (actor < 1 || actor > 8 || _canonicalSaved == null || _mpQuarantined)
                return null;
            var m = CanonicalTradeCodec.Decode(_canonicalSaved);
            return new PersistedHumanActionCursor(m.InputEpoch, m.InputSequence[actor], m.InputNonce[actor]);
        }

        private void PumpOriginalMpConsentOnly()
        {
            if (!_mpBootstrapRequested || _mpQuarantined || _ownerGeneration != _mapEpoch || _signed == null || !_signed.Live || !_signed.Ready)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                try
                {
                    long tick;
                    string reason;
                    if (!TryReadOriginalMapTick(out tick, out reason))
                        return;
                    OriginalMultiplayerConsentFrame observed;
                    if (!TryObserveOriginalMpTradeOnlyFrame(tick, out observed, out reason))
                        return;
                    EnsureOriginalMpConsentOwner(observed);
                    if (_pendingLocalIntent != null)
                        QueueFrozenUiIntent();
                // Existing bridge alone queues activation/actions and retries.
                }
                catch (Exception error)
                {
                    _mpQuarantined = true;
                    ProtocolNote("status=mp_owner_quarantined|reason=" + error.GetType().Name);
                }
            }
        }

        private void InitializeOriginalMpOuter(OriginalMultiplayerConsentFrame world)
        {
            // Original full native team partition and 21 default prices, NOT fabricated
            // empty coalitions. This read model is never passed to native team execution.
            _localPlayer = world.LocalOriginalHumanSlot;
            _mapFingerprint = world.SharedMapFingerprint;
            _rosterFingerprint = world.SharedRosterFingerprint;
            int processed = CanonicalTradeCodec.Decode(_canonicalSaved).Day;
            _lastProcessedDay = processed;
            _lastObservedNativeDay = world.NativeGameDay;
            _mapIdentity = world.FullOriginalMapIdentity.Copy();
            _roster.Clear();
            _coalitions.Clear();
            _relations.Clear();
            _playerCoalition.Clear();
            _capturedLordIds.Clear();
            _originalTeamPartition.Clear();
            _frozenPrices.Clear();
            CaptureRosterAndCoalitions(true);
            if (!_roster.OrderBy(x => x).SequenceEqual(world.Players().Select(x => (int)x.Slot).OrderBy(x => x)))
                throw new InvalidDataException("Original MP outer roster differs");
            FreezePrices();
            foreach (var coalition in _coalitions.Values.OrderBy(x => x.CoalitionId, StringComparer.Ordinal))
            {
                RefreshCoalition(coalition); // actual native economy, military and AI margins
                var relation = new RelationState
                {
                    CoalitionId = coalition.CoalitionId,
                    LastProcessedDay = processed,
                    Allied = coalition.StartingAlly,
                    StartingAlliance = coalition.StartingAlly
                };
                coalition.Allied = relation.Allied;
                // Existing compatibility migration has explicit imported/default provenance.
                // Human peers have no AIC; never invent one, quote or native permission.
                DiplomacyAgreementRules.InitializeFromLegacy(relation, coalition, processed);
                _relations.Add(relation.CoalitionId, relation);
            }

            var outer = SnapshotSave();
            var bytes = DiplomacyPersistenceCodec.Serialize(outer, _rules);
            _originalOuterSeed = DiplomacyPersistenceCodec.Deserialize(bytes, _rules);
            CanonicalTradeStatePersistence.Validate(_originalOuterSeed);
        }

        // Local, actual legacy/Schema19 envelope. Only clock fields advance from the
        // retained original observation; there are no diplomacy mutations in this lane.
        private LegacyDiplomacySave CaptureExistingOriginalMpOuter(OriginalMultiplayerConsentFrame world)
        {
            RequireNativeDiplomacySection();
            if (!Monitor.IsEntered(_gate) || world == null || _originalOuterSeed == null)
                throw new InvalidOperationException("Original MP outer owner not initialized");
            var outer = DiplomacyPersistenceCodec.Deserialize(DiplomacyPersistenceCodec.Serialize(_originalOuterSeed, _rules), _rules);
            if (!outer.MapIdentity.Same(world.FullOriginalMapIdentity) || outer.LocalPlayerId != world.LocalOriginalHumanSlot)
                throw new InvalidDataException("Original MP outer map/local identity differs");
            // Keep the already validated BEFORE logical day here. The tentative
            // projection advances logical day together with canonical candidate bytes.
            return outer;
        }

        private bool RefreshOriginalMpTradeOnlyFacade(object panel)
        {
            if (!_mpConsentMode)
                return false;
            if (!CanPresentDiplomacyUi)
                return true;
            bool valid = !_mpQuarantined && !OriginalCaptureRevocationPending && _ownerGeneration == _mapEpoch && _signed != null && _signed.Live && _signed.Ready;
            int selected = SelectedPlayer(panel);
            bool allowed = valid && MpUiPermitted(selected);
            // The existing launcher lives inside a collapsed injected parent.
            // Open that container only for this verified trade facade and hide every
            // unrelated diplomacy child; never relax the general map-active guard.
            SetVisibilityIfPresent(TradeElement(panel, "AllianceRootPanel"), valid);
            foreach (string name in new[]
            {
                "AllianceSelectedRuler",
                "AllianceDetailsPanel",
                "AllianceDetailsButton",
                "AllianceDetailsLabel",
                "AllianceOverviewButton",
                "AllianceOverviewLabel",
                "AllianceOverviewPanel"
            }

            )
                SetVisibilityIfPresent(TradeElement(panel, name), false);
            SetVisibilityIfPresent(TradeElement(panel, "TradeTab"), allowed);
            SetVisibilityIfPresent(TradeElement(panel, "TradeTabLabel"), allowed);
            // Only the trade facade is opened. No general diplomacy/portrait rewrite.
            SetVisibilityIfPresent(TradeElement(panel, "TradeModal"), valid && _tradeTab);
            SetVisibilityIfPresent(TradeElement(panel, "TradePanel"), valid && _tradeTab);
            if (valid && _tradeTab)
                RefreshMpConsentUi(panel, _draftPartner);
            return true;
        }
    }
}
