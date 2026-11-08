// Process-local, mod-authored AI relief intent lifecycle only.
// No original CPU requester, Native transport, order/ACK or favor authority.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal enum ModSupportIntentEnd : byte
    {
        DispatchBeganUnconfirmed = 1,
        Cancelled = 2,
        OriginalOrderConflict = 3,
        ForeignWorld = 4,
        ColdInvalidated = 5,
        Expired = 6,
        ReliefEnded = 7,
        ClockRegressed = 8
    }

    internal enum ModSupportPreflight : byte
    {
        Absent = 0,
        Waiting = 1,
        RequestIndependentNativePreflight = 2,
        ReleasedOriginalConflict = 3
    }

    // The two protocols share the one existing MOD intent journal, but an
    // original evaluator REQUEST is not a direct per-tribe order.
    internal enum ModSupportPreflightRoute : byte
    {
        None = 0,
        DirectTribe = 1,
        OriginalAllyEvaluator = 2
    }

    // An immutable, non-authoritative proposal. A mod is the author, while
    // the donor AI is consulted and a distinct AI is defended/requesting.
    internal sealed class ModSupportIntent
    {
        internal const string ModAuthor = "DiplomacyMod";
        internal readonly ulong Nonce;
        internal readonly byte ConsultedDonorAI, RequesterDefendedAI;
        internal readonly int Day, Priority, TribePower;
        internal readonly long Generation, MapEpoch, CancellationSerial;
        internal readonly long IssuedTick, ReliefEdgeTick, ExpiresAfterTick;
        // Unique process-local genuine false->true relief transition; distinct
        // from a candidate nonce and refreshed still-true observations.
        internal readonly ulong ReliefEventId;
        internal readonly string MapIdentitySha256;
        internal readonly AiMilitaryCooperationKind Kind;
        internal bool OriginalCPURequested { get { return false; } }
        internal bool NativeDispatchAuthorized { get { return false; } }
        internal bool NativeAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }

        internal ModSupportIntent(ulong nonce, AiMilitaryCooperationIntent idea,
            int power, long generation, long epoch, long cancellation,
            long tick, long edgeTick, ulong eventId, long expires, string map)
        {
            Nonce = nonce;
            ConsultedDonorAI = idea.Donor;
            RequesterDefendedAI = idea.Recipient;
            Day = idea.Day;
            Priority = idea.Priority;
            Kind = idea.Kind;
            TribePower = power;
            Generation = generation;
            MapEpoch = epoch;
            CancellationSerial = cancellation;
            IssuedTick = tick;
            ReliefEdgeTick = edgeTick;
            ReliefEventId = eventId;
            ExpiresAfterTick = expires;
            MapIdentitySha256 = map;
        }
    }

    internal sealed class ModSupportIntentReceipt
    {
        internal readonly ModSupportIntent Intent;
        internal readonly ModSupportIntentEnd End;
        internal readonly long ObservedTick;
        // Exact request-only preflight is retained even after an owner Tick
        // reclassifies the still-UNRECONCILED native call as expired.
        internal readonly ModSupportPreflightRoute PreflightRoute;
        internal readonly long PreflightRequestTick;
        internal bool NativeAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }
        internal ModSupportIntentReceipt(ModSupportIntent intent,
            ModSupportIntentEnd ending, long tick,
            ModSupportPreflightRoute route, long preflightTick)
        {
            Intent = intent;
            End = ending;
            ObservedTick = tick;
            PreflightRoute = route;
            PreflightRequestTick = preflightTick;
        }
    }

    // No saved-state decoder, transport callback, original order writer or
    // original cancel path exists here. These tickets are never capabilities.
    // The owner serializes all API calls under its EXISTING managed gate.
    // This pure class does not acquire a lock or issue any Native calls.
    internal sealed class ModSupportIntentLifecycle
    {
        internal const int MaxOutstanding = 8, MaxHistory = 32;
        internal const long MaximumLifetimeTicks = 64L;
        private sealed class Relief
        {
            internal bool Known, Emergency;
            internal long LastTick = -1, EdgeTick = -1;
            internal long LastTrueObservationTick = -1;
            internal ulong EventId;
            // Only dispatches consume an event/pair. Each new observed
            // false->true edge resets this bounded (<=7) donor set.
            internal readonly HashSet<byte> DispatchedDonors = new HashSet<byte>();
        }
        private sealed class Pending
        {
            internal ModSupportIntent Intent;
            internal long PreflightRequestTick = -1;
            internal ModSupportPreflightRoute PreflightRoute;
        }
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<ModSupportIntentReceipt> _history =
            new List<ModSupportIntentReceipt>();
        private readonly Dictionary<byte, Relief> _relief =
            new Dictionary<byte, Relief>();
        // Event identity and candidate nonce are independent, monotonically
        // increasing in this process. Neither resets on Cold or new maps.
        private ulong _nextReliefEventId = 1;
        private ulong _nextNonce = 1;
        // restoredCold is a transition signal, not a per-frame state reset.
        // A new Cold requires an observed false then true signal transition.
        private bool _coldBindSignalActive;
        private long _generation = -1, _epoch = -1, _tick = -1;
        private long _cancellation = -1;
        private int _day = -1;
        private string _map;
        internal int PendingCount { get { return _pending.Count; } }
        internal ulong NextNonce { get { return _nextNonce; } }
        internal bool OriginalCPURequested { get { return false; } }
        internal bool NativeDispatchAuthorized { get { return false; } }
        internal bool NativeAck { get { return false; } }
        internal int FavorCredit { get { return 0; } }

        private static bool ValidMap(string text)
        {
            return text != null && text.Length == 64 &&
                text.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        }
        private void End(Pending candidate, ModSupportIntentEnd phase, long tick)
        {
            if (!_pending.Remove(candidate))
                return;
            if (_history.Count == MaxHistory)
                _history.RemoveAt(0);
            _history.Add(new ModSupportIntentReceipt(candidate.Intent, phase, tick,
                candidate.PreflightRoute, candidate.PreflightRequestTick));
        }
        private void EndAll(ModSupportIntentEnd phase, long tick)
        {
            foreach (Pending p in _pending.OrderBy(p => p.Intent.Nonce).ToArray())
                End(p, phase, tick);
        }
        private void Expire(long now)
        {
            foreach (Pending p in _pending.OrderBy(p => p.Intent.Nonce).ToArray())
                if (now > p.Intent.ExpiresAfterTick)
                    End(p, ModSupportIntentEnd.Expired, now);
        }
        private bool Tick(long now)
        {
            if (_generation < 1 || now < 0)
                return false;
            if (now < _tick)
            {
                EndAll(ModSupportIntentEnd.ClockRegressed, _tick);
                _relief.Clear();
                return false;
            }
            _tick = now;
            Expire(now);
            return true;
        }

        // Only an externally verified original map identity may bind this
        // process-local lifecycle. Cold always invalidates all old work.
        // Nonces never reset, including during Cold and map rollover.
        internal bool BindWorld(long generation, long epoch, string map,
            long cancellationSerial, long tick, int day, bool restoredCold)
        {
            if (generation < 1 || epoch < 1 || cancellationSerial < 0 ||
                tick < 0 || day < 1 || !ValidMap(map))
                return false;
            if (_generation == -1 ||
                generation > _generation ||
                generation == _generation && epoch > _epoch)
            {
                EndAll(ModSupportIntentEnd.ForeignWorld, _tick);
                _relief.Clear();
                _coldBindSignalActive = false;
                _generation = generation;
                _epoch = epoch;
                _map = map;
                _cancellation = cancellationSerial;
                _day = day;
                _tick = tick;
            }
            else if (generation != _generation || epoch != _epoch ||
                !String.Equals(_map, map, StringComparison.Ordinal))
            {
                EndAll(ModSupportIntentEnd.ForeignWorld, _tick);
                _relief.Clear();
                return false;
            }
            else if (cancellationSerial != _cancellation)
            {
                EndAll(ModSupportIntentEnd.Cancelled, _tick);
                _relief.Clear();
                if (cancellationSerial < _cancellation)
                    return false;
                _cancellation = cancellationSerial;
            }
            if (day < _day)
            {
                EndAll(ModSupportIntentEnd.ForeignWorld, _tick);
                _relief.Clear();
                return false;
            }
            if (day > _day)
            {
                EndAll(ModSupportIntentEnd.Expired, _tick);
                _relief.Clear();
                _day = day;
            }
            if (!Tick(tick))
                return false;
            if (restoredCold && !_coldBindSignalActive)
            {
                EndAll(ModSupportIntentEnd.ColdInvalidated, tick);
                _relief.Clear(); // first original emergency is baseline only
                _coldBindSignalActive = true;
            }
            else if (!restoredCold)
            {
                // The next actual load must supply a new rising transition.
                _coldBindSignalActive = false;
            }
            return true;
        }

        // A first observation after Cold is a baseline, not a fresh edge.
        // Unknown observations do not invent a calm state. Only a later
        // observed false -> true transition under the same map qualifies.
        internal bool ObserveRelief(byte defendedAI, long now, bool? emergency)
        {
            if (defendedAI < 1 || defendedAI > 8 || !Tick(now) ||
                !emergency.HasValue)
                return false;
            Relief r;
            if (!_relief.TryGetValue(defendedAI, out r))
            {
                r = new Relief();
                _relief.Add(defendedAI, r);
            }
            if (r.Known && now <= r.LastTick)
                return false;
            bool edge = HostDirectedAiTribePolicy.FreshReliefPermitted(
                true, !r.Known, r.Known, r.Emergency, false, emergency.Value);
            r.Known = true;
            r.Emergency = emergency.Value;
            r.LastTick = now;
            if (!emergency.Value)
            {
                r.EventId = 0;
                r.LastTrueObservationTick = -1;
                r.DispatchedDonors.Clear();
                foreach (Pending p in _pending.Where(p =>
                    p.Intent.RequesterDefendedAI == defendedAI).ToArray())
                    End(p, ModSupportIntentEnd.ReliefEnded, now);
            }
            else if (edge)
            {
                if (_nextReliefEventId == UInt64.MaxValue)
                {
                    // Never wrap genuine-event IDs and alias an old dispatch.
                    r.EventId = 0;
                    return false;
                }
                r.EventId = _nextReliefEventId++;
                r.EdgeTick = now;
                r.LastTrueObservationTick = now;
                r.DispatchedDonors.Clear();
            }
            else if (r.EventId != 0)
            {
                // A current independent observation can refresh evidence for
                // this STILL UNDISPATCHED event, not mint a fresh edge.
                r.LastTrueObservationTick = now;
            }
            return edge;
        }

        // Uses the existing strategic intent and shared home reserve; it
        // never substitutes the missing original native order-slot witness.
        internal bool TryQueue(AiMilitaryCooperationIntent idea, int localHuman,
            bool donorAIObserved, bool requesterAIObserved,
            int donorStrength, int castleReserve, int localThreat,
            int proposedTribePower, long now, out ModSupportIntent intent)
        {
            intent = null;
            if (!Tick(now) || idea == null || idea.Kind != AiMilitaryCooperationKind.DefendAlly ||
                idea.Enemy != 0 || idea.Day != _day || idea.Priority < 1 ||
                idea.Donor < 1 || idea.Donor > 8 || idea.Recipient < 1 ||
                idea.Recipient > 8 || idea.Donor == idea.Recipient ||
                localHuman < 1 || localHuman > 8 ||
                idea.Donor == localHuman || idea.Recipient == localHuman ||
                !donorAIObserved || !requesterAIObserved ||
                donorStrength < 1 || castleReserve < 0 || localThreat < 0 ||
                proposedTribePower < 1 || proposedTribePower > donorStrength ||
                now > Int64.MaxValue - MaximumLifetimeTicks ||
                _pending.Count == MaxOutstanding || _nextNonce == UInt64.MaxValue)
                return false;
            if ((long)donorStrength - proposedTribePower <
                MilitaryReservePolicy.RequiredAtHome(castleReserve, localThreat))
                return false;
            Relief r;
            if (!_relief.TryGetValue(idea.Recipient, out r) ||
                !r.Known || !r.Emergency || r.EventId == 0 ||
                !HostDirectedAiTribePolicy.FreshReliefEdgeWithinTicks(
                    now, r.LastTrueObservationTick) ||
                r.DispatchedDonors.Contains(idea.Donor) ||
                _pending.Any(p => p.Intent.ConsultedDonorAI == idea.Donor ||
                    p.Intent.RequesterDefendedAI == idea.Recipient))
                return false;
            intent = new ModSupportIntent(_nextNonce++, idea, proposedTribePower,
                _generation, _epoch, _cancellation, now, r.EdgeTick,
                r.EventId, now + MaximumLifetimeTicks, _map);
            // Queue, native preflight and expiry never consume the relief.
            // A later fresh still-true observation can replan a timed-out
            // candidate with another nonce, within this same genuine event.
            _pending.Add(new Pending { Intent = intent });
            return true;
        }

        private IOrderedEnumerable<Pending> OrderedEntries()
        {
            return _pending.OrderByDescending(p => p.Intent.Priority)
                .ThenBy(p => p.Intent.IssuedTick)
                .ThenBy(p => p.Intent.ConsultedDonorAI)
                .ThenBy(p => p.Intent.RequesterDefendedAI)
                .ThenBy(p => p.Intent.Nonce);
        }

        internal ModSupportIntent[] OrderedPending(long now)
        {
            if (!Tick(now))
                return new ModSupportIntent[0];
            return OrderedEntries().Select(p => p.Intent).ToArray();
        }

        private Pending Find(ulong nonce)
        {
            return _pending.FirstOrDefault(p => p.Intent.Nonce == nonce);
        }

        private bool CurrentUnspentRelief(Pending p, long now)
        {
            Relief r;
            return p != null &&
                _relief.TryGetValue(p.Intent.RequesterDefendedAI, out r) &&
                r.Known && r.Emergency && r.EventId != 0 &&
                r.EventId == p.Intent.ReliefEventId &&
                !r.DispatchedDonors.Contains(p.Intent.ConsultedDonorAI) &&
                HostDirectedAiTribePolicy.FreshReliefEdgeWithinTicks(
                    now, r.LastTrueObservationTick);
        }

        // This only ASKS the independent native owner to run a fresh
        // full original preflight. No true/false here authorizes a command.
        // Missing transport, unknown/busy lease/save fence are retryable.
        // A proven competing original order releases OUR ticket only.
        internal ModSupportPreflight RequestOriginalPreflight(ulong nonce,
            long now, bool? transportAvailable, bool? leaseHeld,
            bool? originalSaveFenceClear, bool? originalOrderSlotUncontested)
        {
            if (!Tick(now))
                return ModSupportPreflight.Waiting;
            Pending p = Find(nonce);
            if (p == null)
                return ModSupportPreflight.Absent;
            // Enforce the same deterministic arbitration as OrderedPending.
            if (!Object.ReferenceEquals(p, OrderedEntries().FirstOrDefault()))
                return ModSupportPreflight.Waiting;
            // A positively observed competing ORIGINAL order releases only
            // our ticket even if its last relief observation is now stale.
            if (originalOrderSlotUncontested == false)
            {
                End(p, ModSupportIntentEnd.OriginalOrderConflict, now);
                return ModSupportPreflight.ReleasedOriginalConflict;
            }
            if (!CurrentUnspentRelief(p, now))
                return ModSupportPreflight.Waiting;
            if (transportAvailable != true || leaseHeld != true ||
                originalSaveFenceClear != true ||
                originalOrderSlotUncontested != true)
                return ModSupportPreflight.Waiting;
            p.PreflightRoute = ModSupportPreflightRoute.DirectTribe;
            p.PreflightRequestTick = now;
            return ModSupportPreflight.RequestIndependentNativePreflight;
        }

        // REQUEST-ONLY review: reuses our existing planner's stable nonce,
        // false->true relief event, priority, expiry and one-attempt journal.
        // Its original native evaluator does NOT require a globally empty CPU
        // tribe queue. This is NOT the direct order sender or a native call.
        // Presently an independently attested original request owner/seam is
        // absent, so the live path remains retryable/unarmed.
        internal ModSupportPreflight RequestOriginalEvaluatorPreflight(
            ulong nonce, long now, OriginalAllyRequestReviewFacts facts)
        {
            if (!Tick(now))
                return ModSupportPreflight.Waiting;
            Pending p = Find(nonce);
            if (p == null)
                return ModSupportPreflight.Absent;
            if (!Object.ReferenceEquals(p, OrderedEntries().FirstOrDefault()) ||
                !CurrentUnspentRelief(p, now))
                return ModSupportPreflight.Waiting;
            OriginalAllyRequestReview review =
                OriginalAllyRequestReviewPolicy.Review(p.Intent, facts, now);
            if (review.Stage !=
                OriginalAllyRequestReviewStage.EligibleForIndependentOriginalEvaluatorReview)
                return ModSupportPreflight.Waiting;
            // This requests a distinct independent original owner preflight.
            // It never calls any game API, original evaluator, or Chore producer.
            p.PreflightRoute = ModSupportPreflightRoute.OriginalAllyEvaluator;
            p.PreflightRequestTick = now;
            return ModSupportPreflight.RequestIndependentNativePreflight;
        }

        // Only a genuinely observed external dispatch-begin consumes the
        // existing relief event/pair. Wrong routes cannot mark one another.
        // An external evaluator denial before a call or an unproven transport
        // is NOT a dispatch-begin and leaves the MOD request retryable.
        private bool NoteExternalDispatchBeganForRoute(ulong nonce, long now,
            ModSupportPreflightRoute expectedRoute)
        {
            if (!Tick(now))
                return false;
            Pending p = Find(nonce);
            if (p == null || p.PreflightRoute != expectedRoute ||
                p.PreflightRequestTick != now ||
                !Object.ReferenceEquals(p, OrderedEntries().FirstOrDefault()) ||
                !CurrentUnspentRelief(p, now))
                return false;
            // Not a native ACK, evaluator acceptance, troop movement,
            // success, favor credit or original CPU-authored request.
            _relief[p.Intent.RequesterDefendedAI].DispatchedDonors.Add(
                p.Intent.ConsultedDonorAI);
            End(p, ModSupportIntentEnd.DispatchBeganUnconfirmed, now);
            return true;
        }

        // Backwards-compatible direct tribe notification (still UNARMED).
        internal bool NoteExternalDispatchBegan(ulong nonce, long now)
        {
            return NoteExternalDispatchBeganForRoute(nonce, now,
                ModSupportPreflightRoute.DirectTribe);
        }

        // Distinct original evaluator REQUEST notification. The independent
        // owner must prove that a request REALLY began; reviewing/asking for a
        // preflight or observing a wrapper Boolean is never enough.
        internal bool NoteExternalOriginalSupportRequestBegan(ulong nonce, long now)
        {
            return NoteExternalDispatchBeganForRoute(nonce, now,
                ModSupportPreflightRoute.OriginalAllyEvaluator);
        }

        // A real native BEGIN is an immutable process-local observation:
        // reconcile on a LATER owner tick, never inside original threadLock.
        // IMPORTANT: do NOT call Tick(now) (which expires pending) before
        // matching the original preflight at its actual BEGIN tick.
        // A later Expired/Cold/ForeignWorld receipt is corrected in the SAME
        // existing bounded history slot; no second journal or native ACK.
        internal bool NoteExternalOriginalSupportBeginFollowingTick(
            ulong nonce, long originalBeginTick, long now,
            long generation, long epoch, long cancellation, string map)
        {
            if (nonce == 0 || generation < 1 || epoch < 1 ||
                cancellation < 0 || originalBeginTick < 0 ||
                now <= originalBeginTick || !ValidMap(map))
                return false;
            Pending p = Find(nonce);
            if (p != null &&
                p.PreflightRoute == ModSupportPreflightRoute.OriginalAllyEvaluator &&
                p.PreflightRequestTick == originalBeginTick &&
                SameRealBegin(p.Intent, nonce, originalBeginTick,
                    generation, epoch, cancellation, map))
            {
                // Native call ALREADY began before expiry. A subsequent
                // Tick/Expire may not erase or downgrade this real attempt.
                SpendCurrentOriginalReliefIfStillSame(p.Intent);
                End(p, ModSupportIntentEnd.DispatchBeganUnconfirmed,
                    originalBeginTick);
                if (_generation == generation && _epoch == epoch &&
                    _cancellation == cancellation &&
                    String.Equals(_map, map, StringComparison.Ordinal) &&
                    now >= _tick)
                    Tick(now); // Only AFTER recording the begun attempt.
                return true;
            }
            // Tick, Cold, cancellation, day or map rollover might already have
            // archived an un-reconciled request. Original preflight route and
            // preflight tick are frozen in that receipt for exact correlation.
            for (int i = _history.Count - 1; i >= 0; --i)
            {
                ModSupportIntentReceipt previous = _history[i];
                if (previous == null ||
                    previous.PreflightRoute !=
                        ModSupportPreflightRoute.OriginalAllyEvaluator ||
                    previous.PreflightRequestTick != originalBeginTick ||
                    !SameRealBegin(previous.Intent, nonce, originalBeginTick,
                        generation, epoch, cancellation, map))
                    continue;
                if (previous.End == ModSupportIntentEnd.DispatchBeganUnconfirmed)
                    return false; // exactly once
                SpendCurrentOriginalReliefIfStillSame(previous.Intent);
                _history[i] = new ModSupportIntentReceipt(
                    previous.Intent, ModSupportIntentEnd.DispatchBeganUnconfirmed,
                    originalBeginTick, previous.PreflightRoute,
                    previous.PreflightRequestTick);
                return true;
            }
            // The bridge still retains its immutable BEGIN observation even
            // if bounded history was evicted; do not blind-resend.
            return false;
        }

        private static bool SameRealBegin(ModSupportIntent intent,
            ulong nonce, long began, long generation, long epoch,
            long cancellation, string map)
        {
            return intent != null && intent.Nonce == nonce && nonce != 0 &&
                intent.Generation == generation &&
                intent.MapEpoch == epoch &&
                intent.CancellationSerial == cancellation &&
                intent.IssuedTick <= began &&
                began <= intent.ExpiresAfterTick &&
                String.Equals(intent.MapIdentitySha256, map,
                    StringComparison.Ordinal);
        }

        private void SpendCurrentOriginalReliefIfStillSame(ModSupportIntent intent)
        {
            Relief current;
            if (intent != null && _generation == intent.Generation &&
                _epoch == intent.MapEpoch &&
                _cancellation == intent.CancellationSerial &&
                String.Equals(_map, intent.MapIdentitySha256,
                    StringComparison.Ordinal) &&
                _relief.TryGetValue(intent.RequesterDefendedAI, out current) &&
                current.EventId == intent.ReliefEventId)
                current.DispatchedDonors.Add(intent.ConsultedDonorAI);
        }

        internal bool ReleaseOwnIntent(ulong nonce, long now, bool originalConflict)
        {
            if (!Tick(now))
                return false;
            Pending p = Find(nonce);
            if (p == null)
                return false;
            End(p, originalConflict ? ModSupportIntentEnd.OriginalOrderConflict :
                ModSupportIntentEnd.Cancelled, now);
            return true; // NEVER cancels an original order
        }

        internal ModSupportIntentReceipt[] RecentHistory()
        {
            return _history.ToArray();
        }
        // Deliberately no ACK, movement success, replay or favor-credit API.
    }
}
#endif
