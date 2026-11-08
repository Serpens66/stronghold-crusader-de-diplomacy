// PRIVATE original defend evaluator correlation: a bounded synchronous
// ThreadStatic call-span ticket, never a native ACK or a direct tribe order.
// Called from the EXISTING SHCDESE defend Pre/Post subscription, which must
// already be installed and must not be re-registered for this feature.
// No locks, allocations, game state/native/UI access or logging in Observe.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal enum OriginalSupportHookObservation : byte
    {
        None = 0,
        PreOnly = 1,
        PreSkippedOrMismatched = 2,
        SynchronousPostValueObserved = 3,
        DuplicateOrUnbound = 4
    }

    internal sealed class OriginalSupportHookSpan
    {
        internal readonly OriginalSupportRequestTicket Ticket;
        internal readonly int NativeThreadId, Donor, Defended;
        // Callback modifications are only primitive; no owner locks.
        internal int PreSeen, PreSkipped, PostSeen, Mismatch, Duplicate;
        internal int MutablePostValue;
        internal bool IsClosed;

        internal OriginalSupportHookSpan(OriginalSupportRequestTicket ticket)
            : this(ticket.DonorAI, ticket.DefendedAI)
        {
            Ticket = ticket;
        }

        internal OriginalSupportHookSpan(int donor, int defended)
        {
            Donor = donor;
            Defended = defended;
            NativeThreadId = Thread.CurrentThread.ManagedThreadId;
        }
        internal OriginalSupportHookObservation Classification
        {
            get
            {
                if (Mismatch != 0 || Duplicate != 0)
                    return OriginalSupportHookObservation.DuplicateOrUnbound;
                if (PreSkipped != 0)
                    return OriginalSupportHookObservation.PreSkippedOrMismatched;
                if (PreSeen == 1 && PostSeen == 1 && !IsClosed)
                    return OriginalSupportHookObservation.SynchronousPostValueObserved;
                if (PreSeen == 1)
                    return OriginalSupportHookObservation.PreOnly;
                return OriginalSupportHookObservation.None;
            }
        }
    }

    internal sealed class OriginalSupportHookReceipt
    {
        internal readonly ulong ModNonce, ReliefEventId;
        internal readonly long Generation, MapEpoch, CancellationSerial, OriginalTick;
        internal readonly OriginalSupportHookObservation Observation;
        internal readonly bool? MutableObservedPostValue;
        internal bool ImmutableOriginalEvaluatorDecision { get { return false; } }
        internal bool NativeOrderAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }

        internal OriginalSupportHookReceipt(OriginalSupportHookSpan span, long originalTick)
        {
            ModNonce = span.Ticket.Nonce;
            ReliefEventId = span.Ticket.ReliefEventId;
            Generation = span.Ticket.Generation;
            MapEpoch = span.Ticket.MapEpoch;
            CancellationSerial = span.Ticket.CancellationSerial;
            OriginalTick = originalTick;
            Observation = span.Classification;
            MutableObservedPostValue =
                Observation == OriginalSupportHookObservation.SynchronousPostValueObserved
                ? (bool?)(span.MutablePostValue == 1) : null;
        }
    }

    internal static class OriginalAllySynchronousEvaluatorCapture
    {
        [ThreadStatic] private static OriginalSupportHookSpan _active;

        // Entered immediately before the EXACT private typed original void
        // consumer call; a nested/reentrant entry is forbidden, not overwritten.
        internal static OriginalSupportHookSpan Begin(OriginalSupportRequestTicket ticket)
        {
            if (ticket == null || ticket.Nonce == 0 || ticket.ReliefEventId == 0 ||
                _active != null)
                return null;
            OriginalSupportHookSpan span = new OriginalSupportHookSpan(ticket);
            _active = span;
            return span;
        }

        internal static OriginalSupportHookSpan Begin(int donor, int defended)
        {
            if (donor < 1 || donor > 8 || defended < 1 || defended > 8 ||
                donor == defended || _active != null)
                return null;
            var span = new OriginalSupportHookSpan(donor, defended);
            _active = span;
            return span;
        }

        internal static bool? EndObservedDecision(OriginalSupportHookSpan span)
        {
            if (span == null || !Object.ReferenceEquals(_active, span))
                return null;
            bool? decision = span.Classification == OriginalSupportHookObservation.SynchronousPostValueObserved
                ? (bool?)(span.MutablePostValue == 1) : null;
            span.IsClosed = true;
            _active = null;
            return decision;
        }

        // Observe only the ACTUAL existing subscriber's scalar hook fields.
        // Phase 0=Pre, 1=Post. Capturing Post does NOT prove immutable native
        // acceptance: hook ReturnValue/SkipOriginalFunction may be mutated by
        // other subscriptions. An unbound, PreSkip or duplicate is not evidence.
        internal static void Observe(int phase, bool skipOriginal,
            int consultedDonor, int defendedRequester, bool returnValue)
        {
            try
            {
                OriginalSupportHookSpan span = _active;
                if (span == null || span.IsClosed ||
                    span.NativeThreadId != Thread.CurrentThread.ManagedThreadId)
                    return;
                if (consultedDonor != span.Donor || defendedRequester != span.Defended)
                {
                    span.Mismatch = 1;
                    return;
                }
                if (phase == 0)
                {
                    if (span.PreSeen != 0 || span.PostSeen != 0)
                        span.Duplicate = 1;
                    else
                        span.PreSeen = 1;
                    if (skipOriginal)
                        span.PreSkipped = 1;
                }
                else if (phase == 1)
                {
                    if (span.PostSeen != 0 || span.PreSeen != 1)
                        span.Duplicate = 1;
                    else
                    {
                        span.PostSeen = 1;
                        if (skipOriginal)
                            span.PreSkipped = 1;
                        span.MutablePostValue = returnValue ? 1 : 0;
                    }
                }
                else
                    span.Mismatch = 1;
            }
            catch
            {
                // Never leak managed exceptions through a native hook.
            }
        }

        // Consume synchronously while the scope is STILL active, before
        // native call unwinds. The receipt is a detached managed MOD record;
        // a native void return alone remains unconfirmed.
        internal static OriginalSupportHookReceipt End(
            OriginalSupportHookSpan span, long originalTick)
        {
            if (span == null || !Object.ReferenceEquals(_active, span) ||
                span.NativeThreadId != Thread.CurrentThread.ManagedThreadId)
                return null;
            OriginalSupportHookReceipt result =
                new OriginalSupportHookReceipt(span, originalTick);
            span.IsClosed = true;
            _active = null;
            return result;
        }
    }
}
#endif
