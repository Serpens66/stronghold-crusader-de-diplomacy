// canonical transport protocol. A payload/checksum is NOT authentication.
// Only Steam-provided sender metadata and engine-owned map/host observations can
// authorize dispatch. Original native/save mutation release gates remain closed.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace StrongholdEuropeDE.Runtime
{
    internal enum WireKind : byte
    {
        PlayerRequest = 1,
        HostDecision = 2,
        HostCommitReceipt = 3,
        HostAbort = 4,
        ClientAck = 5,
        HostAutonomousOffer = 6
    }

    internal sealed class TradeWireMessage
    {
        internal const int MaximumEncodedBytes = 2048, MaxTextBytes = 240;
        internal const int Version = 1;
        internal const int Magic = 0x56313036;
        internal WireKind Kind;
        internal string Map, Roster;
        internal ulong HostTerm, Sequence, QuoteId, RootId, SaveEpoch;
        internal byte SenderSlot, HostSlot, Requester, Recipient;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount, IssuedDay, ExpiresDay;
        internal byte Decision;
        internal string QuoteDigest;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static bool Good(int id)
        {
            return new[]
            {
                2,
                4,
                6,
                7,
                9,
                10,
                11,
                12,
                13,
                14,
                16,
                17,
                18,
                19,
                20,
                21,
                22,
                23,
                24
            }.Contains(id);
        }

        private static void TextValid(string text, string label)
        {
            if (text == null || text.Length == 0 || Utf8.GetByteCount(text) > MaxTextBytes)
                throw new InvalidDataException(label + " size");
            foreach (char c in text)
                if (c < ' ' || c == 127)
                    throw new InvalidDataException(label + " control character");
        }

        private static bool LowerHex64(string text)
        {
            if (text == null || text.Length != 64)
                return false;
            foreach (char c in text)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            return true;
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(WireKind), Kind) || SenderSlot < 1 || SenderSlot > 8 || HostSlot < 1 || HostSlot > 8 || Requester < 1 || Requester > 8 || Recipient < 1 || Recipient > 8 || Requester == Recipient || HostTerm < 1 || HostTerm >= ulong.MaxValue - 1 || Sequence < 1 || Sequence >= ulong.MaxValue - 1 || QuoteId < 1001 || QuoteId >= ulong.MaxValue - 8 || RootId != 0 && (RootId < 1001 || RootId >= QuoteId) || !Good(OfferedGood) || !Good(RequestedGood) || OfferedGood == RequestedGood || OfferedAmount < 1 || OfferedAmount > 100000 || RequestedAmount < 1 || RequestedAmount > 100000 || IssuedDay < 1 || ExpiresDay < IssuedDay || (long)ExpiresDay - IssuedDay > 15 || Decision > 3 || Kind == WireKind.HostCommitReceipt && SaveEpoch == 0 || Kind != WireKind.HostCommitReceipt && SaveEpoch != 0)
                throw new InvalidDataException("invalid  trade wire terms");
            TextValid(Map, "map");
            TextValid(Roster, "roster");
            if (!LowerHex64(QuoteDigest) || !string.Equals(QuoteDigest, ComputeQuoteDigest(), StringComparison.Ordinal))
                throw new InvalidDataException("quote digest mismatch");
            bool hostMessage = Kind == WireKind.HostDecision || Kind == WireKind.HostCommitReceipt || Kind == WireKind.HostAbort || Kind == WireKind.HostAutonomousOffer;
            if (hostMessage && SenderSlot != HostSlot)
                throw new InvalidDataException("host message not host");
            if (Kind == WireKind.PlayerRequest && SenderSlot != Requester)
                throw new InvalidDataException("request is not from requester");
            if (Kind == WireKind.ClientAck && SenderSlot != Requester && SenderSlot != Recipient)
                throw new InvalidDataException("ack foreign slot");
            if (Kind != WireKind.HostDecision && Decision != 0)
                throw new InvalidDataException("unexpected decision code");
        }

        private static void WriteText(BinaryWriter w, string value)
        {
            byte[] raw = Utf8.GetBytes(value);
            if (raw.Length < 1 || raw.Length > MaxTextBytes)
                throw new InvalidDataException("wire string size");
            w.Write((byte)raw.Length);
            w.Write(raw);
        }

        private static string ReadText(BinaryReader r)
        {
            int n = r.ReadByte();
            if (n < 1 || n > MaxTextBytes || n > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException("bounded string failed");
            byte[] data = r.ReadBytes(n);
            if (data.Length != n)
                throw new EndOfStreamException();
            string s = Utf8.GetString(data);
            TextValid(s, "decoded string");
            if (!Utf8.GetBytes(s).SequenceEqual(data))
                throw new InvalidDataException("noncanonical utf8");
            return s;
        }

        internal string ComputeQuoteDigest()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Utf8, true))
            {
                WriteText(w, Map);
                WriteText(w, Roster);
                w.Write(HostTerm);
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
                w.Flush();
                using (var h = SHA256.Create())
                    return BitConverter.ToString(h.ComputeHash(ms.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static byte[] Encode(TradeWireMessage m)
        {
            if (m == null)
                throw new ArgumentNullException("message");
            m.Validate();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Utf8, true))
            {
                w.Write(Magic);
                w.Write((byte)Version);
                w.Write((byte)m.Kind);
                WriteText(w, m.Map);
                WriteText(w, m.Roster);
                w.Write(m.HostTerm);
                w.Write(m.Sequence);
                w.Write(m.QuoteId);
                w.Write(m.RootId);
                w.Write(m.SaveEpoch);
                w.Write(m.SenderSlot);
                w.Write(m.HostSlot);
                w.Write(m.Requester);
                w.Write(m.Recipient);
                w.Write(m.OfferedGood);
                w.Write(m.RequestedGood);
                w.Write(m.OfferedAmount);
                w.Write(m.RequestedAmount);
                w.Write(m.IssuedDay);
                w.Write(m.ExpiresDay);
                w.Write(m.Decision);
                w.Write(Convert.FromBase64String(Convert.ToBase64String(HexToBytes(m.QuoteDigest))));
                w.Flush();
                byte[] canonical = ms.ToArray();
                using (var h = SHA256.Create())
                    w.Write(h.ComputeHash(canonical));
                w.Flush();
                if (ms.Length > MaximumEncodedBytes)
                    throw new InvalidDataException("wire oversized");
                return ms.ToArray();
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            byte[] b = new byte[32];
            for (int i = 0; i < 32; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        internal static TradeWireMessage Decode(byte[] raw)
        {
            if (raw == null || raw.Length < 4 + 1 + 1 + 32 || raw.Length > MaximumEncodedBytes)
                throw new InvalidDataException("invalid packet size");
            int canonical = raw.Length - 32;
            using (var h = SHA256.Create())
            {
                byte[] expected = h.ComputeHash(raw, 0, canonical);
                int diff = 0;
                for (int i = 0; i < 32; i++)
                    diff |= raw[canonical + i] ^ expected[i];
                if (diff != 0)
                    throw new InvalidDataException("packet checksum incorrect");
            }

            using (var ms = new MemoryStream(raw, false))
            using (var r = new BinaryReader(ms, Utf8, true))
            {
                if (r.ReadInt32() != Magic || r.ReadByte() != Version)
                    throw new InvalidDataException("wire version/magic incorrect");
                var x = new TradeWireMessage
                {
                    Kind = (WireKind)r.ReadByte(),
                    Map = ReadText(r),
                    Roster = ReadText(r),
                    HostTerm = r.ReadUInt64(),
                    Sequence = r.ReadUInt64(),
                    QuoteId = r.ReadUInt64(),
                    RootId = r.ReadUInt64(),
                    SaveEpoch = r.ReadUInt64(),
                    SenderSlot = r.ReadByte(),
                    HostSlot = r.ReadByte(),
                    Requester = r.ReadByte(),
                    Recipient = r.ReadByte(),
                    OfferedGood = r.ReadUInt16(),
                    RequestedGood = r.ReadUInt16(),
                    OfferedAmount = r.ReadInt32(),
                    RequestedAmount = r.ReadInt32(),
                    IssuedDay = r.ReadInt32(),
                    ExpiresDay = r.ReadInt32(),
                    Decision = r.ReadByte()
                };
                byte[] digest = r.ReadBytes(32);
                if (digest.Length != 32 || ms.Position != canonical)
                    throw new InvalidDataException("unexpected trailing wire bytes");
                x.QuoteDigest = ToHex(digest);
                x.Validate();
                return x;
            }
        }
    }

    // The evidence below must be populated by the *transport callback* from
    // engine/Steam metadata. Values taken from the packet are never evidence.
    internal sealed class SteamDeliveryEvidence
    {
        internal ulong AuthenticatedSenderSteamId, AuthenticatedLobbyHostSteamId;
        internal ulong[] SlotSteamIds = new ulong[9];
        internal int LocalSlot;
        internal bool NetworkEnvironment, Multiplayer, LocalHost, ExactLiveRoster, ExactEngineSession;
        internal bool OriginalEngineSaveDurable, NativeFourCellsAttested;
    }

    internal static class TradeSteamAdmission
    {
        internal static bool TryAuthorize(TradeWireMessage packet, SteamDeliveryEvidence evidence, string expectedMap, string expectedRoster, ulong expectedTerm, ulong lastSeenSequence, out string reason)
        {
            reason = "untrusted_delivery";
            if (packet == null || evidence == null || evidence.SlotSteamIds == null || evidence.SlotSteamIds.Length != 9 || !evidence.NetworkEnvironment || !evidence.Multiplayer || !evidence.ExactLiveRoster || !evidence.ExactEngineSession || evidence.LocalSlot < 1 || evidence.LocalSlot > 8 || evidence.AuthenticatedSenderSteamId <= 1000 || evidence.AuthenticatedLobbyHostSteamId <= 1000)
                return false;
            try
            {
                packet.Validate();
            }
            catch (Exception)
            {
                reason = "malformed";
                return false;
            }

            if (!string.Equals(packet.Map, expectedMap, StringComparison.Ordinal) || !string.Equals(packet.Roster, expectedRoster, StringComparison.Ordinal) || packet.HostTerm != expectedTerm || packet.Sequence <= lastSeenSequence)
            {
                reason = "scope_or_replay";
                return false;
            }

            if (evidence.SlotSteamIds[packet.SenderSlot] != evidence.AuthenticatedSenderSteamId || evidence.SlotSteamIds[packet.HostSlot] != evidence.AuthenticatedLobbyHostSteamId)
            {
                reason = "sender_or_host_identity_mismatch";
                return false;
            }

            bool hostIssued = packet.Kind == WireKind.HostDecision || packet.Kind == WireKind.HostCommitReceipt || packet.Kind == WireKind.HostAbort || packet.Kind == WireKind.HostAutonomousOffer;
            if (hostIssued && evidence.AuthenticatedSenderSteamId != evidence.AuthenticatedLobbyHostSteamId)
            {
                reason = "non_host_claiming_host";
                return false;
            }

            if (!hostIssued && !evidence.LocalHost)
            {
                // Clients can receive host replies, but do not process peer requests.
                reason = "peer_request_not_delivered_to_host";
                return false;
            }

            if (packet.Kind == WireKind.HostCommitReceipt && (!evidence.OriginalEngineSaveDurable || !evidence.NativeFourCellsAttested))
            {
                reason = "missing_native_durable_commit_proof";
                return false;
            }

            reason = "authorized_transport_only";
            return true;
        }
    }

    // A callback can enqueue between map unload and queue drain.
    // Bind every Steam notification to both independent local lifecycle epochs.
    // Neither epoch is ever taken from sender-supplied packet fields.
    internal static class DeliveryEpoch
    {
        internal static bool Matches(long receivedMap, long receivedGeneration, long activeMap, long activeGeneration)
        {
            return receivedMap > 0 && receivedGeneration > 0 && receivedMap == activeMap && receivedGeneration == activeGeneration;
        }
    }

    // Semantic quote binding is independent of a caller-controlled checksum.
    // It must compare to an already persisted and validated server-side record
    // BEFORE consuming an authenticated sender's monotonic sequence number.
    internal sealed class CanonicalQuote
    {
        internal ulong Id, RootId;
        internal byte Requester, Recipient;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount, IssuedDay, ExpiresDay;
    }

    internal static class QuoteBinding
    {
        internal static bool Matches(TradeWireMessage incoming, CanonicalQuote recorded)
        {
            if (incoming == null || recorded == null)
                return false;
            try
            {
                incoming.Validate();
            }
            catch (Exception)
            {
                return false;
            }

            return incoming.QuoteId == recorded.Id && incoming.RootId == recorded.RootId && incoming.Requester == recorded.Requester && incoming.Recipient == recorded.Recipient && incoming.OfferedGood == recorded.OfferedGood && incoming.RequestedGood == recorded.RequestedGood && incoming.OfferedAmount == recorded.OfferedAmount && incoming.RequestedAmount == recorded.RequestedAmount && incoming.IssuedDay == recorded.IssuedDay && incoming.ExpiresDay == recorded.ExpiresDay;
        }

        // Packets have no standing merely because their hash and sender are valid.
        // Match the authoritative, already saved quote BEFORE replay admission.
        // Unsolicited requests/host-created offers require a separate validated
        // host-intake protocol, which does not exist in this release.
        internal static bool TryAcceptExisting(TradeWireMessage incoming, CanonicalQuote saved, int gameDay, out string reason)
        {
            reason = "unknown_quote";
            if (incoming == null || saved == null || !Matches(incoming, saved))
                return false;
            if (gameDay < saved.IssuedDay || gameDay > saved.ExpiresDay)
            {
                reason = "quote_expired_or_future";
                return false;
            }

            if (incoming.Kind == WireKind.PlayerRequest || incoming.Kind == WireKind.HostAutonomousOffer)
            {
                reason = "new_offer_requires_authenticated_host_intake";
                return false;
            }

            if (incoming.Kind == WireKind.HostCommitReceipt)
            {
                reason = "native_save_commit_unattested";
                return false;
            }

            if (incoming.Kind != WireKind.HostDecision && incoming.Kind != WireKind.HostAbort && incoming.Kind != WireKind.ClientAck)
            {
                reason = "unrecognized_quote_message";
                return false;
            }

            reason = "known_quote_transport_observation_only";
            return true;
        }
    }

    // An admission result is NOT a license to mutate the engine. This detached
    // monotonic state is maintained alongside the actual game SaveData journal.
    internal sealed class TradeSteamReplay
    {
        internal ulong HostTerm;
        internal readonly ulong[] LastSeenBySender = new ulong[9];
        internal void Validate()
        {
            if (HostTerm >= ulong.MaxValue - 1 || LastSeenBySender[0] != 0 || HostTerm == 0 && LastSeenBySender.Skip(1).Any(x => x != 0) || LastSeenBySender.Skip(1).Any(x => x >= ulong.MaxValue - 1))
                throw new InvalidDataException("untrusted multiplayer replay ledger");
        }

        internal TradeSteamReplay Copy()
        {
            var x = new TradeSteamReplay
            {
                HostTerm = HostTerm
            };
            Array.Copy(LastSeenBySender, x.LastSeenBySender, 9);
            return x;
        }

        internal bool TryAdmit(TradeWireMessage m, SteamDeliveryEvidence evidence, string map, string roster, ulong term, out string reason)
        {
            if (m == null || m.SenderSlot < 1 || m.SenderSlot > 8)
            {
                reason = "invalid_sender";
                return false;
            }

            if (HostTerm != 0 && HostTerm != term)
            {
                reason = "host_term_changed";
                return false;
            }

            if (!TradeSteamAdmission.TryAuthorize(m, evidence, map, roster, term, LastSeenBySender[m.SenderSlot], out reason))
                return false;
            if (HostTerm == 0)
                HostTerm = term;
            LastSeenBySender[m.SenderSlot] = m.Sequence;
            return true;
        }

        internal bool TryAdmitExisting(TradeWireMessage m, CanonicalQuote saved, int gameDay, SteamDeliveryEvidence evidence, string map, string roster, ulong trustedHostTerm, out string reason)
        {
            // Semantic refusal must be side-effect free, even for an otherwise
            // authenticated Steam sender with a large monotonic sequence number.
            if (!QuoteBinding.TryAcceptExisting(m, saved, gameDay, out reason))
                return false;
            return TryAdmit(m, evidence, map, roster, trustedHostTerm, out reason);
        }
    }
}
