// read-only *adapter preparation* for official SHCDESE 2.14.3.
// Native reads are fail-closed against the current game/Serps PE mismatch.
// Called only after an attested, thread-bound and exclusive inventory lease.
// Not wired to live tick and never requests any Native write capability.
using System;
using System.Linq;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ReadOnlyNativeAiCapacityEvidence
    {
        internal byte PlayerId;
        internal ushort Good;
        internal int PendingPurchases;
        internal uint LastStandPhase;
        internal int GameDay;
        internal bool IsAlive;
        internal bool ExactSourceRead;
        internal bool LastStandVerified
        {
            get
            {
                return LastStandPhase >= 1 && LastStandPhase <= 8;
            }
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // runtime module path/disk SHA alone cannot attest native
        // memory layout/lease. The explicit ABI gate remains FALSE, so even
        // on the currently SHA-matched Steam game no AI pointer reads occur.
        // No live-tick dispatch; this code is staged research only.
        private unsafe bool TryReadAiEvidence(int player, ushort good, out ReadOnlyNativeAiCapacityEvidence evidence)
        {
            evidence = null;
            string diagnostic;
            if (!SerpsRawraCompat.AttestedLoadedNativeCompatibility(out diagnostic) || !_mapActive || _mapDisabled || _mapBootstrapPending || _inventoryRestorePending || _goodsTransfersUnavailable || _pendingPackage != null || _nativeTransferProbe != null || GameNetworkAPI.IsNetworkedEnvironment() || player < 1 || player > 8 || StrongholdEuropeDE.TradeModels.GoodCatalog.Find(good) == null)
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            try
            {
                using (lease)
                {
                    var players = GamePlayerManagerAPI.Instance;
                    var ai = GameAIManagerAPI.Instance;
                    if (players == null || ai == null || !players.IsPlayerIdValid(player) || !players.GetAlivePlayerIds().Contains(player))
                        return false;
                    GamePlayerResources* original;
                    if (!players.TryGetPlayerResourcesById(player, out original) || original == null)
                        return false;
                    // New currentSave.14.3 named field replaces an unknown UINT32 at the
                    // same documented offset 0x38F0; values >8 are invalid.
                    uint lastStand = original->r_AILastStandPhase;
                    if (lastStand > 8U)
                        return false;
                    int requested = ai.GetGoodFromPendingPurchases(player, (eGoods)good);
                    if (requested < 0)
                        return false;
                    evidence = new ReadOnlyNativeAiCapacityEvidence
                    {
                        PlayerId = (byte)player,
                        Good = good,
                        PendingPurchases = requested,
                        LastStandPhase = lastStand,
                        GameDay = Math.Max(1, _lastProcessedDay),
                        IsAlive = true,
                        ExactSourceRead = true
                    };
                    return true;
                }
            }
            catch (Exception e)
            {
                Log("aid_evidence", "status=rawra_ai_snapshot_refused|reason=" + Safe(e.GetType().Name));
                evidence = null;
                return false;
            }
        }
    }
}
