// Bounded maintenance of actual human HUD orders; never AI-to-AI authority.
using System;
using System.Threading;
using CrusaderDE;
using SHCDESE.Interop;
using SHCDESE.API;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private const int MaximumOrderReapplications = 2;
        private const long OrderRetryIntervalTicks = 256;
        private const long OrderTrackingTicks = 1024;
        private readonly int[] _orderReapplications = new int[9];
        private readonly long[] _orderNextRetryTick = new long[9];
        private readonly HumanAllyOrderAttempt[] _originalHumanOrders = new HumanAllyOrderAttempt[9];
        // Native evaluator events have no command/packet id. Once a prior
        // dispatch of a tuple can overlap, asynchronous replies stay unbound.
        // Nine bits per AI (eight attack targets + defence), reset with map.
        private readonly int[] _ambiguousReplyKeys = new int[9];
        private long _lastMilitaryTick = -1;
        private int _lastMilitaryDay = -1;
        private static int ReplyKey(HumanAllyOrderAttempt order)
        {
            return order == null ? 0 : order.Opcode == 11 ? 256 : order.Opcode >= 1 && order.Opcode <= 6 && order.Target >= 1 && order.Target <= 8 ? 1 << (order.Target - 1) : 0;
        }

        private void MarkReplyAmbiguous(HumanAllyOrderAttempt order)
        {
            if (order == null || order.Consulted < 1 || order.Consulted > 8)
                return;
            int key = ReplyKey(order), previous;
            do
            {
                previous = Volatile.Read(ref _ambiguousReplyKeys[order.Consulted]);
            }
            while (Interlocked.CompareExchange(ref _ambiguousReplyKeys[order.Consulted], previous | key, previous) != previous);
        }

        private void ClearMilitaryMaintenance()
        {
            for (int i = 1; i <= 8; i++)
            {
                _orderReapplications[i] = 0;
                _orderNextRetryTick[i] = 0;
                Volatile.Write(ref _originalHumanOrders[i], null);
                Volatile.Write(ref _ambiguousReplyKeys[i], 0);
            }

            _lastMilitaryTick = -1;
            _lastMilitaryDay = -1;
        }

        // The sole caller is the original human HUD prefix, never a native
        // answer or our own reissue. Replacement also cancels old feedback.
        private void BeginHumanOrderMaintenance(HumanAllyOrderAttempt original)
        {
            if (original == null || original.Consulted < 1 || original.Consulted > 8)
                return;
            int player = original.Consulted;
            original.HumanSource = original;
            HumanAllyOrderAttempt previous = Interlocked.Exchange(ref _originalHumanOrders[player], original);
            if (previous != null && previous.DispatchedDay >= 0)
                MarkReplyAmbiguous(previous);
            HumanAllyOrderAttempt old = Volatile.Read(ref _allyInFlight[player]);
            if (old != null && !ReferenceEquals(old.HumanSource, original))
            {
                MarkReplyAmbiguous(old);
                Interlocked.CompareExchange(ref _allyInFlight[player], null, old);
            }

            old = Volatile.Read(ref _allyReplies[player]);
            if (old != null && !ReferenceEquals(old.HumanSource, original))
                Interlocked.CompareExchange(ref _allyReplies[player], null, old);
            old = Volatile.Read(ref _allyVoices[player]);
            if (old != null && !ReferenceEquals(old.HumanSource, original))
                Interlocked.CompareExchange(ref _allyVoices[player], null, old);
            old = Volatile.Read(ref _allyOrderStatus[player]);
            if (old != null && !ReferenceEquals(old.HumanSource, original))
                Interlocked.CompareExchange(ref _allyOrderStatus[player], null, old);
            _orderReapplications[player] = 0;
            _orderNextRetryTick[player] = 0;
        }

        private bool IsLatestHumanOrder(HumanAllyOrderAttempt order)
        {
            return order != null && order.Consulted >= 1 && order.Consulted <= 8 &&
                AllyOrderIsCurrent(order, order.Consulted) &&
                order.HumanSource != null &&
                !Volatile.Read(ref order.HumanSource.HumanCancelRequested) &&
                ReferenceEquals(Volatile.Read(
                    ref _originalHumanOrders[order.Consulted]), order.HumanSource);
        }

        // A rollback is not a new elapsed interval. Discard all retained
        // intent; a loaded native order can still be displayed observationally.
        private bool ObserveMilitaryClock(long tick, int day)
        {
            if (tick < 0 || day < 0)
                return false;
            bool rollback = _lastMilitaryTick >= 0 && (tick < _lastMilitaryTick || day < _lastMilitaryDay);
            if (rollback)
            {
                int[] ambiguous = new int[9];
                for (int player = 1; player <= 8; player++)
                {
                    HumanAllyOrderAttempt old = Volatile.Read(ref _originalHumanOrders[player]);
                    ambiguous[player] = Volatile.Read(ref _ambiguousReplyKeys[player]) | (old != null && old.DispatchedDay >= 0 ? ReplyKey(old) : 0);
                }

                ClearAllyOrders();
                // A clock rollback alone does not prove native callbacks drained.
                for (int player = 1; player <= 8; player++)
                    Volatile.Write(ref _ambiguousReplyKeys[player], ambiguous[player]);
            }

            _lastMilitaryTick = tick;
            _lastMilitaryDay = day;
            return !rollback;
        }

        private static long Deadline(long tick)
        {
            return tick > long.MaxValue - OrderRetryIntervalTicks ? long.MaxValue : tick + OrderRetryIntervalTicks;
        }

        // Caller owns the original EngineInterface monitor. A missing live
        // observation defers; a known role/team change rejects without latching.
        private bool TryValidateHumanPair(HumanAllyOrderAttempt order, out bool valid)
        {
            valid = false;
            if (order.NativeRequest == null)
                return true;
            var api = GamePlayerManagerAPI.Instance;
            var game = GameData.Instance;
            var live = game == null ? null : game.lastGameState;
            if (api == null || live == null)
                return false;
            valid = IsLatestHumanOrder(order) && order.Requester >= 1 && order.Requester <= 8 && order.Requester != order.Consulted && api.GetLocalPlayerId() == order.Requester && live.is_valid_player(order.Requester) && !live.is_skirmish_player(order.Requester) && !live.is_valid_player(order.Consulted) && live.is_skirmish_player(order.Consulted) && api.IsAIPlayer(order.Consulted) && IsAlivePlayer(order.Requester) && IsAlivePlayer(order.Consulted) && IsCoalitionAllied(order.Consulted) && api.IsPlayerAlliedTo(order.Requester, order.Consulted) && api.IsPlayerAlliedTo(order.Consulted, order.Requester);
            if (valid && order.Opcode >= 1 && order.Opcode <= 6)
                valid = order.Target >= 1 && order.Target <= 8 && order.Target != order.Requester && order.Target != order.Consulted && IsAlivePlayer(order.Target) && !api.IsPlayerAlliedTo(order.Requester, order.Target) && !api.IsPlayerAlliedTo(order.Target, order.Requester) && !api.IsPlayerAlliedTo(order.Consulted, order.Target) && !api.IsPlayerAlliedTo(order.Target, order.Consulted) && !TreatyProtectsPlayers(order.Consulted, order.Target);
            return true;
        }

        private bool CanReapply(HumanAllyOrderAttempt order, long tick, int day)
        {
            if (!IsLatestHumanOrder(order) || order.Accepted != true || !order.NativeDecision || order.PolicyDecision || order.DecisionUnbound || order.ObservedActive || order.EndedDay >= 0 || order.TrackingExpired || order.DispatchFailed || !(order.Opcode == 11 || order.Opcode >= 1 && order.Opcode <= 6))
                return false;
            int player = order.Consulted;
            HumanAllyOrderAttempt original = order.HumanSource;
            return original.DispatchedDay == day && order.DispatchedDay == day && original.RequestedDay <= order.DispatchedDay && order.DispatchedDay - original.RequestedDay <= 1 && original.DispatchedTick >= 0 && tick >= original.DispatchedTick && tick - original.DispatchedTick <= OrderTrackingTicks && _orderReapplications[player] < MaximumOrderReapplications && Volatile.Read(ref _allyOrders[player]) == null && Volatile.Read(ref _allyInFlight[player]) == null && Volatile.Read(ref _allyReplies[player]) == null && tick >= order.DispatchedTick && tick - order.DispatchedTick >= OrderRetryIntervalTicks;
        }

        // Reserve before GameAction, including on exceptions. A retry is a new
        // unconfirmed attempt and must receive its own positive decision before
        // another retry; the old accepted flag is never recycled.
        private HumanAllyOrderAttempt ReserveReapplication(HumanAllyOrderAttempt order, long tick, int day)
        {
            if (!CanReapply(order, tick, day) || tick < _orderNextRetryTick[order.Consulted])
                return null;
            int player = order.Consulted;
            HumanAllyOrderAttempt attempt = CopyAllyOrder(order);
            attempt.PriorStatus = order;
            attempt.HasNativeBefore = false;
            attempt.DispatchedTick = tick;
            attempt.DispatchedDay = day;
            MarkReplyAmbiguous(order);
            _orderReapplications[player]++;
            _orderNextRetryTick[player] = Deadline(tick);
            Volatile.Write(ref _allyOrderStatus[player], attempt);
            Volatile.Write(ref _allyInFlight[player], attempt);
            return attempt;
        }

        private void MaintainConfirmedAllyOrders()
        {
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _pendingTeamTransition != null || _inventoryRestorePending || _pendingPackage != null || _nativeTransferProbe != null)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                long tick = ReadSimulationTick();
                int day = Math.Max(0, _lastProcessedDay);
                for (int player = 1; player <= 8; player++)
                {
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
                    // Human reapplications use the same native order lane:
                    // retain them until the earlier MOD native claim releases.
                    if (Volatile.Read(ref _originalSupportNativeClaims[player]) != null)
                        continue;
#endif
                    HumanAllyOrderAttempt order = Volatile.Read(ref _allyOrderStatus[player]);
                    if (!CanReapply(order, tick, day))
                        continue;
                    try
                    {
                        bool valid;
                        if (!TryValidateHumanPair(order, out valid))
                            continue;
                        if (!valid)
                        {
                            RefuseAllyOrderLocally(order);
                            continue;
                        }

                        OriginalAllyOrderSnapshot observation;
                        if (!TryReadNativeAllyOrder(player, out observation))
                            continue;
                        ApplyAllyOrderObservation(order, observation, day);
                        if (order.ObservedActive)
                            continue;
                        if (order.Opcode != 11)
                        {
                            if (TreatyProtectsPlayers(_localPlayer, order.Target))
                                continue;
                            // Demand a complete observation at this original
                            // tick rather than yesterday's/daily force cache.
                            var force = MilitaryByPlayer(day, true);
                            long available, enemy;
                            force.TryGetValue(player, out available);
                            force.TryGetValue(order.Target, out enemy);
                            if (available <= 0 || enemy > 0 && available < enemy / 2L)
                                continue;
                        }

                        HumanAllyOrderAttempt attempt = ReserveReapplication(order, tick, day);
                        if (attempt == null)
                            continue;
                        DispatchAllyOrder(attempt, false);
                        Log("military_maintenance", "status=bounded_reissue_not_acknowledgement" + "|consulted=" + player + "|kind=" + attempt.Opcode + "|target=" + attempt.Target + "|attempt=" + _orderReapplications[player]);
                    }
                    catch (Exception exception)
                    {
                        NoteVassalOrderFault("maintenance_deferred", exception);
                    }
                }
            }
        }
    }
}
