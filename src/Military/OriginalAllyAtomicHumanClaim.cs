// Private original request HUMAN-FIRST claim. A zero-timeout claim under
// the EXISTING human HUD capture gate is the registration linearization point.
// Do not hold that gate across the native original consumer. A later genuine
// human click stays in the normal pending slot until claim release.
// This is NOT a public host flag, original CPU author or native ACK.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalSupportNativeClaim
    {
        internal readonly DiplomacyFeature Owner;
        internal readonly OriginalAllyHumanPriorityFrame Priority;
        internal readonly ulong Nonce;
        internal readonly byte Donor;
        internal readonly long Tick;
        internal readonly int ThreadId;
        internal int Released;

        internal OriginalSupportNativeClaim(DiplomacyFeature owner,
            OriginalAllyHumanPriorityFrame priority,
            OriginalSupportRequestTicket ticket, long tick)
        {
            Owner = owner;
            Priority = priority;
            Nonce = ticket.Nonce;
            Donor = ticket.DonorAI;
            Tick = tick;
            ThreadId = Thread.CurrentThread.ManagedThreadId;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private readonly OriginalSupportNativeClaim[] _originalSupportNativeClaims =
            new OriginalSupportNativeClaim[9];

        // Real and fake paths share THIS exact atomic capture-gate primitive.
        // It never calls native/GameAction and never grants native authority.
        // Allocate before entering the brief human lock, not inside the gate.
        private bool TryClaimKnownHumanSlot(
            OriginalAllyHumanPriorityFrame frame,
            OriginalSupportRequestTicket ticket, long tick,
            out OriginalSupportNativeClaim claim)
        {
            claim = null;
            if (frame == null || ticket == null ||
                !Object.ReferenceEquals(frame.Owner, this) ||
                frame.ModNonce != ticket.Nonce || frame.Tick != tick ||
                ticket.DonorAI < 1 || ticket.DonorAI > 8)
                return false;
            OriginalSupportNativeClaim proposal =
                new OriginalSupportNativeClaim(this, frame, ticket, tick);
            bool entered = false;
            try
            {
                // Monitor is reentrant, so explicitly exclude a native
                // callback entered while the HUD capture thread holds it.
                if (Monitor.IsEntered(_humanCaptureGate))
                    return false;
                Monitor.TryEnter(_humanCaptureGate, 0, ref entered);
                if (!entered)
                    return false;
                int donor = ticket.DonorAI;
                if (Volatile.Read(ref _allyOrders[donor]) != null ||
                    Volatile.Read(ref _allyInFlight[donor]) != null ||
                    Volatile.Read(ref _originalSupportNativeClaims[donor]) != null)
                    return false;
                // EXACT atomic native-claim linearization against real
                // AllyGameActionPrefix registration under the SAME monitor.
                Volatile.Write(ref _originalSupportNativeClaims[donor], proposal);
                claim = proposal;
                return true;
            }
            catch { return false; }
            finally
            {
                if (entered)
                    Monitor.Exit(_humanCaptureGate);
            }
        }

        // The real path requires an independently attested original sim lane,
        // native monitor, current donor AllyMode/roles and existing owner frame.
        // This method NEVER enters the MOD _gate on the sim thread.
        internal bool TryAcquireOriginalSupportNativeClaim(
            OriginalAllyHumanPriorityFrame frame,
            OriginalSupportRequestTicket ticket, long tick,
            out OriginalSupportNativeClaim claim)
        {
            claim = null;
            if (!OriginalHumanPriorityStillCurrentOnSimulationLane(
                    frame, ticket, tick) ||
                !OriginalSupportLiveSoloAudit.TryVerifyUnderOriginalMonitor(
                    ticket, tick))
                return false;
            return TryClaimKnownHumanSlot(frame, ticket, tick, out claim);
        }

        // After claim, later HUMAN clicks are expected and MUST NOT revoke
        // the claim. Check only token/scope; do not demand empty pending array.
        internal bool OriginalSupportNativeClaimStillCurrent(
            OriginalSupportNativeClaim claim, OriginalSupportRequestTicket ticket,
            long tick)
        {
            return claim != null && ticket != null &&
                ticket.DonorAI >= 1 && ticket.DonorAI <= 8 &&
                Object.ReferenceEquals(claim.Owner, this) &&
                claim.Nonce == ticket.Nonce &&
                claim.Donor == ticket.DonorAI &&
                claim.Tick == tick &&
                claim.ThreadId == Thread.CurrentThread.ManagedThreadId &&
                Volatile.Read(ref claim.Released) == 0 &&
                Object.ReferenceEquals(
                    Volatile.Read(ref _originalSupportNativeClaims[ticket.DonorAI]),
                    claim) &&
                Object.ReferenceEquals(Volatile.Read(ref _activeOwner), this) &&
                IsNativeDiplomacySectionHeld &&
                Volatile.Read(ref _sessionGeneration) == ticket.Generation &&
                Volatile.Read(ref _mapEpoch) == ticket.MapEpoch &&
                String.Equals(Volatile.Read(ref _mapFingerprint), ticket.MapSha256,
                    StringComparison.Ordinal) &&
                Volatile.Read(ref _mapActive) &&
                !Volatile.Read(ref _mapDisabled) &&
                !Volatile.Read(ref _mapBootstrapPending) &&
                !Volatile.Read(ref _inventoryRestorePending);
        }

        // Invoked in bridge finally, regardless of void return/exception.
        // No wait or opposite-order model lock. Queued late humans survive.
        internal void ReleaseOriginalSupportNativeClaim(
            OriginalSupportNativeClaim claim)
        {
            if (claim == null || !Object.ReferenceEquals(claim.Owner, this) ||
                claim.Donor < 1 || claim.Donor > 8)
                return;
            if (Interlocked.CompareExchange(
                    ref _originalSupportNativeClaims[claim.Donor], null, claim) == claim)
                Interlocked.Exchange(ref claim.Released, 1);
        }
    }
}
#endif
