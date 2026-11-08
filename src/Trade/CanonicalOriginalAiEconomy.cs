// Provenance-only, immutable original AI economy inputs for ONE captured game tick.
// No game API, market order, goods write, SaveData, Steam or new owner.
using System;
using System.Collections.Generic;
using System.Linq;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class CanonicalOriginalAiEconomy
    {
        internal readonly byte PlayerSlot;
        internal readonly int OriginalLordId;
        internal readonly int OriginalTeamId;
        internal readonly int OriginalGold;
        internal readonly int OriginalAicGoldReserveSignal;
        // A physical original tradepost is observed separately from trading prices.
        // This cannot guarantee a particular native market transaction succeeds.
        internal readonly bool OriginalMarketBuildingObserved, OriginalTradepostPresent;
        // SHA-256 of fixed, explicitly observed native AIC numeric signals,
        // NOT a hash of the full custom-lord asset or an authenticating signature.
        internal readonly string OriginalAicSignalFingerprint;
        private readonly Dictionary<ushort,int> _pendingOriginalPurchases;
        // Observed AIC numeric thresholds are only heuristic demand signals.
        // They do not prove a particular original-market purchase is due.
        private readonly Dictionary<ushort,int> _nativeAicDemandSignals;

        internal CanonicalOriginalAiEconomy(byte slot, int lordId, int teamId,
            int gold, int goldReserveSignal, string aicSignalsHash,
            IEnumerable<KeyValuePair<ushort,int>> pendingPurchases,
            IEnumerable<KeyValuePair<ushort,int>> demandSignals,
            bool marketBuildingObserved = false, bool originalTradepostPresent = false)
        {
            if (slot < 1 || slot > 8 || lordId < 1 || lordId > 37 ||
                teamId < 0 || teamId > 255 || gold < 0 || gold > 1000000000 ||
                goldReserveSignal < 0 || goldReserveSignal > 1000000000 ||
                aicSignalsHash == null || aicSignalsHash.Length != 64 ||
                aicSignalsHash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) ||
                pendingPurchases == null || demandSignals == null ||
                (originalTradepostPresent && !marketBuildingObserved))
                throw new ArgumentException("Unverified original AI economic identity");
            var copy = new Dictionary<ushort,int>();
            foreach (var item in pendingPurchases)
            {
                if (GoodCatalog.Find(item.Key) == null || item.Value < 0 ||
                    item.Value > 1000000 || copy.ContainsKey(item.Key))
                    throw new ArgumentException("Unverified/duplicated original pending purchase");
                copy.Add(item.Key,item.Value);
            }
            if (copy.Count != GoodCatalog.All.Count)
                throw new ArgumentException("Incomplete original pending-purchase observation");
            var targets = new Dictionary<ushort,int>();
            foreach (var item in demandSignals)
            {
                if (GoodCatalog.Find(item.Key) == null || item.Value < 0 ||
                    item.Value > 1000000 || targets.ContainsKey(item.Key))
                    throw new ArgumentException("Unverified original AIC economic signal");
                targets.Add(item.Key, item.Value);
            }
            if (targets.Count != GoodCatalog.All.Count)
                throw new ArgumentException("Incomplete original AIC signal observation");
            _nativeAicDemandSignals = targets;
            PlayerSlot = slot; OriginalLordId = lordId; OriginalTeamId = teamId;
            OriginalGold = gold; OriginalAicGoldReserveSignal = goldReserveSignal;
            OriginalMarketBuildingObserved = marketBuildingObserved;
            OriginalTradepostPresent = originalTradepostPresent;
            OriginalAicSignalFingerprint = aicSignalsHash;
            _pendingOriginalPurchases = copy;
        }

        internal int PendingOriginalPurchase(ushort good)
        {
            int value;
            if (!_pendingOriginalPurchases.TryGetValue(good, out value))
                throw new InvalidOperationException("Unobserved original market pending amount");
            return value;
        }

        internal int NativeAicDemandSignal(ushort good)
        {
            int result;
            if (!_nativeAicDemandSignals.TryGetValue(good, out result))
                throw new InvalidOperationException("AIC demand signal not captured");
            return result;
        }

        internal CanonicalOriginalAiEconomy Copy()
        {
            return new CanonicalOriginalAiEconomy(PlayerSlot, OriginalLordId, OriginalTeamId,
                OriginalGold, OriginalAicGoldReserveSignal, OriginalAicSignalFingerprint,
                _pendingOriginalPurchases, _nativeAicDemandSignals,
                OriginalMarketBuildingObserved, OriginalTradepostPresent);
        }
    }
}
