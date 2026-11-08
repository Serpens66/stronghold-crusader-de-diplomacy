using System;
using System.Threading;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Player;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private IDisposable _vassalRequestSubscription;
        private long _vassalRequestBlockedPairs;
        private sealed class OriginalGoodsIngressScope
        {
            internal readonly long Generation, Epoch;
            internal readonly int AiSlots;
            internal OriginalGoodsIngressScope(long generation, long epoch, int aiSlots)
            {
                Generation = generation;
                Epoch = epoch;
                AiSlots = aiSlots;
            }
            internal bool IncludesAi(int slot)
            {
                return slot >= 1 && slot <= 8 && (AiSlots & (1 << slot)) != 0;
            }
        }

        private OriginalGoodsIngressScope _originalGoodsIngressScope;

        private void PublishOriginalGoodsIngressScope(GamePlayerManagerAPI players,
            EngineInterface.PlayState live)
        {
            string reason;
            if (!RequestHistoryOwnerReady() || players.GetLocalPlayerId() != _localPlayer ||
                !IsSoloEngineSession(out reason) ||
                typeof(GamePlayerManagerAPI).Module.ModuleVersionId != OriginalRequestExtenderMvid ||
                typeof(EngineInterface).Module.ModuleVersionId != OriginalRequestGameMvid)
            {
                Volatile.Write(ref _originalGoodsIngressScope, null);
                return;
            }

            int slots = 0;
            foreach (int player in _roster)
                if (player != _localPlayer && live.is_skirmish_player(player) &&
                    !live.is_valid_player(player) && players.IsAIPlayer(player))
                    slots |= 1 << player;

            var previous = Volatile.Read(ref _originalGoodsIngressScope);
            if (previous == null || previous.Generation != _sessionGeneration ||
                previous.Epoch != _mapEpoch || previous.AiSlots != slots)
            {
                Volatile.Write(ref _originalGoodsIngressScope,
                    new OriginalGoodsIngressScope(_sessionGeneration, _mapEpoch, slots));
                Log("goods_request", "stage=original_solo_ingress_active|ai_slot_mask=" + slots);
            }
        }

        // The managed caller and SDK event's raw zero-based Good operand are
        // pinned together. A changed game or extender uses the existing policy.
        private static readonly Guid OriginalRequestExtenderMvid =
            new Guid("aab7af1d-dabf-4f55-a7a6-dd67c8f06853");
        private static readonly Guid OriginalRequestGameMvid =
            new Guid("29ade4ef-f587-422f-a1eb-f81dff066e0a");
        private readonly int[] _vassalRequestCancelGood = new int[9];
        private readonly int[] _vassalRequestCancelAmount = new int[9];
#if DIPLOMACY_PRIVATE_TESTS
        // One fixed-size observation queue covering the exact native PRE
        // RequestGoods, Post original AI CanSpareGoods and Post ReceiveGoods
        // callbacks. It NEVER mints an Origin2 ticket or claims an original
        // autonomous CPU producer from a matching actor/good/amount tuple.
        // Only a later independently proven native CALLER may do so.
        private const int OriginalNativeGoodsEvidenceBudget = 32;
        // 1 request.Pre; 2 canSpare.Post; 3 receiveGoods.Post.
        private sealed class OriginalNativeGoodsObservation
        {
            // 0 empty; 1 native callback owns writer; 2 published;
            // 3 managed owner lane currently reviewing the entry.
            internal int State;
            internal int Kind, FirstPlayer, SecondPlayer, Good, Amount, ResultHint;
            internal long Generation, Epoch;
        }
        private static OriginalNativeGoodsObservation[]
            CreateOriginalNativeGoodsObservations()
        {
            var items = new OriginalNativeGoodsObservation[OriginalNativeGoodsEvidenceBudget];
            for (int i = 0; i < items.Length; ++i)
                items[i] = new OriginalNativeGoodsObservation();
            return items;
        }
        private readonly OriginalNativeGoodsObservation[] _originalNativeGoodsObservations =
            CreateOriginalNativeGoodsObservations();
        private int _originalNativeGoodsObservationOverflow;
        private IDisposable _originalNativeAiSpareEvidenceSubscription;

        // Native engine callback. NO owner/_gate locking, SDK native reads,
        // allocation, file/console logging or request/history writes. A
        // protected bounded buffer only captures immutable primitive facts.
        private void CaptureOriginalNativeGoodsEvidence(
            int kind, int firstPlayer, int secondPlayer, int good,
            int amount, int resultHint)
        {
            if (!Volatile.Read(ref _mapActive) || Volatile.Read(ref _mapDisabled) ||
                kind < 1 || kind > 3 ||
                firstPlayer < 1 || firstPlayer > 8 ||
                secondPlayer < 0 || secondPlayer > 8 ||
                (secondPlayer == firstPlayer && secondPlayer != 0) ||
                good < 1 || good > 24 || amount < 1 || amount > 1000000)
                return;
            foreach (OriginalNativeGoodsObservation item in _originalNativeGoodsObservations)
            {
                if (Interlocked.CompareExchange(ref item.State, 1, 0) != 0)
                    continue;
                try
                {
                    item.Kind = kind;
                    item.FirstPlayer = firstPlayer;
                    item.SecondPlayer = secondPlayer;
                    item.Good = good;
                    item.Amount = amount;
                    item.ResultHint = resultHint;
                    item.Generation = Volatile.Read(ref _sessionGeneration);
                    item.Epoch = Volatile.Read(ref _mapEpoch);
                    Volatile.Write(ref item.State, 2);
                }
                catch (Exception)
                {
                    Volatile.Write(ref item.State, 0);
                }
                return;
            }
            Interlocked.Increment(ref _originalNativeGoodsObservationOverflow);
        }

        private void OnNativeAiCanSpareGoodsEvidence(PlayerAIEvaluateCanSpareGoods e)
        {
            if (e == null || e.Phase != EventHookPhase.Post)
                return;
            // This is the ORIGINAL AI outcome hint, but the callback does
            // NOT include the actual issuing CPU request/caller identity.
            CaptureOriginalNativeGoodsEvidence(2, e.TargetPlayerId, 0,
                (int)e.Good, e.Amount, e.ReturnValue ? 1 : 0);
        }

        // Owner-managed, not native; intentionally makes no executable
        // request or Origin2 claim. A shared tuple is not a causal bind.
        private void PumpOriginalNativeGoodsEvidence()
        {
            if (!System.Threading.Monitor.IsEntered(_gate) ||
                !_mapActive || _mapDisabled || _mapBootstrapPending)
                return;
            foreach (OriginalNativeGoodsObservation item in _originalNativeGoodsObservations)
            {
                if (Interlocked.CompareExchange(ref item.State, 3, 2) != 2)
                    continue;
                try
                {
                    if (item.Generation == _sessionGeneration &&
                        item.Epoch == _mapEpoch)
                    {
                        string kind = item.Kind == 1 ?
                            "original_native_request_pre" :
                            item.Kind == 2 ?
                            "original_ai_can_spare_post" :
                            "original_native_receive_goods_post";
                        Log("original_goods_evidence",
                            "status=" + kind +
                            "|player_first=" + item.FirstPlayer +
                            "|player_second=" + item.SecondPlayer +
                            "|good=" + item.Good +
                            "|amount=" + item.Amount +
                            "|native_boolean_result_hint=" + item.ResultHint +
                            "|generation=" + item.Generation +
                            "|epoch=" + item.Epoch +
                            "|original_producer_identity=unknown" +
                            "|Origin2_authentication=false" +
                            "|request_id=unbound|native_receipt=false" +
                            "|favor_credit=false|no_mutation=true");
                    }
                }
                finally
                {
                    Volatile.Write(ref item.State, 0);
                }
            }
            int dropped = Interlocked.Exchange(
                ref _originalNativeGoodsObservationOverflow, 0);
            if (dropped > 0)
                Log("original_goods_evidence",
                    "status=native_evidence_bounded_overflow|count=" + dropped +
                    "|native_ack=false|no_request_minted=true");
        }
#endif

        private static long GoodsRequestPairBit(int requester, int requestedPlayer)
        {
            return requester >= 1 && requester <= 8 && requestedPlayer >= 1 && requestedPlayer <= 8 && requester != requestedPlayer ? 1L << ((requester - 1) * 8 + requestedPlayer - 1) : 0L;
        }

        // Managed model only; callers own _gate. This is personal fealty, never
        // the entire coalition. A named vassal cannot request from any player;
        // an ordinary coalition neighbour is not treated as that vassal.
        private bool VassalRequestForbidden(int requester, int requestedPlayer)
        {
            if (GoodsRequestPairBit(requester, requestedPlayer) == 0)
                return false;
            foreach (RelationState relation in _relations.Values)
                if (relation.Allied && DiplomacyVassalage.IsActive(relation.ExtendedState) && DiplomacyVassalage.VassalOf(relation.ExtendedState) == requester)
                    return true;
            return false;
        }

        private bool CanRequestGoodsFrom(int player)
        {
            return player >= 1 && player <= 8 && player != _localPlayer && !VassalRequestForbidden(_localPlayer, player);
        }

        private void PublishVassalRequestSnapshot()
        {
            long pairs = 0;
            if (_mapActive && !_mapDisabled && !_mapBootstrapPending)
                foreach (RelationState relation in _relations.Values)
                    if (relation.Allied && DiplomacyVassalage.IsActive(relation.ExtendedState))
                        for (int requested = 1; requested <= 8; requested++)
                            pairs |= GoodsRequestPairBit(DiplomacyVassalage.VassalOf(relation.ExtendedState), requested);
            Volatile.Write(ref _vassalRequestBlockedPairs, pairs);
        }

        private void OnNativeGoodsRequest(PlayerAIRequestGoodsEventArgs request)
        {
            if (request == null || request.Phase != EventHookPhase.Pre)
                return;
            // COPY 1.45 names are misleading here: Ally_RequestGoods1064 ->
            // packet71/kind1 -> 70 places the REQUESTED player in Source and
            // the REQUESTER in Target. Do not reuse the received-goods roles.
            int requester = request.TargetPlayerId, requested = request.SourcePlayerId;
            int good = (int)request.Good + 1; // Chore113 stores catalog ID minus one.
            long pair = GoodsRequestPairBit(requester, requested);
            bool forbidden = (Volatile.Read(ref _vassalRequestBlockedPairs) & pair) != 0;
            var scope = Volatile.Read(ref _originalGoodsIngressScope);
            bool unowned = scope != null && scope.IncludesAi(requester) && pair != 0 &&
                Volatile.Read(ref _mapActive) && !Volatile.Read(ref _mapDisabled) &&
                !Volatile.Read(ref _mpConsentMode) && !Volatile.Read(ref _mpBootstrapRequested) &&
                scope.Generation == Volatile.Read(ref _sessionGeneration) &&
                scope.Epoch == Volatile.Read(ref _mapEpoch);
#if DIPLOMACY_PRIVATE_TESTS
            CaptureOriginalNativeGoodsEvidence(1, requester, requested, good,
                request.Amount, forbidden || unowned ? 1 : 0);
#endif
            if (forbidden || unowned)
                request.SkipOriginalFunction = true;
            if (unowned && good >= 1 && good <= 24 && request.Amount > 0 && request.Amount <= 1000000)
                CaptureUnknownGoodsIngress(requested, good, request.Amount, requester);
            // No model lock, native read, allocation, log or UI on this callback.
        }

        private void ResetVassalGoodsRequests()
        {
            Volatile.Write(ref _vassalRequestBlockedPairs, 0L);
            Volatile.Write(ref _originalGoodsIngressScope, null);
            Array.Clear(_vassalRequestCancelGood, 0, _vassalRequestCancelGood.Length);
            Array.Clear(_vassalRequestCancelAmount, 0, _vassalRequestCancelAmount.Length);
        }

        private void PumpVassalGoodsRequests()
        {
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                // The native HUD's last complete PlayState exposes incoming
                // requests to local. Original CancelGoods1062 -> packet71/kind3
                // -> 00 zeros exactly these type/amount fields and updates
                // request cooldown; return0 means queued, not failure.
                var state = GameData.Instance == null ? null : GameData.Instance.lastGameState;
                if (state == null || state.skirmish_player_requesting_type == null || state.skirmish_player_requesting_amount == null)
                    return;
                for (int requester = 1; requester <= 8; requester++)
                {
                    if (requester >= state.skirmish_player_requesting_type.Length || requester >= state.skirmish_player_requesting_amount.Length)
                        continue;
                    int good = state.skirmish_player_requesting_type[requester];
                    int amount = state.skirmish_player_requesting_amount[requester];
                    if (!VassalRequestForbidden(requester, _localPlayer) || good <= 0 || amount <= 0)
                    {
                        _vassalRequestCancelGood[requester] = 0;
                        _vassalRequestCancelAmount[requester] = 0;
                        continue;
                    }

                    if (_vassalRequestCancelGood[requester] == good && _vassalRequestCancelAmount[requester] == amount)
                        continue;
                    // Claim before dispatch: a foreign callback can throw after
                    // native accepted the packet. Never emit it each frame. A
                    // map/load reset permits cleanup of a persisted old request.
                    _vassalRequestCancelGood[requester] = good;
                    _vassalRequestCancelAmount[requester] = amount;
                    try
                    {
                        EngineInterface.GameAction(Enums.GameActionCommand.Ally_CancelGoodsReq, requester, 0, 0);
                    }
                    catch
                    { /* The next native snapshot, not return0, attests consumption. */
                    }
                }
            }
        }
    }
}
