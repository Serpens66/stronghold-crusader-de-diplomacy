// Bounded, transient managed GameAction(1064) capture; owner-verified
// observations are stored in the EXISTING durable RequestHistory.
// Unknown provenance is not
// OriginalHumanGoods, OriginalAutomaticGoods, or ModAuthored.
using System;
using System.Linq;
using System.Threading;
using CrusaderDE;
using SHCDESE.API;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private const int UnknownGoodsIngressBudget = 8;
        private const long UnknownGoodsIngressMaximumWaitTicks = 256L;

        private sealed class UnknownGoodsIngress
        {
            // 0 free; 1 callback writer; 2 published; 3 owner-lane review.
            internal int State;
            internal int Requested, Good, Amount, LocalHint, Reviewed, NativeRequester;
            internal long Generation, Epoch, CaptureTick;
        }

        // Allocated at plugin initialization, never on GameAction callback.
        private static UnknownGoodsIngress[] CreateUnknownGoodsIngress()
        {
            var slots = new UnknownGoodsIngress[UnknownGoodsIngressBudget];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new UnknownGoodsIngress();
            return slots;
        }

        private readonly UnknownGoodsIngress[] _unknownGoodsIngress =
            CreateUnknownGoodsIngress();

        // Called only by the existing managed GameAction prefix. The callback
        // has no native SDK access, file/log output, owner/_gate lock, allocation
        // of request objects, or delegation to a native request writer.
        private static bool CaptureUnboundOriginalGoodsManagedIngress(
            DiplomacyFeature owner, int requested, int good, int amount,
            ref int wrapperResult)
        {
            if (owner == null || !Volatile.Read(ref owner._mapActive) ||
                Volatile.Read(ref owner._mapDisabled))
                return true; // No owning mod map: leave native Vanilla alone.

            // These malformed operands are not a useful diplomatic request.
            // Never re-label their source or turn wrapper0 into an ACK.
            if (requested < 1 || requested > 8 || good < 1 || good > 24 ||
                amount < 1 || amount > 1000000)
            {
                wrapperResult = 0;
                return false;
            }

            owner.CaptureUnknownGoodsIngress(requested, good, amount, 0);
            wrapperResult = 0;
            return false;
        }

        // Native hooks publish only bounded primitive fields. The real owner
        // validates the epoch and persists the observation in its next frame.
        private void CaptureUnknownGoodsIngress(int requested, int good, int amount,
            int nativeRequester)
        {
            UnknownGoodsIngress[] slots = _unknownGoodsIngress;
            for (int i = 0; i < slots.Length; i++)
            {
                UnknownGoodsIngress entry = slots[i];
                if (Interlocked.CompareExchange(ref entry.State, 1, 0) != 0)
                    continue;
                try
                {
                    entry.Requested = requested;
                    entry.Good = good;
                    entry.Amount = amount;
                    entry.NativeRequester = nativeRequester;
                    entry.Generation = Volatile.Read(ref _sessionGeneration);
                    entry.Epoch = Volatile.Read(ref _mapEpoch);
                    entry.LocalHint = Volatile.Read(ref _localPlayer);
                    entry.CaptureTick = ReadSimulationTick();
                    entry.Reviewed = 0;
                    Volatile.Write(ref entry.State, 2);
                }
                catch (Exception)
                {
                    Volatile.Write(ref entry.State, 0);
                }
                return;
            }

            // Bounded overflow fails closed. It cannot silently pass a native
            // command without a RequestHistory owner ID. The owner may report this only
            // through an already existing managed QA diagnostics lane.
            Interlocked.Increment(ref _unknownGoodsIngressOverflow);
        }

        private int _unknownGoodsIngressOverflow;

        // The owner/native lease and original session checks precede this
        // call. This is an observation only: a RequestHistory row is not an
        // accepted original order and never creates an executable intent.
        private bool TryPersistUnattributedGoodsObservation(
            UnknownGoodsIngress item, out ulong requestId)
        {
            requestId = 0;
            // Origin byte 6 requires an explicit outer schema22 writer;
            // older readers cannot decode it. Preserve the old record layout,
            // legacy read paths and existing Mod-gift serial suffix.
            if (DiplomacyStateCodec.SchemaVersion < 22 ||
                item == null || !RequestHistoryOwnerReady() ||
                !IsNativeDiplomacySectionHeld || _inventoryRestorePending ||
                _saveSuppressed || _activeSave != null ||
                _pendingSavePayload != null || _pendingLegacySavePayload != null ||
                _executingIntent != null || _pendingTeamTransition != null ||
                _pendingPackage != null || _nativeTransferProbe != null ||
                item.Generation != _sessionGeneration || item.Epoch != _mapEpoch ||
                item.LocalHint != _localPlayer ||
                !_roster.Contains(item.Requested) || !_roster.Contains(_localPlayer))
                return false;

            if (_requestHistory == null)
                _requestHistory = new DiplomaticRequestHistory(
                    _mapFingerprint, _rosterFingerprint);
            if (!String.Equals(_requestHistory.MapFingerprint, _mapFingerprint,
                    StringComparison.Ordinal) ||
                !String.Equals(_requestHistory.RosterFingerprint,
                    _rosterFingerprint, StringComparison.Ordinal))
                return false;

            // local context (verified routing party) != original caller.
            var roster = _roster.ToDictionary(player => player, player => 0);
            if (item.NativeRequester != 0)
                return _requestHistory.TryRecordUnattributedNativeGoodsRefusal(
                    item.NativeRequester, item.Requested, item.Good, item.Amount,
                    _lastProcessedDay, roster, _localPlayer, out requestId);
            return _requestHistory.TryCaptureUnattributedManagedGoods(
                item.LocalHint, item.Requested, item.Good, item.Amount,
                _lastProcessedDay, roster, _localPlayer, out requestId);
        }

        // Called from the already established owner _gate frame. This is the
        // ONLY place that accesses native role/team state. Retain an unknown
        // ingress for a short bounded window; a later independently verified
        // human HUD confirmation uses the normal GoodsRequest pipeline, not
        // an invented request source or a replay of this foreign invocation.
        private void PumpUnknownOriginalGoodsManagedIngress()
        {
            if (!Monitor.IsEntered(_gate) || !_mapActive || _mapDisabled ||
                _mapBootstrapPending || _inventoryRestorePending)
                return;

            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                var api = GamePlayerManagerAPI.Instance;
                var game = GameData.Instance;
                var live = game == null ? null : game.lastGameState;
                if (api == null || live == null || !IsNativeDiplomacySectionHeld)
                {
                    Volatile.Write(ref _originalGoodsIngressScope, null);
                    return;
                }
                PublishOriginalGoodsIngressScope(api, live);
                long tick = ReadSimulationTick();

                foreach (UnknownGoodsIngress item in _unknownGoodsIngress)
                {
                    if (Interlocked.CompareExchange(ref item.State, 3, 2) != 2)
                        continue;
                    bool retire = false;
                    try
                    {
                        // A UI/epoch transition never authorizes an old command.
                        bool sameWorld = item.Generation == _sessionGeneration &&
                            item.Epoch == _mapEpoch &&
                            item.LocalHint == _localPlayer &&
                            tick >= item.CaptureTick && item.CaptureTick >= 0;
                        if (!sameWorld ||
                            tick - item.CaptureTick > UnknownGoodsIngressMaximumWaitTicks)
                        {
                            retire = true;
                            Log("goods_request",
                                "stage=unattributed_ingress_neutral_drop_unverified_or_full" +
                                "|requested_player=" + item.Requested +
                                "|good=" + item.Good + "|amount=" + item.Amount +
                                "|native_ack=false|favor_credit=false");
                            continue;
                        }

                        if (item.NativeRequester != 0)
                        {
                            ulong id;
                            OriginalGoodsIngressScope scope = Volatile.Read(ref _originalGoodsIngressScope);
                            if (scope != null && scope.IncludesAi(item.NativeRequester) &&
                                IsSendableGood(item.Good) &&
                                TryPersistUnattributedGoodsObservation(item, out id))
                            {
                                retire = true;
                                Log("goods_request", "stage=unattributed_native_request_refused" +
                                    "|request_id=" + id + "|requester_slot=" + item.NativeRequester +
                                    "|requested_player=" + item.Requested + "|good=" + item.Good +
                                    "|amount=" + item.Amount + "|producer=unknown");
                            }
                            continue;
                        }

                        // Actual original local human, reciprocal alliance and
                        // bounded item are only NECESSARY checks, not proof that
                        // this managed call came from the human HUD.
                        bool actorAndTermsUsable = api.GetLocalPlayerId() ==
                                item.LocalHint &&
                            live.is_valid_player(item.LocalHint) &&
                            !live.is_skirmish_player(item.LocalHint) &&
                            !live.is_valid_player(item.Requested) &&
                            live.is_skirmish_player(item.Requested) &&
                            api.IsAIPlayer(item.Requested) &&
                            api.IsPlayerAlliedTo(item.LocalHint, item.Requested) &&
                            api.IsPlayerAlliedTo(item.Requested, item.LocalHint) &&
                            IsAlivePlayer(item.Requested) &&
                            IsAlivePlayer(item.LocalHint) &&
                            IsSendableGood(item.Good) &&
                            CanRequestGoodsFrom(item.Requested);

                        // Actual native owner/role/terms proof now persists ONE
                        // observation in the already bounded RequestHistory. It
                        // does NOT attest who called the intercepted wrapper.
                        // If history is busy/full, keep this primitive slot for
                        // bounded retry, then explicitly neutral-drop on expiry.
                        ulong observedRequestId;
                        if (actorAndTermsUsable &&
                            TryPersistUnattributedGoodsObservation(
                                item, out observedRequestId))
                        {
                            retire = true;
                            Log("goods_request",
                                "stage=unattributed_managed_request_persisted" +
                                "|request_id=" + observedRequestId +
                                "|requester_context=" + item.LocalHint +
                                "|requested_player=" + item.Requested +
                                "|good=" + item.Good + "|amount=" + item.Amount +
                                "|origin=unattributed_managed_goods" +
                                "|original_caller=unknown" +
                                "|native_ack=false|resend=false" +
                                "|favor_credit=false|penalty=false");
                        }

                        // A genuinely CURRENT, visible original HUD modal may
                        // be rebound to the EXISTING GoodsRequest UI workflow.
                        // This does not replay the intercepted unknown call:
                        // the human must explicitly confirm in that HUD again
                        // before an OriginalHumanGoods RequestHistory record is created.
                        // Matching merely a native local human is insufficient.
                        if (actorAndTermsUsable && retire &&
                            _goodsRequest == null &&
                            !HasPendingGoodsCommand && _pendingGoodsTarget == 0 &&
                            !_overviewVisible && CanPresentDiplomacyUi)
                        {
                            MainViewModel view = MainViewModel.Instance;
                            object panel = CurrentAlliesPanel();
                            string coalition;
                            if (view != null && view.AlliesPanelVisible &&
                                view.Allies_GoodsViewVis &&
                                view.Allies_RequestGoodsViewVis &&
                                !view.Allies_SendGoodsViewVis &&
                                panel != null &&
                                SelectedPlayer(panel) == item.Requested &&
                                (int)_selectedGoodsField.GetValue(panel) ==
                                    item.Good &&
                                (int)_selectedGoodsAmountField.GetValue(panel) ==
                                    item.Amount &&
                                _playerCoalition.TryGetValue(
                                    item.Requested, out coalition))
                            {
                                _goodsRequest = new GoodsRequest
                                {
                                    Panel = panel,
                                    Generation = _sessionGeneration,
                                    Epoch = _mapEpoch,
                                    Fingerprint = _mapFingerprint,
                                    Coalition = coalition,
                                    LocalPlayer = item.LocalHint,
                                    Target = item.Requested,
                                    Good = item.Good,
                                    Amount = item.Amount,
                                    RequestedDay = Math.Max(0, _lastProcessedDay),
                                    Confirmed = false,
                                    Lifetime =
                                        new DiplomacyPanelCommandLifetime(view),
                                };
                                retire = true;
                                Log("goods_request",
                                    "stage=unbound_rebound_to_visible_UI" +
                                    "|still_requires_new_explicit_confirm=true" +
                                    "|original_caller_still_unknown=true" +
                                    "|unattributed_evidence_persisted_separately=true" +
                                    "|native_ack=false|favor_credit=false");
                                continue;
                            }
                        }
                        if (!retire &&
                            Interlocked.CompareExchange(ref item.Reviewed, 1, 0) == 0)
                            Log("goods_request",
                                "stage=" + (actorAndTermsUsable ?
                                    "unattributed_ingress_waiting_owner_or_history_capacity" :
                                    "unbound_ingress_reviewed_role_or_terms_unavailable") +
                                "|native_original_actor_hint=" + item.LocalHint +
                                "|requested_player=" + item.Requested +
                                "|good=" + item.Good + "|amount=" + item.Amount +
                                "|generation=" + item.Generation +
                                "|epoch=" + item.Epoch +
                                "|mod_request_id=unknown" +
                                "|native_ack=false|favor_credit=false");

                        // A validated observation stays permanently unknown-
                        // origin; it is not a forwardable command. A real later
                        // HUD click can capture an independent authenticated
                        // request and must still explicitly confirm. No replay.
                    }
                    catch (Exception)
                    {
                        // Unreadable native proof remains queued, not accepted.
                    }
                    finally
                    {
                        Volatile.Write(ref item.State, retire ? 0 : 2);
                    }
                }
            }
            int overflow = Interlocked.Exchange(ref _unknownGoodsIngressOverflow, 0);
            if (overflow > 0)
                Log("goods_request",
                    "stage=unbound_ingress_budget_exhausted" +
                    "|count=" + overflow +
                    "|native_ack=false|favor_credit=false");
        }

        // No receipt/native delivery binder is used here. This observation
        // cannot become allowed, forwarded or fulfilled; Cold loads only
        // RequestHistory bytes, never an executable GameAction request.
    }
}
