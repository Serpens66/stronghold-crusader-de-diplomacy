using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace StrongholdEuropeDE.Runtime.Networking
{
    // Windows CAPI RSA-AES provider supports SHA256 on both CLR4 and Unity Mono.
    // Keys are ephemeral: no private parameters are written to disk or a save.
    internal static class Crypto
    {
        internal static RSACryptoServiceProvider NewEphemeral()
        {
            var p = new CspParameters(24)
            {
                Flags = CspProviderFlags.NoPrompt | CspProviderFlags.CreateEphemeralKey
            };
            return new RSACryptoServiceProvider(2048, p)
            {
                PersistKeyInCsp = false
            };
        }
    }

    internal enum AdmissionReason
    {
        ok,
        malformed,
        oversized,
        wrong_version,
        wrong_kind,
        wrong_scope,
        stale_transport_context,
        sender_not_participant,
        sender_not_host,
        challenge_missing,
        challenge_mismatch,
        key_changed,
        bad_signature,
        session_mismatch,
        session_invalidated,
        bootstrap_incomplete,
        duplicate,
        sequence_gap,
        sequence_exhausted,
        invalid_command
    }

    internal sealed class AdmissionException : Exception
    {
        internal readonly AdmissionReason Reason;
        internal AdmissionException(AdmissionReason reason) : base(reason.ToString())
        {
            Reason = reason;
        }
    }

    internal static class Require
    {
        internal static void That(bool condition, AdmissionReason reason)
        {
            if (!condition)
                throw new AdmissionException(reason);
        }
    }

    // Immutable bytes; no backing array escapes.
    internal sealed class Bytes
    {
        private readonly byte[] value;
        internal Bytes(byte[] source)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            value = (byte[])source.Clone();
        }

        internal int Length
        {
            get
            {
                return value.Length;
            }
        }

        internal byte[] Copy()
        {
            return (byte[])value.Clone();
        }

        internal bool Same(Bytes other)
        {
            if (other == null || value.Length != other.value.Length)
                return false;
            int difference = 0;
            for (int i = 0; i < value.Length; i++)
                difference |= value[i] ^ other.value[i];
            return difference == 0;
        }

        internal static Bytes Random32()
        {
            var b = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(b);
            return new Bytes(b);
        }

        internal static Bytes Hash(Bytes source)
        {
            using (var sha = SHA256.Create())
                return new Bytes(sha.ComputeHash(source.Copy()));
        }
    }

    /*
     * Construct ONLY in the engine/Steam adapter.
     * generation is a locally maintained lifecycle counter, never a packet field.
     * It changes on map entry/exit, load, roster change, host change, or reconnect.
     *
     * Participant IDs and hashes come from a complete engine-owned snapshot,
     * not from a received hello.
     */
    internal sealed class TrustedContext
    {
        internal readonly long Generation;
        internal readonly ulong HostSteamId;
        internal readonly ulong LocalSteamId;
        internal readonly Bytes Scope; // exactly 4 concatenated SHA256 hashes
        private readonly ulong[] humans;
        internal TrustedContext(long generation, ulong hostSteamId, ulong localSteamId, byte[] scope128, ulong[] humanSteamIds)
        {
            if (scope128 == null || scope128.Length != 128 || humanSteamIds == null || humanSteamIds.Length < 1 || humanSteamIds.Length > 8 || generation < 1 || hostSteamId <= 1000 || localSteamId <= 1000)
                throw new ArgumentException("Invalid trusted context");
            humans = (ulong[])humanSteamIds.Clone();
            Array.Sort(humans);
            for (int i = 0; i < humans.Length; i++)
            {
                if (humans[i] <= 1000 || (i != 0 && humans[i] == humans[i - 1]))
                    throw new ArgumentException("Invalid human roster");
            }

            Generation = generation;
            HostSteamId = hostSteamId;
            LocalSteamId = localSteamId;
            Scope = new Bytes(scope128);
            if (!Contains(hostSteamId) || !Contains(localSteamId))
                throw new ArgumentException("Host/local absent from roster");
        }

        internal bool Contains(ulong steamId)
        {
            return Array.BinarySearch(humans, steamId) >= 0;
        }

        internal ulong[] HumanIds()
        {
            return (ulong[])humans.Clone();
        }
    }

    /*
     * Transport metadata is supplied separately from payload.
     * Never construct this by decoding a packet.
     * Check generation again when draining a deferred callback queue.
     */
    internal sealed class SteamDelivery
    {
        internal readonly long Generation;
        internal readonly ulong SenderSteamId;
        internal SteamDelivery(long generation, ulong senderSteamId)
        {
            Generation = generation;
            SenderSteamId = senderSteamId;
        }
    }

    internal enum PacketKind : byte
    {
        Challenge = 1,
        Hello = 2,
        Ack = 3,
        Command = 4,
        HumanAction = 5, // RSA-signed host order, same sequence lane
        HumanIntent = 6, // unsigned Steam sender-authenticated input ONLY
        Activation = 7, // signed canonical InputEpoch activation
        WorldFrame = 8, // exact selected WorldAI currentSave 101B consent-only digest
        PeerPublicationReceipt = 9, // unsigned official-Steam-only (188B)
        PublicationBarrier = 10 // signed original Chore only (188B)
    }

    internal sealed class Envelope
    {
        internal readonly PacketKind Kind;
        internal readonly Bytes Body;
        internal readonly Bytes SignedPrefix;
        internal readonly Bytes Signature;
        internal Envelope(PacketKind kind, Bytes body, Bytes prefix, Bytes signature)
        {
            Kind = kind;
            Body = body;
            SignedPrefix = prefix;
            Signature = signature;
        }
    }

    internal static class AdmissionCodec
    {
        internal const int MaximumBytes = 1198;
        private const uint Magic = 0x37314553; // LE bytes: SE17
        private const byte Version = 1;
        internal static Bytes Build(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
                writer.Flush();
                return new Bytes(stream.ToArray());
            }
        }

        internal static BinaryReader Reader(Bytes bytes)
        {
            return new BinaryReader(new MemoryStream(bytes.Copy(), false));
        }

        internal static Bytes Read(BinaryReader reader, int count)
        {
            byte[] b = reader.ReadBytes(count);
            Require.That(b.Length == count, AdmissionReason.malformed);
            return new Bytes(b);
        }

        internal static void End(BinaryReader reader)
        {
            Require.That(reader.BaseStream.Position == reader.BaseStream.Length, AdmissionReason.malformed);
        }

        private static int BodySize(PacketKind kind)
        {
            switch (kind)
            {
                case PacketKind.Challenge:
                    return 32;
                case PacketKind.Hello:
                    return 459;
                case PacketKind.Ack:
                    return 64;
                case PacketKind.Command:
                    return 80;
                case PacketKind.HumanAction:
                    return HumanTradeActionEnvelope.BodyLength;
                case PacketKind.HumanIntent:
                    return HumanTradeActionEnvelope.BodyLength;
                case PacketKind.Activation:
                    return AuthenticatedHumanActionActivation.BodyLength;
                case PacketKind.WorldFrame:
                    return OrderedWorldDigestBodyCodec.BodyBytes;
                case PacketKind.PeerPublicationReceipt:
                    return PeerPublicationAcknowledgement.BodyLength;
                case PacketKind.PublicationBarrier:
                    return SignedPublicationBarrier.BodyLength;
                default:
                    throw new AdmissionException(AdmissionReason.wrong_kind);
            }
        }

        private static bool Signed(PacketKind kind)
        {
            return kind == PacketKind.Hello || kind == PacketKind.Command || kind == PacketKind.HumanAction || kind == PacketKind.Activation || kind == PacketKind.WorldFrame || kind == PacketKind.PublicationBarrier;
        }

        internal static Bytes Encode(PacketKind kind, Bytes body, RSA signer)
        {
            Require.That(body != null && body.Length == BodySize(kind), AdmissionReason.malformed);
            Bytes prefix = Build(w =>
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write((byte)kind);
                w.Write((ushort)body.Length);
                w.Write(body.Copy());
            });
            if (!Signed(kind))
            {
                Require.That(signer == null, AdmissionReason.malformed);
                return prefix;
            }

            Require.That(signer != null && signer.KeySize == 2048, AdmissionReason.malformed);
            byte[] sig = signer.SignData(prefix.Copy(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Require.That(sig.Length == 256, AdmissionReason.malformed);
            return Build(w =>
            {
                w.Write(prefix.Copy());
                w.Write(sig);
            });
        }

        internal static Envelope Decode(Bytes raw)
        {
            Require.That(raw != null, AdmissionReason.malformed);
            Require.That(raw.Length <= MaximumBytes, AdmissionReason.oversized);
            Require.That(raw.Length >= 8, AdmissionReason.malformed);
            try
            {
                using (var r = Reader(raw))
                {
                    Require.That(r.ReadUInt32() == Magic, AdmissionReason.wrong_version);
                    Require.That(r.ReadByte() == Version, AdmissionReason.wrong_version);
                    var kind = (PacketKind)r.ReadByte();
                    int bodySize = BodySize(kind);
                    Require.That(r.ReadUInt16() == bodySize, AdmissionReason.malformed);
                    int sigSize = Signed(kind) ? 256 : 0;
                    Require.That(raw.Length == 8 + bodySize + sigSize, AdmissionReason.malformed);
                    Bytes body = Read(r, bodySize);
                    Bytes signature = Read(r, sigSize);
                    End(r);
                    byte[] prefix = new byte[8 + bodySize];
                    Buffer.BlockCopy(raw.Copy(), 0, prefix, 0, prefix.Length);
                    return new Envelope(kind, body, new Bytes(prefix), signature);
                }
            }
            catch (EndOfStreamException)
            {
                throw new AdmissionException(AdmissionReason.malformed);
            }
        }

        internal static void Verify(Envelope envelope, Bytes modulus)
        {
            Require.That(modulus != null && modulus.Length == 256, AdmissionReason.malformed);
            byte[] m = modulus.Copy();
            Require.That((m[0] & 0x80) != 0 && (m[255] & 1) != 0, AdmissionReason.malformed);
            try
            {
                using (var rsa = Crypto.NewEphemeral())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = m, Exponent = new byte[] { 1, 0, 1 } });
                    Require.That(rsa.VerifyData(envelope.SignedPrefix.Copy(), envelope.Signature.Copy(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), AdmissionReason.bad_signature);
                }
            }
            catch (CryptographicException)
            {
                throw new AdmissionException(AdmissionReason.bad_signature);
            }
        }
    }

    internal sealed class SessionDescriptor
    {
        internal readonly Bytes Scope, Nonce, Modulus, Encoded, Id;
        internal readonly ulong HostSteamId;
        internal SessionDescriptor(Bytes scope, ulong hostSteamId, Bytes nonce, Bytes modulus)
        {
            Require.That(scope != null && scope.Length == 128 && nonce != null && nonce.Length == 32 && modulus != null && modulus.Length == 256 && hostSteamId > 1000, AdmissionReason.malformed);
            Scope = scope;
            Nonce = nonce;
            Modulus = modulus;
            HostSteamId = hostSteamId;
            Encoded = AdmissionCodec.Build(w =>
            {
                // Separate domain from every envelope and command.
                w.Write(new byte[] { 83, 69, 49, 55, 83, 69, 83, 83 });
                w.Write(scope.Copy());
                w.Write(hostSteamId);
                w.Write(nonce.Copy());
                w.Write(modulus.Copy());
                w.Write(new byte[] { 1, 0, 1 });
            });
            Id = Bytes.Hash(Encoded);
        }

        internal static SessionDescriptor Read(BinaryReader r)
        {
            Bytes scope = AdmissionCodec.Read(r, 128);
            ulong host = r.ReadUInt64();
            Bytes nonce = AdmissionCodec.Read(r, 32);
            Bytes modulus = AdmissionCodec.Read(r, 256);
            Bytes exponent = AdmissionCodec.Read(r, 3);
            Require.That(exponent.Same(new Bytes(new byte[] { 1, 0, 1 })), AdmissionReason.malformed);
            return new SessionDescriptor(scope, host, nonce, modulus);
        }

        internal void Write(BinaryWriter w)
        {
            w.Write(Scope.Copy());
            w.Write(HostSteamId);
            w.Write(Nonce.Copy());
            w.Write(Modulus.Copy());
            w.Write(new byte[] { 1, 0, 1 });
        }
    }

    internal sealed class PeerBootstrap
    {
        private readonly TrustedContext context;
        private readonly Bytes challenge = Bytes.Random32();
        private SessionDescriptor pinned;
        private bool invalid;
        internal PeerBootstrap(TrustedContext context)
        {
            this.context = context ?? throw new ArgumentNullException("context");
        }

        internal Bytes ChallengePacket()
        {
            CheckLive();
            return AdmissionCodec.Encode(PacketKind.Challenge, challenge, null);
        }

        private void CheckLive()
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
        }

        internal void Invalidate()
        {
            invalid = true;
            pinned = null;
        }

        internal Bytes ReceiveHello(Bytes wire, SteamDelivery delivery)
        {
            CheckLive();
            Require.That(delivery != null && delivery.Generation == context.Generation, AdmissionReason.stale_transport_context);
            Require.That(delivery.SenderSteamId == context.HostSteamId, AdmissionReason.sender_not_host);
            Envelope e = AdmissionCodec.Decode(wire);
            Require.That(e.Kind == PacketKind.Hello, AdmissionReason.wrong_kind);
            SessionDescriptor candidate;
            using (var r = AdmissionCodec.Reader(e.Body))
            {
                candidate = SessionDescriptor.Read(r);
                Bytes echoed = AdmissionCodec.Read(r, 32);
                AdmissionCodec.End(r);
                Require.That(echoed.Same(challenge), AdmissionReason.challenge_mismatch);
            }

            Require.That(candidate.HostSteamId == context.HostSteamId && candidate.Scope.Same(context.Scope), AdmissionReason.wrong_scope);
            AdmissionCodec.Verify(e, candidate.Modulus);
            if (pinned != null)
                Require.That(pinned.Id.Same(candidate.Id), AdmissionReason.key_changed);
            else
                pinned = candidate;
            return AdmissionCodec.Encode(PacketKind.Ack, AdmissionCodec.Build(w =>
            {
                w.Write(pinned.Id.Copy());
                w.Write(challenge.Copy());
            }), null);
        }

        // Preflight result only. This does not activate simulation execution.
        internal SessionDescriptor PinnedSession()
        {
            CheckLive();
            Require.That(pinned != null, AdmissionReason.bootstrap_incomplete);
            return pinned;
        }
    }

    internal sealed class HostBootstrap : IDisposable
    {
        private readonly TrustedContext context;
        private readonly RSACryptoServiceProvider key;
        private readonly Dictionary<ulong, Bytes> challenges = new Dictionary<ulong, Bytes>();
        private readonly HashSet<ulong> acknowledged = new HashSet<ulong>();
        private bool invalid;
        // Host may retransmit the latest identical signed command but cannot
        // bind a second canonical action to one sequence number or skip one.
        private ulong lastSignedSequence;
        private Bytes lastSignedCanonical, lastSignedPacket;
        private PacketKind lastSignedKind;
        internal readonly SessionDescriptor Session;
        internal HostBootstrap(TrustedContext context)
        {
            this.context = context ?? throw new ArgumentNullException("context");
            Require.That(context.LocalSteamId == context.HostSteamId, AdmissionReason.sender_not_host);
            key = Crypto.NewEphemeral();
            RSAParameters pub = key.ExportParameters(false);
            Require.That(new Bytes(pub.Exponent).Same(new Bytes(new byte[] { 1, 0, 1 })), AdmissionReason.malformed);
            Session = new SessionDescriptor(context.Scope, context.HostSteamId, Bytes.Random32(), new Bytes(pub.Modulus));
            // Host's own descriptor and engine snapshot were checked locally.
            acknowledged.Add(context.HostSteamId);
        }

        private void CheckDelivery(SteamDelivery delivery)
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
            Require.That(delivery != null && delivery.Generation == context.Generation, AdmissionReason.stale_transport_context);
            Require.That(context.Contains(delivery.SenderSteamId), AdmissionReason.sender_not_participant);
        }

        internal Bytes ReceiveChallenge(Bytes wire, SteamDelivery delivery)
        {
            CheckDelivery(delivery);
            Envelope e = AdmissionCodec.Decode(wire);
            Require.That(e.Kind == PacketKind.Challenge, AdmissionReason.wrong_kind);
            // New challenge withdraws an earlier acknowledgement for this peer.
            // Duplicate challenge is idempotent.
            Bytes previous;
            if (!challenges.TryGetValue(delivery.SenderSteamId, out previous) || !previous.Same(e.Body))
            {
                challenges[delivery.SenderSteamId] = e.Body;
                if (delivery.SenderSteamId != context.HostSteamId)
                    acknowledged.Remove(delivery.SenderSteamId);
            }

            return AdmissionCodec.Encode(PacketKind.Hello, AdmissionCodec.Build(w =>
            {
                Session.Write(w);
                w.Write(e.Body.Copy());
            }), key);
        }

        internal AdmissionReason ReceiveAck(Bytes wire, SteamDelivery delivery)
        {
            CheckDelivery(delivery);
            Envelope e = AdmissionCodec.Decode(wire);
            Require.That(e.Kind == PacketKind.Ack, AdmissionReason.wrong_kind);
            using (var r = AdmissionCodec.Reader(e.Body))
            {
                Bytes id = AdmissionCodec.Read(r, 32);
                Bytes echo = AdmissionCodec.Read(r, 32);
                AdmissionCodec.End(r);
                Require.That(id.Same(Session.Id), AdmissionReason.session_mismatch);
                Bytes expected;
                Require.That(challenges.TryGetValue(delivery.SenderSteamId, out expected), AdmissionReason.challenge_missing);
                Require.That(expected.Same(echo), AdmissionReason.challenge_mismatch);
            }

            return acknowledged.Add(delivery.SenderSteamId) ? AdmissionReason.ok : AdmissionReason.duplicate;
        }

        internal bool PreflightComplete
        {
            get
            {
                if (invalid)
                    return false;
                foreach (ulong id in context.HumanIds())
                    if (!acknowledged.Contains(id))
                        return false;
                return true;
            }
        }

        // ONE ephemeral RSA key and ONE globally monotonic host sequence
        // sign old ExecuteTrade and new human-to-AI/human-to-human actions.
        // A new request cannot re-sign a different body under the same
        // sequence even if its packet kind differs.
        private Bytes SignCanonical(PacketKind kind, Bytes body, Bytes sessionId, ulong sequence)
        {
            Require.That(!invalid, AdmissionReason.session_invalidated);
            Require.That(PreflightComplete, AdmissionReason.bootstrap_incomplete);
            Require.That(sessionId != null && sessionId.Same(Session.Id), AdmissionReason.session_mismatch);
            Require.That(body != null, AdmissionReason.invalid_command);
            if (sequence == lastSignedSequence && lastSignedSequence > 0)
            {
                Require.That(kind == lastSignedKind && lastSignedCanonical != null && lastSignedCanonical.Same(body), AdmissionReason.invalid_command);
                return new Bytes(lastSignedPacket.Copy());
            }

            Require.That(lastSignedSequence < ulong.MaxValue - 1, AdmissionReason.sequence_exhausted);
            Require.That(sequence == lastSignedSequence + 1, AdmissionReason.sequence_gap);
            Bytes packet = AdmissionCodec.Encode(kind, body, key);
            lastSignedSequence = sequence;
            lastSignedKind = kind;
            lastSignedCanonical = body;
            lastSignedPacket = packet;
            return new Bytes(packet.Copy());
        }

        internal Bytes SignCommand(TradeCommand command)
        {
            Require.That(command != null, AdmissionReason.invalid_command);
            return SignCanonical(PacketKind.Command, command.Encode(), command.SessionId, command.Sequence);
        }

        internal Bytes SignHumanAction(HumanTradeActionEnvelope action)
        {
            Require.That(action != null && action.IsHostSigned && context.Contains(action.ActorSteamId), AdmissionReason.sender_not_participant);
            return SignCanonical(PacketKind.HumanAction, action.EncodeBody(), action.SessionId, action.HostSequence);
        }

        internal Bytes SignActivation(AuthenticatedHumanActionActivation activation)
        {
            Require.That(activation != null, AdmissionReason.invalid_command);
            return SignCanonical(PacketKind.Activation, activation.EncodeBody(), activation.SessionId, activation.HostSequence);
        }

        // Kind8 is a fourth variant on the SAME HostBootstrap RSA key and
        // SAME globally monotonically signed host sequence. NO new signer.
        internal Bytes SignWorldFrame(OrderedHostWorldWitness witnessed)
        {
            Require.That(witnessed != null && witnessed.OriginalHostSteamId == Session.HostSteamId, AdmissionReason.sender_not_host);
            return SignCanonical(PacketKind.WorldFrame, new Bytes(OrderedWorldDigestBodyCodec.Encode(witnessed)), witnessed.OriginalSessionId, witnessed.SignedHostChoreSequence);
        }

        internal Bytes SignPublicationBarrier(SignedPublicationBarrier barrier)
        {
            Require.That(barrier != null, AdmissionReason.invalid_command);
            return SignCanonical(PacketKind.PublicationBarrier, barrier.EncodeBody(), barrier.Binding.SessionId, barrier.GlobalChoreSequence);
        }

        public void Dispose()
        {
            if (invalid)
                return;
            invalid = true;
            challenges.Clear();
            acknowledged.Clear();
            key.Dispose();
        }
    }

    /*
     * Only an ExecuteTrade intent is admitted here.
     * This is not a durable commit receipt or a native-write capability.
     */
    internal sealed class TradeCommand
    {
        internal readonly Bytes SessionId;
        internal readonly ulong Sequence, QuoteId, RootId;
        internal readonly byte Requester, Recipient;
        internal readonly ushort OfferedGood, RequestedGood;
        internal readonly int OfferedAmount, RequestedAmount;
        internal readonly int IssuedDay, ExpiresDay;
        internal TradeCommand(Bytes sessionId, ulong sequence, ulong quoteId, ulong rootId, byte requester, byte recipient, ushort offeredGood, ushort requestedGood, int offeredAmount, int requestedAmount, int issuedDay, int expiresDay)
        {
            SessionId = sessionId;
            Sequence = sequence;
            QuoteId = quoteId;
            RootId = rootId;
            Requester = requester;
            Recipient = recipient;
            OfferedGood = offeredGood;
            RequestedGood = requestedGood;
            OfferedAmount = offeredAmount;
            RequestedAmount = requestedAmount;
            IssuedDay = issuedDay;
            ExpiresDay = expiresDay;
            Validate();
        }

        private static bool Good(ushort id)
        {
            switch (id)
            {
                case 2:
                case 4:
                case 6:
                case 7:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                case 16:
                case 17:
                case 18:
                case 19:
                case 20:
                case 21:
                case 22:
                case 23:
                case 24:
                    return true;
                default:
                    return false;
            }
        }

        private void Validate()
        {
            Require.That(SessionId != null && SessionId.Length == 32 && Sequence >= 1 && Sequence < ulong.MaxValue && QuoteId >= 1001 && QuoteId < ulong.MaxValue - 8 && (RootId == 0 || (RootId >= 1001 && RootId < QuoteId)) && Requester >= 1 && Requester <= 8 && Recipient >= 1 && Recipient <= 8 && Requester != Recipient && Good(OfferedGood) && Good(RequestedGood) && OfferedGood != RequestedGood && OfferedAmount >= 1 && OfferedAmount <= 100000 && RequestedAmount >= 1 && RequestedAmount <= 100000 && IssuedDay >= 1 && ExpiresDay >= IssuedDay && (long)ExpiresDay - IssuedDay <= 15, AdmissionReason.invalid_command);
        }

        internal Bytes Encode()
        {
            return AdmissionCodec.Build(w =>
            {
                w.Write(SessionId.Copy());
                w.Write(Sequence);
                w.Write((byte)1); // ExecuteTrade schema/opcode
                w.Write(QuoteId);
                w.Write(RootId);
                w.Write(Requester);
                w.Write(Recipient);
                w.Write(OfferedGood);
                w.Write(RequestedGood);
                w.Write(OfferedAmount);
                w.Write(RequestedAmount);
                w.Write(IssuedDay);
                w.Write(ExpiresDay);
                w.Write((byte)0); // reserved, must be zero
            });
        }

        internal static TradeCommand Decode(Bytes body)
        {
            using (var r = AdmissionCodec.Reader(body))
            {
                Bytes session = AdmissionCodec.Read(r, 32);
                ulong sequence = r.ReadUInt64();
                Require.That(r.ReadByte() == 1, AdmissionReason.invalid_command);
                var c = new TradeCommand(session, sequence, r.ReadUInt64(), r.ReadUInt64(), r.ReadByte(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                Require.That(r.ReadByte() == 0, AdmissionReason.invalid_command);
                AdmissionCodec.End(r);
                return c;
            }
        }
    }

    /*
     * Instantiate only at the integration's verified synchronized boundary.
     * Own and call it exclusively from the ordered Chore execution path.
     * There is deliberately no Steam ReceiveCommand entry point.
     */
    internal sealed class ChoreSession
    {
        private readonly SessionDescriptor session;
        private ulong lastConsumed;
        private ulong lastVerifiedInputSequence; // read-only causal observation, NOT replay owner
        // Bounded authenticated replay witnesses. Earlier signed commands
        // may be duplicates ONLY when their exact canonical body is known.
        // Outside this window, fail closed rather than trusting an unknown
        // old sequence; never grow memory without limit during long matches.
        private const int ReplayWitnessLimit = 2048;
        private readonly Dictionary<ulong, Bytes> replayWitnesses = new Dictionary<ulong, Bytes>();
        private readonly Queue<ulong> replayOrder = new Queue<ulong>();
        // 0=ready, 1=exclusive Consume owner, 2=irreversibly invalidated.
        // An atomically acquired owner closes the concurrent-reducer window.
        private int executionState;
        internal ChoreSession(SessionDescriptor frozenSession)
        {
            session = frozenSession ?? throw new ArgumentNullException("frozenSession");
        }

        internal void Invalidate()
        {
            Interlocked.Exchange(ref executionState, 2);
        }

        // OBSERVATION ONLY from the same original managed Chore callback
        // under EngineInterface.threadLock. WorldAI frame history captures
        // this at its actual native tick; not a second replay owner.
        internal ulong VerifiedSignedHighwater
        {
            get
            {
                return lastConsumed;
            }
        }

        internal ulong VerifiedInputFrontier
        {
            get
            {
                return lastVerifiedInputSequence;
            }
        }

        // Existing API is still exclusively old ExecuteTrade. The MIXED API
        // deliberately shares *the same* session, replay witnesses, sequence
        // cursor, signature verification, and exclusive execution state.
        internal AdmissionReason Consume(Bytes wire, Action<TradeCommand> deterministicReducer)
        {
            if (deterministicReducer == null)
                throw new ArgumentNullException("deterministicReducer");
            return ConsumeMixed(wire, deterministicReducer, null, null);
        }

        internal AdmissionReason ConsumeMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanActionReducer)
        {
            return ConsumeMixed(wire, tradeReducer, humanActionReducer, null);
        }

        internal AdmissionReason ConsumeMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanActionReducer, Action<AuthenticatedHumanActionActivation> activationReducer)
        {
            return ConsumeMixed(wire, tradeReducer, humanActionReducer, activationReducer, null);
        }

        internal AdmissionReason ConsumeMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanActionReducer, Action<AuthenticatedHumanActionActivation> activationReducer, Action<OrderedHostWorldWitness> worldFrameReducer)
        {
            return ConsumeMixed(wire, tradeReducer, humanActionReducer, activationReducer, worldFrameReducer, null);
        }

        internal AdmissionReason ConsumeMixed(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanActionReducer, Action<AuthenticatedHumanActionActivation> activationReducer, Action<OrderedHostWorldWitness> worldFrameReducer, Action<SignedPublicationBarrier> barrierReducer)
        {
            if (tradeReducer == null && humanActionReducer == null && activationReducer == null && worldFrameReducer == null && barrierReducer == null)
                throw new ArgumentNullException("No canonical reducer");
            Require.That(Interlocked.CompareExchange(ref executionState, 1, 0) == 0, AdmissionReason.session_invalidated);
            try
            {
                return ConsumeMixedExclusively(wire, tradeReducer, humanActionReducer, activationReducer, worldFrameReducer, barrierReducer);
            }
            finally
            {
                Interlocked.CompareExchange(ref executionState, 0, 1);
            }
        }

        private AdmissionReason ConsumeMixedExclusively(Bytes wire, Action<TradeCommand> tradeReducer, Action<HumanTradeActionEnvelope> humanActionReducer, Action<AuthenticatedHumanActionActivation> activationReducer, Action<OrderedHostWorldWitness> worldFrameReducer, Action<SignedPublicationBarrier> barrierReducer)
        {
            Envelope e = AdmissionCodec.Decode(wire);
            TradeCommand trade = null;
            HumanTradeActionEnvelope human = null;
            AuthenticatedHumanActionActivation activation = null;
            OrderedHostWorldWitness originalWorld = null;
            SignedPublicationBarrier barrier = null;
            ulong sequence;
            if (e.Kind == PacketKind.Command)
            {
                Require.That(tradeReducer != null, AdmissionReason.wrong_kind);
                trade = TradeCommand.Decode(e.Body);
                Require.That(trade.SessionId.Same(session.Id), AdmissionReason.session_mismatch);
                sequence = trade.Sequence;
            }
            else if (e.Kind == PacketKind.HumanAction)
            {
                Require.That(humanActionReducer != null, AdmissionReason.wrong_kind);
                human = HumanTradeActionEnvelope.Decode(e.Body);
                Require.That(human.IsHostSigned && human.SessionId.Same(session.Id), AdmissionReason.session_mismatch);
                sequence = human.HostSequence;
            }
            else if (e.Kind == PacketKind.Activation)
            {
                Require.That(activationReducer != null, AdmissionReason.wrong_kind);
                activation = AuthenticatedHumanActionActivation.Decode(e.Body);
                Require.That(activation.SessionId.Same(session.Id), AdmissionReason.session_mismatch);
                sequence = activation.HostSequence;
            }
            else if (e.Kind == PacketKind.WorldFrame)
            {
                Require.That(worldFrameReducer != null, AdmissionReason.wrong_kind);
                Require.That(OrderedWorldDigestBodyCodec.TryDecode(e.Body.Copy(), out originalWorld), AdmissionReason.invalid_command);
                Require.That(originalWorld != null && originalWorld.OriginalSessionId.Same(session.Id) && originalWorld.OriginalHostSteamId == session.HostSteamId, AdmissionReason.wrong_scope);
                sequence = originalWorld.SignedHostChoreSequence;
            }
            else if (e.Kind == PacketKind.PublicationBarrier)
            {
                Require.That(barrierReducer != null, AdmissionReason.wrong_kind);
                barrier = SignedPublicationBarrier.DecodeBody(e.Body);
                Require.That(barrier.GlobalChoreSequence == barrier.Binding.FrameChoreSequence + 1 && barrier.Binding.SessionId.Same(session.Id), AdmissionReason.session_mismatch);
                sequence = barrier.GlobalChoreSequence;
            }
            else
            {
                // Especially HumanIntent and PeerPublicationReceipt: unsigned, accepted only via real
                // host Steam identity, NEVER as an ordered game Chore.
                throw new AdmissionException(AdmissionReason.wrong_kind);
            }

            AdmissionCodec.Verify(e, session.Modulus);
            Require.That(Interlocked.CompareExchange(ref executionState, 1, 1) == 1, AdmissionReason.session_invalidated);
            if (sequence <= lastConsumed)
            {
                Bytes previous;
                if (!replayWitnesses.TryGetValue(sequence, out previous) || !previous.Same(e.SignedPrefix))
                {
                    Invalidate();
                    return AdmissionReason.session_invalidated;
                }

                return AdmissionReason.duplicate;
            }

            if (lastConsumed == ulong.MaxValue - 1)
                return AdmissionReason.sequence_exhausted;
            if (sequence != lastConsumed + 1)
            {
                Invalidate();
                return AdmissionReason.sequence_gap;
            }

            if (barrier != null)
                Require.That(barrier.Binding.LastIncludedVerifiedChore == lastVerifiedInputSequence, AdmissionReason.wrong_scope);
            // Signed global input is consumed even if its validly bound
            // business action is rejected. Otherwise replay after changes
            // to stock or alliances could turn an old rejection into success.
            lastConsumed = sequence;
            if (trade != null || human != null || activation != null)
                lastVerifiedInputSequence = sequence;
            replayWitnesses.Add(sequence, e.SignedPrefix);
            replayOrder.Enqueue(sequence);
            if (replayOrder.Count > ReplayWitnessLimit)
                replayWitnesses.Remove(replayOrder.Dequeue());
            try
            {
                Require.That(Interlocked.CompareExchange(ref executionState, 1, 1) == 1, AdmissionReason.session_invalidated);
                if (trade != null)
                    tradeReducer(trade);
                else if (human != null)
                    humanActionReducer(human);
                else if (activation != null)
                    activationReducer(activation);
                else if (originalWorld != null)
                    worldFrameReducer(originalWorld);
                else
                    barrierReducer(barrier);
                Require.That(Interlocked.CompareExchange(ref executionState, 1, 1) == 1, AdmissionReason.session_invalidated);
                return AdmissionReason.ok;
            }
            catch
            {
                Invalidate();
                throw;
            }
        }
    }
}
