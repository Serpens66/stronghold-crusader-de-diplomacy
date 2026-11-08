// Admission is complete independently of the native goods release gate.
// The adapter serializes every method with the ORIGINAL engine monitor.
using System;
using System.Collections.Generic;
using System.IO;

namespace StrongholdEuropeDE.Runtime.Networking
{
    internal enum QueueResult
    {
        Refused,
        AwaitingPeers,
        PendingOtherCommand,
        QueuedNotCommitted,
        RetainedForRetry
    }

    internal sealed class HostSignedTradeSession : IDisposable
    {
        internal readonly TrustedContext Context;
        private readonly HostBootstrap _host;
        private readonly PeerBootstrap _peer;
        private readonly Dictionary<ulong, Bytes> _challenges = new Dictionary<ulong, Bytes>();
        private ChoreSession _ordered;
        private Bytes _pending;
        private ulong _issued, _pendingSequence;
        private bool _invalid;
        // After a PublicationBarrier is RSA signed, the ORIGINAL host signer
        // remains STOPPED until every original Steam peer has acknowledged
        // actual OwnerReadbackApplied at that signed barrier. The old _pending
        // slot is still the only strict queue/retry owner.
        private bool _publicationAwaitingAllPeerApplied;
        private ulong _publicationFrameSequence;
        internal HostSignedTradeSession(TrustedContext context)
        {
            Context = context ?? throw new ArgumentNullException("context");
            if (context.LocalSteamId == context.HostSteamId)
                _host = new HostBootstrap(context);
            else
                _peer = new PeerBootstrap(context);
        }

        internal bool IsHost
        {
            get
            {
                return _host != null;
            }
        }

        internal bool Live
        {
            get
            {
                return !_invalid;
            }
        }

        internal bool IsBusyWithOrderedChore
        {
            get
            {
                return _pending != null;
            }
        }

        internal bool WaitingForOrderedPublicationAcks
        {
            get
            {
                return _publicationAwaitingAllPeerApplied;
            }
        }

        // Does not verify packet authenticity by itself. Source is the SAME
        // original verified ChoreSession highwater, never featureTicks.
        internal ulong LastVerifiedChoreSequence
        {
            get
            {
                return _ordered == null ? 0 : _ordered.VerifiedSignedHighwater;
            }
        }

        internal ulong LastVerifiedInputSequence
        {
            get
            {
                return _ordered == null ? 0 : _ordered.VerifiedInputFrontier;
            }
        }

        internal bool HasPublicationFrame
        {
            get
            {
                return _publicationFrameSequence != 0;
            }
        }

        internal ulong NextHostSequence
        {
            get
            {
                if (_issued == ulong.MaxValue)
                    return 0;
                return _issued + 1;
            }
        }

        internal bool Ready
        {
            get
            {
                if (_invalid)
                    return false;
                if (_host != null)
                    return _host.PreflightComplete;
                try
                {
                    return _peer.PinnedSession() != null;
                }
                catch (AdmissionException)
                {
                    return false;
                }
            }
        }

        internal Bytes Challenge
        {
            get
            {
                return _invalid || _peer == null ? null : _peer.ChallengePacket();
            }
        }

        internal SessionDescriptor Descriptor
        {
            get
            {
                return _host != null ? _host.Session : _peer.PinnedSession();
            }
        }

        internal Bytes ReceiveSteam(Bytes wire, SteamDelivery delivery, out ulong replyTo)
        {
            replyTo = 0;
            Require.That(!_invalid, AdmissionReason.session_invalidated);
            Require.That(delivery != null && delivery.Generation == Context.Generation, AdmissionReason.stale_transport_context);
            Require.That(Context.Contains(delivery.SenderSteamId) && delivery.SenderSteamId != Context.LocalSteamId, AdmissionReason.sender_not_participant);
            Envelope envelope = AdmissionCodec.Decode(wire);
            // No Steam path is allowed to instantiate or advance ChoreSession.
            Require.That(envelope.Kind != PacketKind.Command && envelope.Kind != PacketKind.HumanAction && envelope.Kind != PacketKind.HumanIntent && envelope.Kind != PacketKind.Activation && envelope.Kind != PacketKind.WorldFrame && envelope.Kind != PacketKind.PeerPublicationReceipt && envelope.Kind != PacketKind.PublicationBarrier, AdmissionReason.wrong_kind);
            if (_host != null)
            {
                if (envelope.Kind == PacketKind.Challenge)
                {
                    Bytes old;
                    if (_issued > 0 && _challenges.TryGetValue(delivery.SenderSteamId, out old) && !old.Same(envelope.Body))
                    {
                        // A rejoining peer has lost its ordered replay state. Refuse further
                        // commands until a real lifecycle change establishes a fresh context.
                        Dispose();
                        throw new AdmissionException(AdmissionReason.session_invalidated);
                    }

                    _challenges[delivery.SenderSteamId] = envelope.Body;
                    Bytes hello = _host.ReceiveChallenge(wire, delivery);
                    replyTo = delivery.SenderSteamId;
                    return hello;
                }

                Require.That(envelope.Kind == PacketKind.Ack, AdmissionReason.wrong_kind);
                _host.ReceiveAck(wire, delivery);
                return null;
            }

            Require.That(envelope.Kind == PacketKind.Hello, AdmissionReason.wrong_kind);
            Bytes ack;
            try
            {
                ack = _peer.ReceiveHello(wire, delivery);
            }
            catch (AdmissionException error)
            {
                // ReceiveHello reports key_changed only AFTER real host sender,
                // challenge, scope and candidate RSA verification succeeded.
                // Retire this SAME ordered session; never keep old key authority live.
                if (error.Reason == AdmissionReason.key_changed)
                    Dispose();
                throw;
            }

            replyTo = Context.HostSteamId;
            return ack;
        }

        // All quote terms must already have passed the host's normal saved-quote
        // admission. Queue acceptance is explicitly NOT a business commit.
        internal QueueResult Queue(ulong quote, ulong root, byte a, byte b, ushort give, ushort want, int giveAmount, int wantAmount, int issuedDay, int expiryDay, Func<Bytes, bool> strictQueue)
        {
            if (_publicationFrameSequence != 0 || _publicationAwaitingAllPeerApplied || _invalid || _host == null || strictQueue == null)
                return QueueResult.Refused;
            if (!_host.PreflightComplete)
                return QueueResult.AwaitingPeers;
            if (_pending != null)
                return QueueResult.PendingOtherCommand;
            if (_issued >= ulong.MaxValue - 2)
                return QueueResult.Refused;
            var command = new TradeCommand(_host.Session.Id, _issued + 1, quote, root, a, b, give, want, giveAmount, wantAmount, issuedDay, expiryDay);
            _pending = _host.SignCommand(command);
            _pendingSequence = command.Sequence;
            _issued = command.Sequence;
            return RetryPending(strictQueue);
        }

        // Authenticated original Steam ingress is a SEPARATE host-only
        // admission path. The human-owned packet has no signature and MUST
        // never enter ConsumeOrderedMixed or a native simulation callback.
        internal AdmissionReason StageAuthenticatedHumanIntent(AuthenticatedHostActionInbox intake, Bytes intent, SteamDelivery source, ulong canonicalInputEpoch, ulong persistedActorSequence, ulong persistedActorNonce)
        {
            Require.That(!_invalid && _host != null && _host.PreflightComplete && intake != null, AdmissionReason.bootstrap_incomplete);
            return intake.StageRemote(intent, source, canonicalInputEpoch, persistedActorSequence, persistedActorNonce);
        }

        // The host alone may sign the staged action, using THE SAME ephemeral
        // session key and NEXT sequence as an existing ExecuteTrade chore.
        // Host activation is a signed globally ordered Chore and must be
        // consumed by all peers before any human input updates InputEpoch.
        internal QueueResult QueueActivation(ulong canonicalInputEpoch, Func<Bytes, bool> strictQueue)
        {
            if (_publicationFrameSequence != 0 || _publicationAwaitingAllPeerApplied || _invalid || _host == null || strictQueue == null)
                return QueueResult.Refused;
            if (!_host.PreflightComplete)
                return QueueResult.AwaitingPeers;
            if (_pending != null)
                return QueueResult.PendingOtherCommand;
            if (NextHostSequence == 0 || canonicalInputEpoch == 0)
                return QueueResult.Refused;
            var activation = new AuthenticatedHumanActionActivation(_host.Session.Id, NextHostSequence, canonicalInputEpoch);
            _pending = _host.SignActivation(activation);
            _pendingSequence = activation.HostSequence;
            _issued = activation.HostSequence;
            return RetryPending(strictQueue);
        }

        internal QueueResult QueueHumanAction(HumanTradeActionEnvelope action, Func<Bytes, bool> strictQueue)
        {
            if (_publicationFrameSequence != 0 || _publicationAwaitingAllPeerApplied || _invalid || _host == null || strictQueue == null)
                return QueueResult.Refused;
            if (!_host.PreflightComplete)
                return QueueResult.AwaitingPeers;
            if (_pending != null)
                return QueueResult.PendingOtherCommand;
            if (action == null || !action.IsHostSigned || action.HostSequence != NextHostSequence || action.ActorSteamId <= 1000 || !Context.Contains(action.ActorSteamId))
                return QueueResult.Refused;
            _pending = _host.SignHumanAction(action);
            _pendingSequence = action.HostSequence;
            _issued = action.HostSequence;
            return RetryPending(strictQueue);
        }

        // WorldFrame uses the ORIGINAL host RSA signature and same exact
        // _pending/_issued slot as legacy TradeCommand and selected  consent.
        internal QueueResult QueueWorldFrame(OrderedHostWorldWitness frame, Func<Bytes, bool> strictQueue)
        {
            if (_publicationFrameSequence != 0 || _publicationAwaitingAllPeerApplied || _invalid || _host == null || strictQueue == null || frame == null)
                return QueueResult.Refused;
            if (!_host.PreflightComplete)
                return QueueResult.AwaitingPeers;
            if (_pending != null)
                return QueueResult.PendingOtherCommand;
            if (NextHostSequence == 0 || LastVerifiedChoreSequence != _issued || LastVerifiedInputSequence + 1 != NextHostSequence || frame.SignedHostChoreSequence != NextHostSequence || !frame.OriginalSessionId.Same(_host.Session.Id) || frame.OriginalHostSteamId != Context.HostSteamId)
                return QueueResult.Refused;
            _pending = _host.SignWorldFrame(frame);
            _publicationFrameSequence = frame.SignedHostChoreSequence;
            _pendingSequence = frame.SignedHostChoreSequence;
            _issued = frame.SignedHostChoreSequence;
            return RetryPending(strictQueue);
        }

        internal QueueResult QueuePublicationBarrier(SignedPublicationBarrier barrier, OriginalPeerPublicationQuorum originalQuorum, Func<Bytes, bool> strictQueue)
        {
            if (_publicationFrameSequence == 0 || _publicationAwaitingAllPeerApplied || _invalid || _host == null || strictQueue == null || barrier == null || originalQuorum == null)
                return QueueResult.Refused;
            if (!_host.PreflightComplete)
                return QueueResult.AwaitingPeers;
            if (_pending != null)
                return QueueResult.PendingOtherCommand;
            if (barrier.Binding.FrameChoreSequence != _publicationFrameSequence || barrier.GlobalChoreSequence != _publicationFrameSequence + 1 || barrier.GlobalChoreSequence != NextHostSequence || !barrier.Binding.SessionId.Same(_host.Session.Id) || !originalQuorum.AllActualOriginalHumansReady || !originalQuorum.Binding.Same(barrier.Binding))
                return QueueResult.Refused;
            Bytes signed = _host.SignPublicationBarrier(barrier);
            originalQuorum.BindExistingHostSignedPublicationBarrier(barrier);
            _pending = signed;
            _pendingSequence = barrier.GlobalChoreSequence;
            _issued = barrier.GlobalChoreSequence;
            // This guard survives queued-but-not-Chore and Chore-but-not-
            // canonical/publication ACK. No subsequent  signed inputs!
            _publicationAwaitingAllPeerApplied = true;
            return RetryPending(strictQueue);
        }

        // The coordinator calls ONLY after the existing managed original
        // readback/publishing owner attests exact serializer SHA and all
        // authenticated Phase2 PeerApplied receipts. Does not prove durable .sav.
        internal void ConfirmExactPublishedOwnerQuorum(OriginalPeerPublicationQuorum published, SignedPublicationBarrier exactSigned, byte[] actualOwnerSerializedReadbackSha256)
        {
            Require.That(!_invalid && _host != null && _publicationAwaitingAllPeerApplied && _pending == null && published != null && exactSigned != null && published.ExistingBarrier == exactSigned && LastVerifiedChoreSequence == exactSigned.GlobalChoreSequence, AdmissionReason.session_invalidated);
            published.RequireAllAppliedForNextSignedHostAction(_host.Session.Id, exactSigned, actualOwnerSerializedReadbackSha256);
            _publicationAwaitingAllPeerApplied = false;
            _publicationFrameSequence = 0;
        }

        internal QueueResult RetryPending(Func<Bytes, bool> strictQueue)
        {
            if (_invalid || _host == null || !_host.PreflightComplete || _pending == null || strictQueue == null)
                return QueueResult.Refused;
            // A queue failure retains the SAME signed bytes and sequence. Never make
            // a gap, issue another quote, or fall back to an asynchronous Steam send.
            try
            {
                return strictQueue(new Bytes(_pending.Copy())) ? QueueResult.QueuedNotCommitted : QueueResult.RetainedForRetry;
            }
            catch (Exception)
            {
                return QueueResult.RetainedForRetry;
            }
        }

        internal AdmissionReason ConsumeOrdered(Bytes wire, Action<TradeCommand> reducer)
        {
            return ConsumeOrderedMixed(wire, reducer, null, null);
        }

        internal AdmissionReason ConsumeOrderedMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanReducer)
        {
            return ConsumeOrderedMixed(wire, tradeReducer, humanReducer, null);
        }

        internal AdmissionReason ConsumeOrderedMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanReducer, Action<AuthenticatedHumanActionActivation> activationReducer)
        {
            return ConsumeOrderedMixed(wire, tradeReducer, humanReducer, activationReducer, null);
        }

        internal AdmissionReason ConsumeOrderedMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanReducer, Action<AuthenticatedHumanActionActivation> activationReducer, Action<OrderedHostWorldWitness> worldFrameReducer)
        {
            return ConsumeOrderedMixed(wire, tradeReducer, humanReducer, activationReducer, worldFrameReducer, null);
        }

        internal AdmissionReason ConsumeOrderedMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanReducer, Action<AuthenticatedHumanActionActivation> activationReducer, Action<OrderedHostWorldWitness> worldFrameReducer, Action<SignedPublicationBarrier> publicationReducer)
        {
            Require.That(!_invalid && Ready, AdmissionReason.bootstrap_incomplete);
            if (_ordered == null)
                _ordered = new ChoreSession(Descriptor);
            ulong consumed = 0;
            try
            {
                AdmissionReason result = _ordered.ConsumeMixed(wire, tradeReducer == null ? null : (Action<TradeCommand>)(c =>
                {
                    consumed = c.Sequence;
                    tradeReducer(c);
                }), humanReducer == null ? null : (Action<HumanTradeActionEnvelope>)(a =>
                {
                    consumed = a.HostSequence;
                    humanReducer(a);
                }), activationReducer == null ? null : (Action<AuthenticatedHumanActionActivation>)(a =>
                {
                    consumed = a.HostSequence;
                    activationReducer(a);
                }), worldFrameReducer == null ? null : (Action<OrderedHostWorldWitness>)(w =>
                {
                    consumed = w.SignedHostChoreSequence;
                    worldFrameReducer(w);
                }), publicationReducer == null ? null : (Action<SignedPublicationBarrier>)(b =>
                {
                    consumed = b.GlobalChoreSequence;
                    publicationReducer(b);
                }));
                // Clearing pending confirms ORDERED CHORE self-delivery, never business
                // success. Actual canonical bytes must still be persisted separately.
                if (consumed != 0 && consumed == _pendingSequence)
                {
                    _pending = null;
                    _pendingSequence = 0;
                }

                if (result == AdmissionReason.sequence_gap || result == AdmissionReason.session_invalidated || result == AdmissionReason.sequence_exhausted)
                    Dispose();
                return result;
            }
            catch (AdmissionException e)
            {
                if (e.Reason == AdmissionReason.session_invalidated || e.Reason == AdmissionReason.sequence_gap)
                    Dispose();
                throw;
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_invalid)
                return;
            _invalid = true;
            if (_ordered != null)
                _ordered.Invalidate();
            if (_host != null)
                _host.Dispose();
            if (_peer != null)
                _peer.Invalidate();
            _pending = null;
            _publicationAwaitingAllPeerApplied = true;
            _challenges.Clear();
        }
    }
}
