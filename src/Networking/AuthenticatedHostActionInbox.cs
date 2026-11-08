// Host-only staging: authenticated Steam metadata fixes the actor's
// identity. Client requests never touch canonical state or Chore history.
// Up to eight independent humans may have one uncommitted action each;
// host signing order is the sole global order observed by every peer.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrongholdEuropeDE.Runtime.Networking
{
    internal sealed class AuthenticatedHostActionInbox
    {
        private sealed class Staged
        {
            internal HumanTradeActionEnvelope Request;
            internal Bytes OriginalWire;
            internal ulong Sender;
            internal ulong SignedSequence;
        }

        private readonly TrustedContext context;
        private readonly SessionDescriptor session;
        private readonly ulong[] steamBySlot;
        private readonly Queue<Staged> order = new Queue<Staged>();
        private readonly Dictionary<byte, Staged> actors = new Dictionary<byte, Staged>();
        private bool invalid;
        internal AuthenticatedHostActionInbox(TrustedContext provenEngineContext, SessionDescriptor pinnedHostSession, ulong[] exactNativeSlotToSteamId)
        {
            context = provenEngineContext ?? throw new ArgumentNullException("provenEngineContext");
            session = pinnedHostSession ?? throw new ArgumentNullException("pinnedHostSession");
            if (context.LocalSteamId != context.HostSteamId || session.HostSteamId != context.HostSteamId || !session.Scope.Same(context.Scope) || exactNativeSlotToSteamId == null || exactNativeSlotToSteamId.Length != 9)
                throw new ArgumentException("Host or scope lacks engine provenance");
            steamBySlot = (ulong[])exactNativeSlotToSteamId.Clone();
            // Native player slots are 1..8; index zero is reserved. If a
            // participant appears at index zero, the roster may otherwise
            // pass the set-equality check yet have no legal actor slot.
            Require.That(steamBySlot[0] == 0 && steamBySlot.Skip(1).All(x => x == 0 || x > 1000), AdmissionReason.wrong_scope);
            ulong[] ids = steamBySlot.Where(x => x > 1000).OrderBy(x => x).ToArray();
            Require.That(ids.Length == context.HumanIds().Length && ids.SequenceEqual(context.HumanIds()) && ids.Distinct().Count() == ids.Length, AdmissionReason.wrong_scope);
        }

        internal int Count
        {
            get
            {
                return order.Count;
            }
        }

        internal void Invalidate()
        {
            invalid = true;
            order.Clear();
            actors.Clear();
        }

        // Host may receive this from a genuine authenticated Steam sender,
        // and may NOT fabricate that sender from the payload.
        internal AdmissionReason StageRemote(Bytes wire, SteamDelivery officialDelivery, ulong canonicalInputEpoch, ulong lastAcceptedActorSequence, ulong lastAcceptedActorNonce)
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
            Require.That(officialDelivery != null && officialDelivery.Generation == context.Generation, AdmissionReason.stale_transport_context);
            Require.That(context.Contains(officialDelivery.SenderSteamId) && officialDelivery.SenderSteamId != context.HostSteamId, AdmissionReason.sender_not_participant);
            return Stage(wire, officialDelivery.SenderSteamId, canonicalInputEpoch, lastAcceptedActorSequence, lastAcceptedActorNonce);
        }

        // Caller must attest localSlot from its ORIGINAL engine-owned local
        // player register and verify it equals the actual Steam host slot.
        // This is a separate API from StageRemote; no synthetic Steam event.
        internal AdmissionReason StageHostLocal(Bytes localUiWire, byte verifiedOriginalLocalPlayerSlot, ulong canonicalInputEpoch, ulong lastAcceptedActorSequence, ulong lastAcceptedActorNonce)
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
            Require.That(verifiedOriginalLocalPlayerSlot >= 1 && verifiedOriginalLocalPlayerSlot <= 8 && steamBySlot[verifiedOriginalLocalPlayerSlot] == context.HostSteamId, AdmissionReason.sender_not_host);
            var action = HumanTradeActionEnvelope.DecodeClientIntent(localUiWire, session.Id);
            Require.That(action.ActorSlot == verifiedOriginalLocalPlayerSlot, AdmissionReason.sender_not_host);
            return Stage(localUiWire, context.HostSteamId, canonicalInputEpoch, lastAcceptedActorSequence, lastAcceptedActorNonce);
        }

        private AdmissionReason Stage(Bytes wire, ulong actualSenderSteamId, ulong canonicalInputEpoch, ulong lastAcceptedActorSequence, ulong lastAcceptedActorNonce)
        {
            HumanTradeActionEnvelope request = HumanTradeActionEnvelope.DecodeClientIntent(wire, session.Id);
            Require.That(request.ActorSlot >= 1 && request.ActorSlot <= 8 && steamBySlot[request.ActorSlot] == actualSenderSteamId && context.Contains(actualSenderSteamId), AdmissionReason.sender_not_participant);
            Require.That(request.Epoch == canonicalInputEpoch && canonicalInputEpoch > 0, AdmissionReason.wrong_scope);
            Staged existing;
            if (actors.TryGetValue(request.ActorSlot, out existing))
            {
                if (existing.OriginalWire.Same(wire) && existing.Sender == actualSenderSteamId)
                    return AdmissionReason.duplicate;
                // Same actor may enqueue its NEXT command only AFTER the
                // previous global chore was consumed and saved.
                return AdmissionReason.invalid_command;
            }

            Require.That(order.Count < 8 && lastAcceptedActorSequence < ulong.MaxValue - 1 && request.ActorSequence == lastAcceptedActorSequence + 1 && request.ActorNonce > lastAcceptedActorNonce, AdmissionReason.sequence_gap);
            var stage = new Staged
            {
                Request = request,
                OriginalWire = new Bytes(wire.Copy()),
                Sender = actualSenderSteamId
            };
            actors.Add(request.ActorSlot, stage);
            order.Enqueue(stage);
            return AdmissionReason.ok;
        }

        // Returns the oldest pending action, bound to *officially witnessed*
        // sender Steam ID and the host's globally ordered Chore sequence.
        // Signing and enqueueing are done by the coordinator's EXISTING
        // HostSignedTradeSession; this object owns neither RSA nor native code.
        internal HumanTradeActionEnvelope OldestToSign(ulong nextHostSequence)
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
            if (order.Count == 0)
                return null;
            Staged next = order.Peek();
            if (next.SignedSequence == 0)
                next.SignedSequence = nextHostSequence;
            else
                Require.That(nextHostSequence == next.SignedSequence, AdmissionReason.sequence_gap);
            return next.Request.WithHostAttestation(next.SignedSequence, next.Sender);
        }

        // Called ONLY after the original ordered Chore has invoked the
        // canonical reducer, successfully serialized its single SaveData
        // ownership and advanced the actor input sequence/nonce. A quote
        // can be rejected for game reasons while still consuming input.
        internal void ConfirmCanonicalConsumption(HumanTradeActionEnvelope signed, ulong savedActorSequence, ulong savedActorNonce)
        {
            Require.That(!invalid && order.Count > 0 && signed != null && signed.IsHostSigned, AdmissionReason.session_invalidated);
            Staged next = order.Peek();
            Require.That(next.SignedSequence == signed.HostSequence && next.Sender == signed.ActorSteamId && next.Request.ActorSlot == signed.ActorSlot && next.Request.ActorSequence == signed.ActorSequence && next.Request.ActorNonce == signed.ActorNonce && next.Request.EncodeBody().Same(new HumanTradeActionEnvelope(signed.SessionId, 0, 0, signed.ActorSlot, signed.RecipientSlot, signed.Action, signed.Tier, signed.OfferedGood, signed.WantedGood, signed.RequestedAmount, signed.HumanOfferedAmount, signed.CounterId, signed.CounterRootId, signed.CounterOfferedAmount, signed.CounterCreatedDay, signed.CounterExpiresDay, signed.ReferenceOfferedGood, signed.ReferenceRequestedGood, signed.ReferenceRequestedAmount, signed.Epoch, signed.ActorSequence, signed.ActorNonce).EncodeBody()) && savedActorSequence == signed.ActorSequence && savedActorNonce == signed.ActorNonce, AdmissionReason.session_invalidated);
            order.Dequeue();
            actors.Remove(next.Request.ActorSlot);
        }

        internal ulong HumanSteamIdForSlot(byte actor)
        {
            // After reconnect/map change this inbox must not continue routing
            // to identities obtained from a previous engine-owned roster.
            if (invalid || actor < 1 || actor > 8)
                return 0;
            return steamBySlot[actor];
        }
    }
}
