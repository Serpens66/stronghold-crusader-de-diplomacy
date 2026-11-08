using System;
using System.Linq;
using HarmonyLib;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private long _coherentSaveGeneration = -1;
        private ObservedNativeTrade _observedNativeTrade;
        private sealed class ObservedNativeTrade
        {
            internal readonly ulong Id;
            internal readonly long Generation, Tick;
            internal readonly int[] Players, Goods, Before, After;
            internal ObservedNativeTrade(ulong id, long generation, long tick, int[] players, int[] goods, int[] before, int[] after)
            {
                Id = id;
                Generation = generation;
                Tick = tick;
                Players = (int[])players.Clone();
                Goods = (int[])goods.Clone();
                Before = (int[])before.Clone();
                After = (int[])after.Clone();
            }
        }

        private ObservedNativeTrade CaptureCompletedNativeTrade(DiplomacyPendingPackage p)
        {
            RequireNativeDiplomacySection();
            if (p == null || p.Lines == null || p.Lines.Count != 2 || p.TradeRecordId == 0)
                throw new InvalidOperationException("No completed native trade package");
            var a = p.Lines[0];
            var b = p.Lines[1];
            int[] players =
            {
                a.SenderPlayer,
                a.TargetPlayer,
                b.SenderPlayer,
                b.TargetPlayer
            };
            int[] goods =
            {
                a.GoodId,
                a.GoodId,
                b.GoodId,
                b.GoodId
            };
            int[] before =
            {
                a.SenderBefore,
                a.RecipientBefore,
                b.SenderBefore,
                b.RecipientBefore
            };
            int[] after =
            {
                checked(a.SenderBefore - a.Amount),
                checked(a.RecipientBefore + a.Amount),
                checked(b.SenderBefore - b.Amount),
                checked(b.RecipientBefore + b.Amount)
            };
            for (int i = 0; i < 4; i++)
                if (GoodAmount(players[i], goods[i]) != after[i])
                    throw new InvalidOperationException("Actual native four-cell proof changed");
            return new ObservedNativeTrade(p.TradeRecordId, _sessionGeneration, ReadSimulationTick(), players, goods, before, after);
        }

        private bool ObservedPrivateNativeAuthority()
        {
#if DIPLOMACY_PRIVATE_TESTS
            string why;
            if (!DisposableGameScope.IsEnabled(out why) || !_saveBridgeInstalled || !_saveHandlerRegistered || _coherentSaveGeneration != _sessionGeneration || _saveTarget == null || _harmony == null || _saveSuppressed || _saveFailurePending != null || _unattestedSavedNativeTrade || !OriginalSoloWarehouseCapacityReader.IsSoloHumanHostSkirmish())
                return false;
            // A previous successful callback is insufficient if another mod removed
            // the actual managed save guard. Require all three still-owned patches.
            var patches = Harmony.GetPatchInfo(_saveTarget);
            if (patches == null || !patches.Prefixes.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == "SavePrefix") || !patches.Postfixes.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == "SavePostfix") || !patches.Finalizers.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == "SaveFinalizer"))
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            using (lease)
            {
                if (_atomicBoundary == null || _atomicBoundary.Quarantined || _atomicBoundary.SaveActive || _atomicBoundary.MutationActive)
                    return false;
                return _capacityAuthority is OriginalSoloWarehouseCapacityReader && OriginalSoloWarehouseCapacityReader.ProvenanceReady(out why);
            }
#else
            return false;
#endif
        }
    }
}
