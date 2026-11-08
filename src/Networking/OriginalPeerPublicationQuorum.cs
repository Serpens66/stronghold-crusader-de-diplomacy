// ephemeral phase1/phase2 QUORUM around the existing ONE
// globally host-signed publication Chore. NO second world frame/cache,
// no SaveData hook and no native settlement permission.
// Phase1 CandidateReady receipts are UNSIGNED Steam packets KIND9, admitted
// only on the EXISTING official sender-providing R3 Packet1902 callback,
// bound to original live Steam->player slot and exact saved owner readback
// digest. Phase2 OwnerReadbackApplied echoes require that the signed Kind10
// Chore has already been consumed by the ORIGINAL ChoreSession.
// Quorum is a transport barrier, NOT a self-authenticating game state.
// Its caller (the coordinator) must provide REAL original-monitored facts
// and the controller's actual serialized schema19 readback, not booleans.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalPeerPublicationQuorum
    {
        private readonly TrustedContext _context;
        private readonly Bytes _session;
        private readonly PublicationFrameBinding _binding;
        private readonly ulong[] _steamBySlot;
        private readonly HashSet<ulong> _expected;
        private readonly Dictionary<ulong, byte[]> _ready = new Dictionary<ulong, byte[]>();
        private readonly Dictionary<ulong, byte[]> _applied = new Dictionary<ulong, byte[]>();
        private SignedPublicationBarrier _signedBarrier;
        private bool _revoked;
        private ulong _locallyConsumedBarrierSequence;
        internal bool IsRevoked
        {
            get
            {
                return _revoked;
            }
        }

        internal int ReadyHumans
        {
            get
            {
                return _ready.Count;
            }
        }

        internal int AppliedHumans
        {
            get
            {
                return _applied.Count;
            }
        }

        internal int ExpectedHumans
        {
            get
            {
                return _expected.Count;
            }
        }

        internal PublicationFrameBinding Binding
        {
            get
            {
                return _binding;
            }
        }

        internal OriginalPeerPublicationQuorum(TrustedContext originalHostOnlyContext, SessionDescriptor pinnedOriginalHostSession, ulong[] actuallyObservedOriginalNativeSteamBySlot, PublicationFrameBinding controllerProducedExactCandidate)
        {
            if (originalHostOnlyContext == null || pinnedOriginalHostSession == null || actuallyObservedOriginalNativeSteamBySlot == null || actuallyObservedOriginalNativeSteamBySlot.Length != 9 || controllerProducedExactCandidate == null || originalHostOnlyContext.HostSteamId <= 1000 || originalHostOnlyContext.LocalSteamId != originalHostOnlyContext.HostSteamId || originalHostOnlyContext.Generation < 1 || originalHostOnlyContext.Scope == null || originalHostOnlyContext.Scope.Length != 128 || !controllerProducedExactCandidate.SessionId.Same(pinnedOriginalHostSession.Id) || !pinnedOriginalHostSession.Scope.Same(originalHostOnlyContext.Scope))
                throw new InvalidDataException("No original host-signed candidate/readback quorum");
            _context = originalHostOnlyContext;
            _session = new Bytes(pinnedOriginalHostSession.Id.Copy());
            _binding = controllerProducedExactCandidate;
            _steamBySlot = (ulong[])actuallyObservedOriginalNativeSteamBySlot.Clone();
            if (_steamBySlot[0] != 0 || _steamBySlot.Skip(1).Any(x => x != 0 && x <= 1000))
                throw new InvalidDataException("Reserved original slot is not empty");
            var ids = _steamBySlot.Skip(1).Where(x => x > 1000).OrderBy(x => x).ToArray();
            var expected = originalHostOnlyContext.HumanIds();
            if (ids.Length < 2 || ids.Length != expected.Length || !ids.SequenceEqual(expected) || ids.Distinct().Count() != ids.Length)
                throw new InvalidDataException("Original host Steam roster incomplete");
            _expected = new HashSet<ulong>(ids);
        }

        internal void Revoke()
        {
            _revoked = true;
            _ready.Clear();
            _applied.Clear();
            _signedBarrier = null;
            _locallyConsumedBarrierSequence = 0;
        }

        private AdmissionException Bad(AdmissionReason reason)
        {
            return new AdmissionException(reason);
        }

        internal void ReceiveOriginalAuthenticatedPeerReceipt(PeerPublicationAcknowledgement receipt, SteamDelivery actualOfficialRawraSender, Func<byte, ulong, bool> currentOriginalLiveSlotMapping)
        {
            if (_revoked)
                throw Bad(AdmissionReason.session_invalidated);
            if (receipt == null || actualOfficialRawraSender == null || currentOriginalLiveSlotMapping == null || actualOfficialRawraSender.Generation != _context.Generation)
                throw Bad(AdmissionReason.stale_transport_context);
            ulong steam = actualOfficialRawraSender.SenderSteamId;
            if (steam == _context.HostSteamId || !_expected.Contains(steam) || receipt.ActorSlot < 1 || receipt.ActorSlot > 8 || _steamBySlot[receipt.ActorSlot] != steam || !currentOriginalLiveSlotMapping(receipt.ActorSlot, steam))
                throw Bad(AdmissionReason.sender_not_participant);
            AcceptActualSourceReceipt(receipt, steam);
        }

        // Host-only original UI/player-identity proof is NOT a synthetic
        // Steam self-message. Must come from the controller's same
        // serialized owner state under the existing original monitor.
        internal void ReceiveOriginalOwnerLocalReceipt(PeerPublicationAcknowledgement receipt, byte actualHostNativeSlot, ulong realHostSteamId)
        {
            if (_revoked)
                throw Bad(AdmissionReason.session_invalidated);
            if (receipt == null || actualHostNativeSlot < 1 || actualHostNativeSlot > 8 || realHostSteamId != _context.HostSteamId || _steamBySlot[actualHostNativeSlot] != realHostSteamId || receipt.ActorSlot != actualHostNativeSlot)
                throw Bad(AdmissionReason.sender_not_host);
            AcceptActualSourceReceipt(receipt, realHostSteamId);
        }

        private void AcceptActualSourceReceipt(PeerPublicationAcknowledgement receipt, ulong steam)
        {
            // A completed older round may still be retrying asynchronously.
            // Authenticated stale readiness has no authority and cannot revoke
            // the exact current active round. Future/conflicting current facts fail.
            if (receipt.Binding.SessionId.Same(_session) && receipt.Binding.FrameChoreSequence < _binding.FrameChoreSequence)
                return;
            if (!_binding.Same(receipt.Binding))
            {
                // A real authenticated sender with conflicting canonical
                // owner candidate/readback belongs to a divergent peer.
                Revoke();
                throw Bad(AdmissionReason.wrong_scope);
            }

            var witness = (byte[])receipt.EncodeBody().Clone();
            if (receipt.Phase == PublicationReceiptPhase.CandidateReady)
            {
                if (_signedBarrier != null && !_ready.ContainsKey(steam))
                    throw Bad(AdmissionReason.invalid_command);
                AddUniqueOrIdempotent(_ready, steam, witness);
            }
            else
            {
                if (_signedBarrier == null || _locallyConsumedBarrierSequence != _signedBarrier.GlobalChoreSequence || receipt.PublishBarrierChoreSequence != _signedBarrier.GlobalChoreSequence || !_ready.ContainsKey(steam))
                    throw Bad(AdmissionReason.invalid_command);
                AddUniqueOrIdempotent(_applied, steam, witness);
            }
        }

        private void AddUniqueOrIdempotent(Dictionary<ulong, byte[]> list, ulong steam, byte[] packet)
        {
            byte[] prev;
            if (list.TryGetValue(steam, out prev))
            {
                if (!prev.SequenceEqual(packet))
                {
                    Revoke();
                    throw Bad(AdmissionReason.session_invalidated);
                }

                return;
            }

            list.Add(steam, packet);
        }

        // This is a READABLE checklist for an already authenticated host,
        // NOT proof of the original state by itself. The coordinator signs
        // only an exactly binding Kind10 after these actual receipts.
        internal bool AllActualOriginalHumansReady
        {
            get
            {
                return !_revoked && _signedBarrier == null && _ready.Count == _expected.Count && _expected.All(x => _ready.ContainsKey(x));
            }
        }

        internal void BindExistingHostSignedPublicationBarrier(SignedPublicationBarrier signed)
        {
            if (!AllActualOriginalHumansReady || signed == null || !_binding.Same(signed.Binding))
                throw Bad(AdmissionReason.bootstrap_incomplete);
            _signedBarrier = signed;
        }

        // Invoke ONLY from the same RSA-verified original managed Barrier
        // reducer, AFTER actual controller owner publication/readback.
        // Typed signature or receipt objects alone cannot set this frontier.
        internal void ConfirmOriginalLocallyConsumedBarrier(SignedPublicationBarrier actuallyVerifiedBarrier, ulong actualOriginalChoreConsumedSequence, byte[] actualLocalOwnerReadbackSha256)
        {
            if (_revoked || _signedBarrier == null || actuallyVerifiedBarrier == null || actualOriginalChoreConsumedSequence != _signedBarrier.GlobalChoreSequence || actuallyVerifiedBarrier.GlobalChoreSequence != actualOriginalChoreConsumedSequence || !_signedBarrier.Binding.Same(actuallyVerifiedBarrier.Binding) || !PublicationFrameBinding.Same32(actualLocalOwnerReadbackSha256, _binding.OwnerReadbackSha()))
                throw Bad(AdmissionReason.session_invalidated);
            _locallyConsumedBarrierSequence = actualOriginalChoreConsumedSequence;
        }

        internal SignedPublicationBarrier ExistingBarrier
        {
            get
            {
                return _signedBarrier;
            }
        }

        internal bool AllActualOriginalHumansApplied
        {
            get
            {
                return !_revoked && _signedBarrier != null && _locallyConsumedBarrierSequence == _signedBarrier.GlobalChoreSequence && _applied.Count == _expected.Count && _expected.All(x => _applied.ContainsKey(x));
            }
        }

        internal void RequireAllAppliedForNextSignedHostAction(Bytes originalSession, SignedPublicationBarrier exactBarrier, byte[] actualControllerLocalSerializedReadbackSha256)
        {
            if (!AllActualOriginalHumansApplied || originalSession == null || !originalSession.Same(_session) || exactBarrier == null || !Object.ReferenceEquals(exactBarrier, _signedBarrier) || !PublicationFrameBinding.Same32(actualControllerLocalSerializedReadbackSha256, _binding.OwnerReadbackSha()))
                throw Bad(AdmissionReason.session_invalidated);
        }
    }
}
