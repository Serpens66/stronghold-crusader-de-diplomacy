// successor: Kind9/Kind10 publication receipts on the existing
//  1..8 stream. SOURCE-ONLY CONTRACT; owner approval for 9/10 pending.
// Kind9 (188 bytes) = UNSIGNED original-Steam metadata-bound Ready/Applied
// echo; NEVER accepted on Chore. Kind10 (188 bytes) = HOST RSA signed
// globally ordered PublicationBarrier on SAME original ChoreSession.
// NO additional packet subscription, signer, save, world digest or cache.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal enum PublicationReceiptPhase : byte
    {
        CandidateReady = 1,
        OwnerReadbackApplied = 2
    }

    internal sealed class PublicationFrameBinding
    {
        internal const int FixedBytes = 188;
        internal const int MaximumReadbackShaBytes = 32;
        internal readonly Bytes SessionId;
        internal readonly ulong FrameChoreSequence;
        internal readonly long FrameOriginalTick;
        internal readonly int OriginalDay;
        internal readonly ulong InputEpoch;
        internal readonly ulong LastIncludedVerifiedChore;
        internal readonly long BaseCanonicalRevision;
        private readonly byte[] _worldSha, _candidateSha, _ownerReadbackSha;
        internal PublicationFrameBinding(Bytes pinnedSessionId, ulong frameSignedSequence, long frameOriginalTick, int originalNativeDay, ulong originalInputEpoch, ulong lastIncludedVerifiedChore, long baseCanonicalRevision, byte[] existingWorldAiConsentOnlySha256, byte[] canonicalCandidateStateSha256, byte[] originalOwnerSerializedReadbackSha256)
        {
            if (pinnedSessionId == null || pinnedSessionId.Length != 32 || frameSignedSequence < 1 || frameSignedSequence >= ulong.MaxValue - 2 || frameOriginalTick < 1 || originalNativeDay < 1 || originalInputEpoch < 1 || originalInputEpoch >= ulong.MaxValue - 1 || lastIncludedVerifiedChore >= frameSignedSequence || // The only allowed included frontier is the one that
 // immediately precedes the host-signed WorldFrame.
            lastIncludedVerifiedChore + 1 != frameSignedSequence || baseCanonicalRevision < 1 || !ValidSha(existingWorldAiConsentOnlySha256) || !ValidSha(canonicalCandidateStateSha256) || !ValidSha(originalOwnerSerializedReadbackSha256))
                throw new InvalidDataException("Invalid exact original source/candidate/owner-readback binding");
            SessionId = new Bytes(pinnedSessionId.Copy());
            FrameChoreSequence = frameSignedSequence;
            FrameOriginalTick = frameOriginalTick;
            OriginalDay = originalNativeDay;
            InputEpoch = originalInputEpoch;
            LastIncludedVerifiedChore = lastIncludedVerifiedChore;
            BaseCanonicalRevision = baseCanonicalRevision;
            _worldSha = (byte[])existingWorldAiConsentOnlySha256.Clone();
            _candidateSha = (byte[])canonicalCandidateStateSha256.Clone();
            _ownerReadbackSha = (byte[])originalOwnerSerializedReadbackSha256.Clone();
        }

        private static bool ValidSha(byte[] b)
        {
            return b != null && b.Length == 32;
        }

        internal byte[] WorldSha()
        {
            return (byte[])_worldSha.Clone();
        }

        internal byte[] CandidateSha()
        {
            return (byte[])_candidateSha.Clone();
        }

        internal byte[] OwnerReadbackSha()
        {
            return (byte[])_ownerReadbackSha.Clone();
        }

        internal bool Same(PublicationFrameBinding other)
        {
            return other != null && SessionId.Same(other.SessionId) && FrameChoreSequence == other.FrameChoreSequence && FrameOriginalTick == other.FrameOriginalTick && OriginalDay == other.OriginalDay && InputEpoch == other.InputEpoch && LastIncludedVerifiedChore == other.LastIncludedVerifiedChore && BaseCanonicalRevision == other.BaseCanonicalRevision && Same32(_worldSha, other._worldSha) && Same32(_candidateSha, other._candidateSha) && Same32(_ownerReadbackSha, other._ownerReadbackSha);
        }

        internal static bool Same32(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != 32 || b.Length != 32)
                return false;
            int differ = 0;
            for (int i = 0; i < 32; i++)
                differ |= a[i] ^ b[i];
            return differ == 0;
        }
    }

    // Exactly 188-byte Kind9 body, not signed, Steam ingress only.
    // Body encodes a claimed actor slot; actual auth comes from the EXISTING
    // official rawra event + current ORIGINAL GameNetworkAPI Steam mapping.
    // This object carries candidate and serialized Schema19 owner readback
    // HASHES, not fake "FrameVerified=true" booleans.
    internal sealed class PeerPublicationAcknowledgement
    {
        internal const int BodyLength = 188;
        private const uint Magic = 0x35323156; // ASCII  LE
        private const byte Version = 2;
        internal readonly PublicationReceiptPhase Phase;
        internal readonly byte ActorSlot;
        internal readonly ulong PublishBarrierChoreSequence;
        internal readonly PublicationFrameBinding Binding;
        internal PeerPublicationAcknowledgement(PublicationReceiptPhase phase, byte originalActorSlot, ulong barrierChoreSequence, PublicationFrameBinding binding)
        {
            if (binding == null || originalActorSlot < 1 || originalActorSlot > 8 || (phase != PublicationReceiptPhase.CandidateReady && phase != PublicationReceiptPhase.OwnerReadbackApplied) || (phase == PublicationReceiptPhase.CandidateReady && barrierChoreSequence != 0) || (phase == PublicationReceiptPhase.OwnerReadbackApplied && barrierChoreSequence <= binding.FrameChoreSequence))
                throw new InvalidDataException("Invalid local candidate/owner-readback receipt phase");
            Phase = phase;
            ActorSlot = originalActorSlot;
            PublishBarrierChoreSequence = barrierChoreSequence;
            Binding = binding;
        }

        internal byte[] EncodeBody()
        {
            using (var m = new MemoryStream(BodyLength))
            using (var w = new BinaryWriter(m))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write((byte)Phase);
                w.Write(ActorSlot);
                w.Write((byte)0);
                w.Write(Binding.SessionId.Copy());
                w.Write(Binding.FrameChoreSequence);
                w.Write(PublishBarrierChoreSequence);
                w.Write(Binding.FrameOriginalTick);
                w.Write(Binding.OriginalDay);
                w.Write(Binding.InputEpoch);
                w.Write(Binding.LastIncludedVerifiedChore);
                w.Write(Binding.WorldSha());
                w.Write(Binding.CandidateSha());
                w.Write(Binding.OwnerReadbackSha());
                w.Write(Binding.BaseCanonicalRevision);
                w.Flush();
                if (m.Length != BodyLength)
                    throw new InvalidDataException("Wrong 188-byte owner receipt layout");
                return m.ToArray();
            }
        }

        internal static PeerPublicationAcknowledgement DecodeBody(byte[] wire)
        {
            if (wire == null || wire.Length != BodyLength)
                throw new InvalidDataException("Peer receipt must have exact length");
            byte[] frozen = (byte[])wire.Clone();
            using (var r = new BinaryReader(new MemoryStream(frozen, false)))
            {
                if (r.ReadUInt32() != Magic || r.ReadByte() != Version)
                    throw new InvalidDataException("Unknown world-publication receipt version");
                var phase = (PublicationReceiptPhase)r.ReadByte();
                byte actor = r.ReadByte();
                if (r.ReadByte() != 0)
                    throw new InvalidDataException("Nonzero future flags");
                var session = new Bytes(r.ReadBytes(32));
                ulong frameSeq = r.ReadUInt64();
                ulong barrierSeq = r.ReadUInt64();
                long tick = r.ReadInt64();
                int day = r.ReadInt32();
                ulong epoch = r.ReadUInt64();
                ulong highwater = r.ReadUInt64();
                byte[] world = r.ReadBytes(32);
                byte[] candidate = r.ReadBytes(32);
                byte[] readback = r.ReadBytes(32);
                long target = r.ReadInt64();
                if (r.BaseStream.Position != BodyLength)
                    throw new InvalidDataException("Unexpected peer receipt tail");
                return new PeerPublicationAcknowledgement(phase, actor, barrierSeq, new PublicationFrameBinding(session, frameSeq, tick, day, epoch, highwater, target, world, candidate, readback));
            }
        }

        internal Bytes UnsignedSteamPacket()
        {
            return AdmissionCodec.Encode(PacketKind.PeerPublicationReceipt, new Bytes(EncodeBody()), null);
        }
    }

    // Exactly 188-byte kind10 body, original host RSA signed and executed
    // by the same global Chore owner AFTER all *authenticated* phase1
    // peer candidate/readback readiness receipts are checked.
    internal sealed class SignedPublicationBarrier
    {
        internal const int BodyLength = 188;
        private const uint Magic = 0x35323156;
        private const byte Version = 2;
        internal readonly ulong GlobalChoreSequence;
        internal readonly PublicationFrameBinding Binding;
        internal SignedPublicationBarrier(ulong originalGlobalChoreSequence, PublicationFrameBinding binding)
        {
            if (binding == null || originalGlobalChoreSequence != binding.FrameChoreSequence + 1 || originalGlobalChoreSequence >= ulong.MaxValue - 1)
                throw new InvalidDataException("Unordered signed publication barrier");
            GlobalChoreSequence = originalGlobalChoreSequence;
            Binding = binding;
        }

        internal Bytes EncodeBody()
        {
            return AdmissionCodec.Build(w =>
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(new byte[3]); // explicit zero flags
                w.Write(Binding.SessionId.Copy());
                w.Write(GlobalChoreSequence);
                w.Write(Binding.FrameChoreSequence);
                w.Write(Binding.FrameOriginalTick);
                w.Write(Binding.OriginalDay);
                w.Write(Binding.InputEpoch);
                w.Write(Binding.LastIncludedVerifiedChore);
                w.Write(Binding.WorldSha());
                w.Write(Binding.CandidateSha());
                w.Write(Binding.OwnerReadbackSha());
                w.Write(Binding.BaseCanonicalRevision);
            });
        }

        internal static SignedPublicationBarrier DecodeBody(Bytes body)
        {
            if (body == null || body.Length != BodyLength)
                throw new AdmissionException(AdmissionReason.malformed);
            try
            {
                using (var r = AdmissionCodec.Reader(body))
                {
                    if (r.ReadUInt32() != Magic || r.ReadByte() != Version || r.ReadByte() != 0 || r.ReadByte() != 0 || r.ReadByte() != 0)
                        throw new AdmissionException(AdmissionReason.wrong_version);
                    var session = AdmissionCodec.Read(r, 32);
                    ulong chore = r.ReadUInt64(), frame = r.ReadUInt64();
                    long tick = r.ReadInt64();
                    int day = r.ReadInt32();
                    ulong epoch = r.ReadUInt64(), highwater = r.ReadUInt64();
                    byte[] world = r.ReadBytes(32), candidate = r.ReadBytes(32), readback = r.ReadBytes(32);
                    long target = r.ReadInt64();
                    AdmissionCodec.End(r);
                    return new SignedPublicationBarrier(chore, new PublicationFrameBinding(session, frame, tick, day, epoch, highwater, target, world, candidate, readback));
                }
            }
            catch (EndOfStreamException)
            {
                throw new AdmissionException(AdmissionReason.malformed);
            }
            catch (InvalidDataException)
            {
                throw new AdmissionException(AdmissionReason.invalid_command);
            }
        }

        internal Bytes SignedEnvelopeReadyForOneExistingSigner()
        {
            // IMPORTANT: This is only the canonical BODY. The existing
            // HostBootstrap.SignPublicationBarrier signs it via RSA.
            return EncodeBody();
        }
    }
}
