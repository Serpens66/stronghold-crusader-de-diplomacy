// PRIVATE bounded human-first arbitration against ACTUAL existing routed
// pending/inflight human ally requests, plus official native donor ally mode.
// No global autonomous CPU-tribe pending queue requirement or raw Chore read.
// An unattested native/local/remote future Chore113 is NOT "proved absent".
// No opposite-order lock acquisition from the original simulation callback.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalAllyHumanPriorityFrame
    {
        internal readonly DiplomacyFeature Owner;
        internal readonly ulong ModNonce;
        internal readonly long Generation, Epoch, Cancellation, Tick;
        internal readonly string MapSha256;
        internal readonly byte Donor, Defended;

        internal OriginalAllyHumanPriorityFrame(DiplomacyFeature owner,
            OriginalSupportRequestTicket ticket, long tick)
        {
            Owner = owner; ModNonce = ticket.Nonce;
            Generation = ticket.Generation; Epoch = ticket.MapEpoch;
            Cancellation = ticket.CancellationSerial;
            Tick = tick; MapSha256 = ticket.MapSha256;
            Donor = ticket.DonorAI; Defended = ticket.DefendedAI;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // No lock acquired here: reconciliation is admitted only from the
        // existing owning MOD gate and a live original owner reference.
        internal bool IsOriginalSupportModOwnerGateHeld()
        {
            return Monitor.IsEntered(_gate) &&
                Object.ReferenceEquals(Volatile.Read(ref _activeOwner), this) &&
                _mapActive && !_mapDisabled;
        }

        // Called ONLY by the real MOD owner during its existing managed _gate
        // scope and original native engine monitor lease, never by the original
        // simulation callback. This method does not enter either lock.
        // A caller-supplied boolean can NEVER manufacture this certificate.
        internal bool TryIssueOriginalAllyPriorityFrame(
            OriginalSupportRequestTicket ticket, long originalTick,
            out OriginalAllyHumanPriorityFrame result)
        {
            result = null;
            if (ticket == null || ticket.Nonce == 0 ||
                !Object.ReferenceEquals(Volatile.Read(ref _activeOwner), this) ||
                !Monitor.IsEntered(_gate) || !IsNativeDiplomacySectionHeld ||
                !_mapActive || _mapDisabled || _mapBootstrapPending ||
                _inventoryRestorePending || _pendingPackage != null ||
                _nativeTransferProbe != null ||
                _sessionGeneration != ticket.Generation ||
                _mapEpoch != ticket.MapEpoch ||
                !String.Equals(_mapFingerprint, ticket.MapSha256,
                    StringComparison.Ordinal) ||
                originalTick < ticket.ObservedTick ||
                originalTick > ticket.ExpiresAfterTick ||
                ticket.DonorAI < 1 || ticket.DonorAI > 8 ||
                ticket.DefendedAI < 1 || ticket.DefendedAI > 8 ||
                ticket.DonorAI == ticket.DefendedAI)
                return false;

            int donor = ticket.DonorAI;
            // The two arrays are the ACTUAL captured human HUD requests in
            // HumanAllyOrderPipeline; any present value, even stale/unknown,
            // blocks this MOD request. No claim about non-MOD native Chores.
            if (Volatile.Read(ref _allyOrders[donor]) != null ||
                Volatile.Read(ref _allyInFlight[donor]) != null)
                return false;
            // Native source is exact, typed, and checked by pinned compatibility:
            // this also requires the original current donor ally mode = 0,
            // current original session and reciprocal native AI alliance.
            if (!OriginalSupportLiveSoloAudit.TryVerifyUnderOriginalMonitor(
                    ticket, originalTick))
                return false;
            // Recheck the human cells after the (possibly long) managed/native
            // read; a simultaneous UI click must not be silently displaced.
            if (Volatile.Read(ref _allyOrders[donor]) != null ||
                Volatile.Read(ref _allyInFlight[donor]) != null)
                return false;
            result = new OriginalAllyHumanPriorityFrame(this, ticket, originalTick);
            return true;
        }

        // Zero allocations and NO _gate enter on original simulation lane.
        // Only identity and *currently* captured human slots are read.
        // Native ally mode and original SDK roles are rechecked separately by
        // OriginalSupportLiveSoloAudit under genuine EngineInterface.threadLock.
        // This check is not a general native Chore113 queue reader.
        internal bool OriginalHumanPriorityStillCurrentOnSimulationLane(
            OriginalAllyHumanPriorityFrame frame,
            OriginalSupportRequestTicket ticket, long originalTick)
        {
            if (frame == null || ticket == null ||
                !Object.ReferenceEquals(frame.Owner, this) ||
                !Object.ReferenceEquals(Volatile.Read(ref _activeOwner), this) ||
                !IsNativeDiplomacySectionHeld ||
                frame.Tick != originalTick ||
                frame.ModNonce != ticket.Nonce ||
                frame.Donor != ticket.DonorAI ||
                frame.Defended != ticket.DefendedAI ||
                frame.Generation != ticket.Generation ||
                frame.Epoch != ticket.MapEpoch ||
                frame.Cancellation != ticket.CancellationSerial ||
                _sessionGeneration != ticket.Generation ||
                _mapEpoch != ticket.MapEpoch ||
                !String.Equals(frame.MapSha256, ticket.MapSha256,
                    StringComparison.Ordinal) ||
                !String.Equals(Volatile.Read(ref _mapFingerprint), ticket.MapSha256,
                    StringComparison.Ordinal) ||
                !Volatile.Read(ref _mapActive) ||
                Volatile.Read(ref _mapDisabled) ||
                Volatile.Read(ref _mapBootstrapPending) ||
                Volatile.Read(ref _inventoryRestorePending) ||
                _pendingPackage != null || _nativeTransferProbe != null)
                return false;
            return Volatile.Read(ref _allyOrders[ticket.DonorAI]) == null &&
                Volatile.Read(ref _allyInFlight[ticket.DonorAI]) == null;
        }
    }
}
#endif
