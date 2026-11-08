// Call only from the existing PumpSignedNetwork managed inbox under
// original EngineInterface.threadLock. This is a ROUTER, NOT a packet
// registration, Harmony patch or additional Steam/Save lifecycle owner.
using System;

namespace StrongholdEuropeDE.Runtime.Networking
{
    internal sealed class PersistedHumanActionCursor
    {
        internal readonly ulong Epoch, LastSequence, LastNonce;
        internal PersistedHumanActionCursor(ulong canonicalInputEpoch, ulong lastPersistedActorSequence, ulong lastPersistedActorNonce)
        {
            Epoch = canonicalInputEpoch;
            LastSequence = lastPersistedActorSequence;
            LastNonce = lastPersistedActorNonce;
        }
    }

    internal static class AuthenticatedTradeIngressRouter
    {
        // readActor MUST use the single saved CanonicalModel.InputEpoch,
        // InputSequence and InputNonce, not a local per-peer send counter.
        // The original  observer supplies the trusted SteamDelivery.
        internal static Bytes Route(HostSignedTradeSession existingSession, AuthenticatedHostActionInbox hostInbox, Bytes officialSteamWire, SteamDelivery originalAuthenticatedDelivery, Func<byte, PersistedHumanActionCursor> readCanonicalActor, out ulong replySteamId, out AdmissionReason status)
        {
            return Route(existingSession, hostInbox, officialSteamWire, originalAuthenticatedDelivery, readCanonicalActor, null, null, out replySteamId, out status);
        }

        // KIND9 is the ONLY unsigned peer publication receipt in this
        // proposed extension. It must arrive with genuine official Steam
        // transport metadata and current original live Steam->slot map.
        internal static Bytes Route(HostSignedTradeSession existingSession, AuthenticatedHostActionInbox hostInbox, Bytes officialSteamWire, SteamDelivery originalAuthenticatedDelivery, Func<byte, PersistedHumanActionCursor> readCanonicalActor, OriginalPeerPublicationQuorum existingOriginalQuorum, Func<byte, ulong, bool> originalSteamSlotProof, out ulong replySteamId, out AdmissionReason status)
        {
            replySteamId = 0;
            status = AdmissionReason.malformed;
            if (existingSession == null || officialSteamWire == null || originalAuthenticatedDelivery == null)
                throw new ArgumentNullException("No original Steam ingress");
            Envelope envelope = AdmissionCodec.Decode(officialSteamWire);
            if (envelope.Kind == PacketKind.PeerPublicationReceipt)
            {
                Require.That(existingSession.IsHost && existingSession.Ready && originalSteamSlotProof != null, AdmissionReason.bootstrap_incomplete);
                var reported = PeerPublicationAcknowledgement.DecodeBody(envelope.Body.Copy());
                Require.That(originalAuthenticatedDelivery.Generation == existingSession.Context.Generation && originalAuthenticatedDelivery.SenderSteamId != existingSession.Context.HostSteamId && existingSession.Context.Contains(originalAuthenticatedDelivery.SenderSteamId) && reported.Binding.SessionId.Same(existingSession.Descriptor.Id) && originalSteamSlotProof(reported.ActorSlot, originalAuthenticatedDelivery.SenderSteamId), AdmissionReason.sender_not_participant);
                if (existingOriginalQuorum == null)
                {
                    status = AdmissionReason.duplicate;
                    return null;
                }

                existingOriginalQuorum.ReceiveOriginalAuthenticatedPeerReceipt(reported, originalAuthenticatedDelivery, originalSteamSlotProof);
                status = AdmissionReason.ok;
                return null;
            }

            if (envelope.Kind == PacketKind.HumanIntent)
            {
                Require.That(existingSession.IsHost && existingSession.Ready && hostInbox != null && readCanonicalActor != null, AdmissionReason.bootstrap_incomplete);
                // Verify the actual Steam metadata before any accessor for
                // actor-specific canonical state. Unsigned HumanIntent bytes
                // must never select another human's sequence/nonce snapshot.
                Require.That(originalAuthenticatedDelivery.Generation == existingSession.Context.Generation, AdmissionReason.stale_transport_context);
                Require.That(existingSession.Context.Contains(originalAuthenticatedDelivery.SenderSteamId) && originalAuthenticatedDelivery.SenderSteamId != existingSession.Context.HostSteamId, AdmissionReason.sender_not_participant);
                var proposed = HumanTradeActionEnvelope.DecodeClientIntent(officialSteamWire, existingSession.Descriptor.Id);
                Require.That(hostInbox.HumanSteamIdForSlot(proposed.ActorSlot) == originalAuthenticatedDelivery.SenderSteamId, AdmissionReason.sender_not_participant);
                var cursor = readCanonicalActor(proposed.ActorSlot);
                Require.That(cursor != null && cursor.Epoch > 0, AdmissionReason.bootstrap_incomplete);
                status = existingSession.StageAuthenticatedHumanIntent(hostInbox, officialSteamWire, originalAuthenticatedDelivery, cursor.Epoch, cursor.LastSequence, cursor.LastNonce);
                return null;
            }

            // Signed commands and activations have exactly ONE route:
            // the strict original Chore 106 under post-native verification.
            if (envelope.Kind == PacketKind.HumanAction || envelope.Kind == PacketKind.Activation || envelope.Kind == PacketKind.Command || envelope.Kind == PacketKind.WorldFrame || envelope.Kind == PacketKind.PublicationBarrier)
                throw new AdmissionException(AdmissionReason.wrong_kind);
            // Existing Challenge/Hello/Ack bootstrap remains unmodified.
            Bytes reply = existingSession.ReceiveSteam(officialSteamWire, originalAuthenticatedDelivery, out replySteamId);
            status = AdmissionReason.ok;
            return reply;
        }
    }
}
