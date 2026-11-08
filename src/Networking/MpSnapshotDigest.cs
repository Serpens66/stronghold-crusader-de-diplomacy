//  source-only cross-peer read-side consistency and fail-closed
// host witness contract. The hash of native-observed state is NOT a
// signature: only the EXISTING one globally ordered, RSA-verified original
// Chore may supply a trusted host digest. No extra transport/Save/Native hook.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal static class MpSnapshotDigest
    {
        internal const int HashBytes = 32;
        internal static byte[] Compute(CanonicalTradeSnapshot world, VerifiedOriginalHumanRoster roster)
        {
            if (world == null || roster == null || world.MapGeneration != roster.OriginalMapGeneration || world.SharedTick != roster.OriginalSharedTick || world.Day != roster.Day || !string.Equals(world.MapFingerprint, roster.GlobalMapFingerprint, StringComparison.Ordinal) || !string.Equals(world.RosterFingerprint, roster.GlobalRosterFingerprint, StringComparison.Ordinal) || world.LocalOfflineHumanSlot != 0)
                throw new InvalidDataException("MP world is not pinned to original roster");
            var players = world.Players().OrderBy(x => x.Slot).ToArray();
            var original = roster.Players().OrderBy(x => x.Slot).ToArray();
            if (players.Length != original.Length || !players.Select(x => x.Slot).SequenceEqual(original.Select(x => x.Slot)))
                throw new InvalidDataException("MP snapshot player set changed");
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].Human != original[i].Human || players[i].Alive != original[i].Alive || players[i].SteamId != original[i].SteamId)
                    throw new InvalidDataException("MP snapshot Steam/AI role changed");
            }

            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.MultiplayerWorldDigestDomain);
                w.Write(roster.Scope());
                w.Write(world.MapFingerprint);
                w.Write(world.RosterFingerprint);
                // MapGeneration is a LOCAL process lifecycle cursor,
                // rechecked above against its own native roster witness.
                // Different clients can have different local generation
                // values for the same signed session; never hash it.
                w.Write(world.SharedTick);
                w.Write(world.Day);
                w.Write(players.Length);
                foreach (var p in players)
                {
                    w.Write(p.Slot);
                    w.Write(p.Human);
                    w.Write(p.Alive);
                    w.Write(p.SteamId);
                }

                // Only original numeric market data. Human UI good names,
                // local translations and AI portrait names are not shared
                // simulation state and must never affect cross-peer hashes.
                var prices = world.Prices().OrderBy(x => x.Id).ToArray();
                w.Write(prices.Length);
                foreach (var price in prices)
                {
                    w.Write(price.Id);
                    w.Write(price.MarketBuy);
                    w.Write(price.MarketSell);
                }

                var rulers = world.Rulers(false).OrderBy(x => x.PlayerId).ToArray();
                w.Write(rulers.Length);
                foreach (var ruler in rulers)
                {
                    w.Write(ruler.PlayerId);
                    w.Write(ruler.Trust);
                    w.Write(ruler.Greed);
                    w.Write(ruler.Caution);
                    w.Write(ruler.Commerce);
                    w.Write(ruler.AtWar);
                }

                foreach (var player in players)
                    foreach (var good in GoodCatalog.All.OrderBy(x => x.Id))
                    {
                        var cell = world.Read(player.Slot, good.Id);
                        if (cell == null)
                            throw new InvalidDataException("Original MP goods cell missing");
                        cell.Validate();
                        w.Write(player.Slot);
                        w.Write(good.Id);
                        w.Write(cell.Quantity);
                        w.Write(cell.Capacity);
                        w.Write(cell.CoreReserve);
                        w.Write(cell.DailyConsumption);
                        w.Write(cell.DailyProduction);
                        w.Write(cell.Buildings);
                        w.Write(cell.Military);
                    }

                // Encode the EXACT pair policy seen by the reducer, not
                // a guessed cross-client "everyone allied" relation.
                // For an unproven directed pair, serialize explicit denial.
                foreach (var from in players)
                    foreach (var to in players)
                    {
                        if (from.Slot == to.Slot)
                            continue;
                        w.Write(from.Slot);
                        w.Write(to.Slot);
                        ObservedTradePairState pair;
                        bool allowed = world.TryGetPair(from.Slot, to.Slot, world.Day, out pair);
                        w.Write(allowed);
                        if (!allowed)
                            continue;
                        if (pair == null || !pair.Authoritative || !pair.RequesterAlive || !pair.RecipientAlive || !pair.HasTradeRelation || pair.Hostile)
                            throw new InvalidDataException("Untrusted allowed pair");
                        w.Write(pair.Day);
                        w.Write(pair.Goodwill);
                        w.Write(pair.Trust);
                        w.Write(pair.Grudge);
                        w.Write(pair.Threat);
                        w.Write(pair.Vassal);
                        w.Write(pair.Overlord);
                    }

                w.Flush();
                using (var sha = SHA256.Create())
                    return sha.ComputeHash(stream.ToArray());
            }
        }

        internal static bool Equal32(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != HashBytes || b.Length != HashBytes)
                return false;
            int difference = 0;
            for (int i = 0; i < HashBytes; i++)
                difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }

    // Version 2: same fixed 101 bytes; the eighth-byte-sized
    // lifecycle field is now canonical InputEpoch, never remote mapEpoch.
    // Pure 101-byte HOST WORLD CONTENT for a possible new typed action in
    // the EXISTING signed globally ordered ChoreSession. The codec does NOT
    // sign packets, accept Steam as Chore or grant Native/save authority.
    // The owner must add it to the existing packet-kind switch/sequence
    // only after agreeing on a single  protocol implementation.
    internal static class OrderedWorldDigestBodyCodec
    {
        internal const uint Magic = 0x30323156; // LE ASCII ""
        internal const byte Version = 2;
        internal const int BodyBytes = 4 + 1 + 32 + 8 + 8 + 8 + 8 + 32;
        internal static byte[] Encode(OrderedHostWorldWitness witness)
        {
            if (witness == null)
                throw new ArgumentNullException("witness");
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(witness.OriginalSessionId.Copy());
                w.Write(witness.SignedHostChoreSequence);
                w.Write(witness.OriginalHostSteamId);
                w.Write(witness.OriginalSharedTick);
                w.Write(witness.CanonicalInputEpoch);
                w.Write(witness.OriginalWorldSha256);
                w.Flush();
                if (stream.Length != BodyBytes)
                    throw new InvalidDataException("World digest body length changed");
                return stream.ToArray();
            }
        }

        // Parsing this BODY is permitted only AFTER signed Chore verification
        // and native managed return. Decoded fields carry NO authority alone.
        internal static bool TryDecode(byte[] body, out OrderedHostWorldWitness witness)
        {
            witness = null;
            if (body == null || body.Length != BodyBytes)
                return false;
            try
            {
                using (var stream = new MemoryStream(body, false))
                using (var r = new BinaryReader(stream))
                {
                    if (r.ReadUInt32() != Magic || r.ReadByte() != Version)
                        return false;
                    var session = new Bytes(r.ReadBytes(32));
                    ulong sequence = r.ReadUInt64();
                    ulong host = r.ReadUInt64();
                    long tick = r.ReadInt64();
                    ulong epoch = r.ReadUInt64();
                    byte[] digest = r.ReadBytes(32);
                    if (stream.Position != BodyBytes || digest.Length != 32)
                        return false;
                    var decoded = new OrderedHostWorldWitness(session, digest, sequence, host, tick, epoch);
                    if (!Encode(decoded).SequenceEqual(body))
                        return false;
                    witness = decoded;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // The ONLY owner that can create an authenticated host witness is the
    // existing post-ReceiveChore RSA-verifying HostSignedTradeSession. This
    // immutable container cannot authenticate itself. Do not construct
    // from a received Steam packet, UI click or unsigned synthetic data.
    internal sealed class OrderedHostWorldWitness
    {
        internal readonly Bytes OriginalSessionId;
        private readonly byte[] _worldSha256;
        internal byte[] OriginalWorldSha256
        {
            get
            {
                return (byte[])_worldSha256.Clone();
            }
        }

        internal readonly ulong SignedHostChoreSequence, OriginalHostSteamId;
        internal readonly long OriginalSharedTick;
        internal readonly ulong CanonicalInputEpoch;
        internal OrderedHostWorldWitness(Bytes signedSession, byte[] originalWorldHash, ulong sequence, ulong hostSteam, long sharedTick, ulong canonicalInputEpoch)
        {
            if (signedSession == null || signedSession.Length != 32 || originalWorldHash == null || originalWorldHash.Length != 32 || sequence == 0 || sequence >= ulong.MaxValue - 1 || hostSteam <= 1000 || sharedTick < 1 || canonicalInputEpoch < 1 || canonicalInputEpoch >= ulong.MaxValue - 1)
                throw new ArgumentException("Unproven shared original world witness");
            OriginalSessionId = new Bytes(signedSession.Copy());
            _worldSha256 = (byte[])originalWorldHash.Clone();
            SignedHostChoreSequence = sequence;
            OriginalHostSteamId = hostSteam;
            OriginalSharedTick = sharedTick;
            CanonicalInputEpoch = canonicalInputEpoch;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // Called ONLY from the existing signer's authenticated ordered-Chore
        // consumer, AFTER the native callback returns and its original
        // engine monitor is held. This method compares numeric content;
        // it DOES NOT establish the witness's RSA authenticity itself.
        private bool MatchesOrderedHostWorld(CanonicalTradeSnapshot snapshot, VerifiedOriginalHumanRoster roster, OrderedHostWorldWitness alreadyVerifiedHostWitness, ulong savedCanonicalInputEpoch, ulong verifiedGlobalChoreSequence, out string reason)
        {
            reason = "host_world_digest_or_signature_not_attested";
            if (!IsNativeDiplomacySectionHeld || snapshot == null || roster == null || alreadyVerifiedHostWitness == null || savedCanonicalInputEpoch < 1 || verifiedGlobalChoreSequence < 1 || _signed == null || !_signed.Live || !_signed.Ready || _signed.Descriptor == null)
                return false;
            try
            {
                if (alreadyVerifiedHostWitness.OriginalHostSteamId != _signed.Context.HostSteamId || alreadyVerifiedHostWitness.OriginalSharedTick != snapshot.SharedTick || alreadyVerifiedHostWitness.CanonicalInputEpoch != savedCanonicalInputEpoch || alreadyVerifiedHostWitness.SignedHostChoreSequence != verifiedGlobalChoreSequence || !alreadyVerifiedHostWitness.OriginalSessionId.Same(_signed.Descriptor.Id))
                {
                    reason = "different_original_signed_world_context";
                    return false;
                }

                // Local map lifecycle is checked ONLY against local engine
                // data, never against the host's independent mapEpoch.
                if (roster.OriginalMapGeneration != _mapEpoch || snapshot.MapGeneration != roster.OriginalMapGeneration || roster.OriginalSharedTick != snapshot.SharedTick)
                {
                    reason = "local_native_generation_or_tick_changed";
                    return false;
                }

                byte[] actual = MpSnapshotDigest.Compute(snapshot, roster);
                if (!MpSnapshotDigest.Equal32(actual, alreadyVerifiedHostWitness.OriginalWorldSha256))
                {
                    reason = "host_peer_goods_or_diplomacy_differ";
                    return false;
                }

                reason = "snapshot_content_matches_host_signed_world_but_native_mp_still_closed";
                return true;
            }
            catch (Exception e)
            {
                reason = "world_digest_refused:" + e.GetType().Name;
                return false;
            }
        }
    }
}
