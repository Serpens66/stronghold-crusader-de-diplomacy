// unified actor-authenticated human command on the EXISTING
// ephemeral host RSA key and EXISTING globally ordered Chore 106 stream.
// No BepInEx, Harmony, Native, Steamworks or SaveData references.
// The unsigned HumanIntent packet is accepted only with real transport
// Steam sender metadata at the host. Never send it by Chore.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime.Networking
{
    internal enum HumanTradeActionKind : byte
    {
        ProposeAi = 0,
        AcceptAiCounter = 1,
        DeclineAiCounter = 2,
        ProposeHuman = 4,
        CounterHuman = 5,
        AcceptHuman = 6,
        DeclineHuman = 7,
        WithdrawHuman = 8
    }

    // Exactly 128 bytes, no variable length fields, native Chore <=1198.
    // The reserved bytes are always zero and checked on decode.
    internal sealed class HumanTradeActionEnvelope
    {
        internal const int BodyLength = 128;
        internal readonly Bytes SessionId;
        internal readonly ulong HostSequence, ActorSteamId;
        internal readonly byte ActorSlot, RecipientSlot, Tier;
        internal readonly HumanTradeActionKind Action;
        internal readonly ushort OfferedGood, WantedGood;
        internal readonly int RequestedAmount, HumanOfferedAmount;
        internal readonly ulong CounterId, CounterRootId;
        internal readonly int CounterOfferedAmount, CounterCreatedDay, CounterExpiresDay;
        internal readonly ushort ReferenceOfferedGood, ReferenceRequestedGood;
        internal readonly int ReferenceRequestedAmount;
        internal readonly ulong Epoch, ActorSequence, ActorNonce;
        internal HumanTradeActionEnvelope(Bytes sessionId, ulong hostSequence, ulong actorSteamId, byte actorSlot, byte recipientSlot, HumanTradeActionKind action, byte tier, ushort offeredGood, ushort wantedGood, int requestedAmount, int humanOfferedAmount, ulong counterId, ulong counterRootId, int counterOfferedAmount, int counterCreatedDay, int counterExpiresDay, ushort referenceOfferedGood, ushort referenceRequestedGood, int referenceRequestedAmount, ulong epoch, ulong actorSequence, ulong actorNonce)
        {
            SessionId = sessionId;
            HostSequence = hostSequence;
            ActorSteamId = actorSteamId;
            ActorSlot = actorSlot;
            RecipientSlot = recipientSlot;
            Action = action;
            Tier = tier;
            OfferedGood = offeredGood;
            WantedGood = wantedGood;
            RequestedAmount = requestedAmount;
            HumanOfferedAmount = humanOfferedAmount;
            CounterId = counterId;
            CounterRootId = counterRootId;
            CounterOfferedAmount = counterOfferedAmount;
            CounterCreatedDay = counterCreatedDay;
            CounterExpiresDay = counterExpiresDay;
            ReferenceOfferedGood = referenceOfferedGood;
            ReferenceRequestedGood = referenceRequestedGood;
            ReferenceRequestedAmount = referenceRequestedAmount;
            Epoch = epoch;
            ActorSequence = actorSequence;
            ActorNonce = actorNonce;
            Validate();
        }

        private static bool Good(ushort good, bool allowAuto)
        {
            if (allowAuto && good == 0)
                return true;
            switch (good)
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
            Require.That(SessionId != null && SessionId.Length == 32 && ((HostSequence == 0 && ActorSteamId == 0) || (HostSequence > 0 && HostSequence < ulong.MaxValue && ActorSteamId > 1000)) && ActorSlot >= 1 && ActorSlot <= 8 && RecipientSlot >= 1 && RecipientSlot <= 8 && ActorSlot != RecipientSlot && (byte)Action != 3 && (byte)Action <= 8 && Tier <= 2 && Epoch > 0 && Epoch < ulong.MaxValue && ActorSequence > 0 && ActorSequence < ulong.MaxValue && ActorNonce > 0 && ActorNonce < ulong.MaxValue && RequestedAmount >= 1 && RequestedAmount <= 100000 && Good(OfferedGood, Action == HumanTradeActionKind.ProposeAi) && Good(WantedGood, Action == HumanTradeActionKind.ProposeAi) && (OfferedGood != WantedGood || (Action == HumanTradeActionKind.ProposeAi && OfferedGood == 0 && WantedGood == 0)), AdmissionReason.invalid_command);
            bool isPropose = Action == HumanTradeActionKind.ProposeAi || Action == HumanTradeActionKind.ProposeHuman;
            bool isHuman = Action >= HumanTradeActionKind.ProposeHuman;
            Require.That((isHuman ? HumanOfferedAmount >= 1 && HumanOfferedAmount <= 100000 : HumanOfferedAmount == 0) && (isPropose ? CounterId == 0 && CounterRootId == 0 && CounterOfferedAmount == 0 && CounterCreatedDay == 0 && CounterExpiresDay == 0 && ReferenceOfferedGood == 0 && ReferenceRequestedGood == 0 && ReferenceRequestedAmount == 0 : CounterId >= 1001 && CounterId < ulong.MaxValue - 8 && (CounterRootId == 0 || CounterRootId >= 1001 && CounterRootId < CounterId) && CounterOfferedAmount >= 1 && CounterOfferedAmount <= 100000 && CounterCreatedDay >= 1 && CounterExpiresDay >= CounterCreatedDay && CounterExpiresDay - CounterCreatedDay <= 20 && (isHuman ? Good(ReferenceOfferedGood, false) && Good(ReferenceRequestedGood, false) && ReferenceOfferedGood != ReferenceRequestedGood && ReferenceRequestedAmount >= 1 && ReferenceRequestedAmount <= 100000 : ReferenceOfferedGood == 0 && ReferenceRequestedGood == 0 && ReferenceRequestedAmount == 0)), AdmissionReason.invalid_command);
        }

        internal bool IsHostSigned
        {
            get
            {
                return HostSequence > 0 && ActorSteamId > 1000;
            }
        }

        internal HumanTradeActionEnvelope WithHostAttestation(ulong hostSequence, ulong authenticatedSteamId)
        {
            Require.That(!IsHostSigned && HostSequence == 0 && ActorSteamId == 0 && hostSequence > 0 && authenticatedSteamId > 1000, AdmissionReason.invalid_command);
            return new HumanTradeActionEnvelope(SessionId, hostSequence, authenticatedSteamId, ActorSlot, RecipientSlot, Action, Tier, OfferedGood, WantedGood, RequestedAmount, HumanOfferedAmount, CounterId, CounterRootId, CounterOfferedAmount, CounterCreatedDay, CounterExpiresDay, ReferenceOfferedGood, ReferenceRequestedGood, ReferenceRequestedAmount, Epoch, ActorSequence, ActorNonce);
        }

        internal Bytes EncodeBody()
        {
            Bytes wire = AdmissionCodec.Build(w =>
            {
                w.Write(SessionId.Copy());
                w.Write(HostSequence);
                w.Write((byte)1); //  fixed action schema
                w.Write(ActorSteamId);
                w.Write(ActorSlot);
                w.Write(RecipientSlot);
                w.Write((byte)Action);
                w.Write(Tier);
                w.Write(OfferedGood);
                w.Write(WantedGood);
                w.Write(RequestedAmount);
                w.Write(HumanOfferedAmount);
                w.Write(CounterId);
                w.Write(CounterRootId);
                w.Write(CounterOfferedAmount);
                w.Write(CounterCreatedDay);
                w.Write(CounterExpiresDay);
                w.Write(ReferenceOfferedGood);
                w.Write(ReferenceRequestedGood);
                w.Write(ReferenceRequestedAmount);
                w.Write(Epoch);
                w.Write(ActorSequence);
                w.Write(ActorNonce);
                w.Write(new byte[3]); // reserved, mandatory zeros
            });
            Require.That(wire.Length == BodyLength, AdmissionReason.malformed);
            return wire;
        }

        internal Bytes ClientIntentPacket()
        {
            Require.That(!IsHostSigned && HostSequence == 0 && ActorSteamId == 0, AdmissionReason.invalid_command);
            return AdmissionCodec.Encode(PacketKind.HumanIntent, EncodeBody(), null);
        }

        internal static HumanTradeActionEnvelope Decode(Bytes body)
        {
            Require.That(body != null && body.Length == BodyLength, AdmissionReason.malformed);
            try
            {
                using (var r = AdmissionCodec.Reader(body))
                {
                    Bytes session = AdmissionCodec.Read(r, 32);
                    ulong hostSeq = r.ReadUInt64();
                    Require.That(r.ReadByte() == 1, AdmissionReason.wrong_version);
                    ulong actorSteam = r.ReadUInt64();
                    byte from = r.ReadByte(), to = r.ReadByte();
                    HumanTradeActionKind action = (HumanTradeActionKind)r.ReadByte();
                    byte tier = r.ReadByte();
                    ushort give = r.ReadUInt16(), want = r.ReadUInt16();
                    int quantity = r.ReadInt32(), literal = r.ReadInt32();
                    ulong counter = r.ReadUInt64(), root = r.ReadUInt64();
                    int counterOffered = r.ReadInt32(), created = r.ReadInt32(), expires = r.ReadInt32();
                    ushort refGive = r.ReadUInt16(), refWant = r.ReadUInt16();
                    int refAmount = r.ReadInt32();
                    ulong epoch = r.ReadUInt64(), sequence = r.ReadUInt64(), nonce = r.ReadUInt64();
                    Require.That(r.ReadByte() == 0 && r.ReadByte() == 0 && r.ReadByte() == 0, AdmissionReason.malformed);
                    AdmissionCodec.End(r);
                    return new HumanTradeActionEnvelope(session, hostSeq, actorSteam, from, to, action, tier, give, want, quantity, literal, counter, root, counterOffered, created, expires, refGive, refWant, refAmount, epoch, sequence, nonce);
                }
            }
            catch (EndOfStreamException)
            {
                throw new AdmissionException(AdmissionReason.malformed);
            }
        }

        internal static HumanTradeActionEnvelope DecodeClientIntent(Bytes wire, Bytes pinnedSession)
        {
            Envelope e = AdmissionCodec.Decode(wire);
            Require.That(e.Kind == PacketKind.HumanIntent, AdmissionReason.wrong_kind);
            HumanTradeActionEnvelope request = Decode(e.Body);
            Require.That(!request.IsHostSigned && request.HostSequence == 0 && request.ActorSteamId == 0 && request.SessionId.Same(pinnedSession), AdmissionReason.session_mismatch);
            return request;
        }
    }
}
