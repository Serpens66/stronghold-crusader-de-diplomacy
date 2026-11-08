// WorldFrame Kind8 ExtendedState: CONSENT-ONLY original-world digest.
// Same existing OrderedWorldDigestBodyCodec (101 byte layout, version2),
// same selected  RSA/ChoreSession/sequence; no new packet or wire kind.
// This digest is explicitly NOT the physically complete capacity digest
// MpSnapshotDigest.Compute, nor a native/save/settlement authority.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class ConsentWorldDigest
    {
        // Distinct literal domain from
        // _ORIGINAL_MP_WORLD_DIGEST_SHARED_EPOCH_.
        // Kind8 with 101-byte OrderedWorldDigestBodyCodec.Version=2
        // MUST be used for this consent-only digest in . Do not
        // silently use a physical capacity digest as a consent frame.
        internal const string Domain = StrongholdEuropeDE.Runtime.CompatibilityContracts.ConsentWorldDigestDomain;
        internal const byte DigestModeConsentOnly = 1;
        internal const byte ExactUnknownCapacityCode = (byte)OriginalCapacityAttestationState.Unknown;
        internal static byte[] Compute(OriginalMultiplayerConsentFrame frame)
        {
            if (frame == null || frame.NativePhysicalSettlementAuthorized || frame.VerifiedRoster == null || frame.NativeSharedTick < 1 || frame.NativeGameDay < 1 || frame.OriginalPinnedSignedSessionId == null || frame.OriginalPinnedSignedSessionId.Length != 32 || frame.FullOriginalMapIdentity == null)
                throw new InvalidDataException("Consent-only original frame unproven");
            var players = frame.Players().OrderBy(x => x.Slot).ToArray();
            var prices = frame.Market().OrderBy(x => x.Id).ToArray();
            var rulers = frame.Profiles().OrderBy(x => x.PlayerId).ToArray();
            var goods = frame.Goods().OrderBy(x => x.Player).ThenBy(x => x.GoodId).ToArray();
            if (players.Length < 2 || players.Length > 8 || players.Count(x => x.Human) < 2 || prices.Length != GoodCatalog.All.Count || rulers.Length != players.Length || goods.Length != players.Length * GoodCatalog.All.Count || frame.UnknownCapacityCells() != goods.Length || goods.Any(x => x == null || x.CapacityState != OriginalCapacityAttestationState.Unknown || x.ExactCapacity.HasValue))
                throw new InvalidDataException("Consent-only digest cannot contain physical MP capacity");
            // Only truly shared roles, goods, market, native live/day/tick
            // and signed world scope go into this cross-client hash.
            // EXCLUDE process-local _mapEpoch, localSteamId, localPlayer,
            // featureTicks and process callback counts.
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Domain);
                w.Write(DigestModeConsentOnly);
                w.Write(frame.CopyTrustedScope());
                w.Write(frame.FullOriginalMapIdentity.ScopeHash());
                w.Write(frame.OriginalPinnedSignedSessionId.Copy());
                w.Write(frame.SharedMapFingerprint);
                w.Write(frame.SharedRosterFingerprint);
                w.Write(frame.OriginalHostSteamId);
                w.Write(frame.NativeSharedTick);
                w.Write(frame.NativeGameDay);
                w.Write(players.Length);
                foreach (var p in players)
                {
                    w.Write(p.Slot);
                    w.Write(p.Human);
                    w.Write(p.Alive);
                    w.Write(p.SteamId);
                }

                w.Write(prices.Length);
                foreach (var p in prices)
                {
                    w.Write(p.Id);
                    w.Write(p.MarketBuy);
                    w.Write(p.MarketSell);
                }

                w.Write(rulers.Length);
                foreach (var r in rulers)
                {
                    w.Write(r.PlayerId);
                    w.Write(r.Trust);
                    w.Write(r.Greed);
                    w.Write(r.Caution);
                    w.Write(r.Commerce);
                    w.Write(r.AtWar);
                }

                w.Write(goods.Length);
                foreach (var g in goods)
                {
                    if (g.NativeQuantity < 0 || g.ConservativeReserve < 0 || g.ConservativeReserve > g.NativeQuantity || g.OriginalAiPendingPurchases < 0)
                        throw new InvalidDataException("Original native MP stock/reserve invalid");
                    w.Write(g.Player);
                    w.Write(g.GoodId);
                    w.Write(g.NativeQuantity);
                    w.Write(g.ConservativeReserve);
                    w.Write(g.OriginalAiPendingPurchases);
                    // Explicit tagged Unknown; this is NOT capacity zero
                    // and does not assert usable storage/settlement space.
                    w.Write(ExactUnknownCapacityCode);
                }

                bool[, ] relations = frame.OriginalAlliances();
                if (relations == null || relations.GetLength(0) != 9 || relations.GetLength(1) != 9)
                    throw new InvalidDataException("Original directed relationship matrix incomplete");
                foreach (var from in players)
                    foreach (var to in players)
                    {
                        if (from.Slot == to.Slot)
                            continue;
                        w.Write(from.Slot);
                        w.Write(to.Slot);
                        w.Write(relations[from.Slot, to.Slot]);
                        w.Write(relations[to.Slot, from.Slot]);
                    }

                w.Flush();
                using (var sha = SHA256.Create())
                    return sha.ComputeHash(stream.ToArray());
            }
        }

        internal static bool IsSameConsentOnlyDigest(byte[] first, byte[] second)
        {
            return MpSnapshotDigest.Equal32(first, second);
        }
    }
}
