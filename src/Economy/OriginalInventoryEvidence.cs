//  – actual SHCDESE 2.14.1 war/production observations and
// explicitly missing native per-good warehouse capacity evidence.
// Only safe read-side calls; game/Serps native hashes and write gates unchanged.
// Different from , this file never invents a capacity of quantity+200000.
using System;
using System.Collections.Generic;
using System.Linq;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.Interop.Enums;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalInventoryEvidence
    {
        // This is HOSTILE EXPOSURE, not a claim that an actual attack order
        // or active war event happened. Non-allied peers (even under truce)
        // require extra defensive reserve, never fewer goods.
        internal static bool HasLivingHostileExposure(int owner, IEnumerable<int> living, Func<int, int, bool> isAllied)
        {
            if (owner < 1 || owner > 8 || living == null || isAllied == null)
                throw new ArgumentException("Missing trusted player/alliance snapshot");
            // Validate the ENTIRE roster before inspecting relationships.
            // Otherwise an early hostile match could conceal an invalid slot
            // or a stale snapshot without the owner.
            int[] slots = living.Distinct().ToArray();
            if (slots.Any(x => x < 1 || x > 8) || !slots.Contains(owner))
                throw new ArgumentException("Invalid or stale native living roster");
            foreach (int other in slots)
            {
                if (other == owner)
                    continue;
                if (!isAllied(owner, other))
                    return true;
            }

            return false;
        }

        // A building supplies EVIDENCE OF POSSIBLE PRODUCTION, not a precise
        // output-per-day rate. The native production progress varies by
        // building, player economy settings, map and time.
        internal static int ObservedOutputForBuilding(eStructs kind)
        {
            switch (kind)
            {
                case eStructs.STRUCT_WOODCUTTERS_HUT:
                    return 2;
                case eStructs.STRUCT_QUARRY:
                    return 4;
                case eStructs.STRUCT_IRON_MINE:
                    return 6;
                case eStructs.STRUCT_PITCH_DIGGER:
                    return 7;
                case eStructs.STRUCT_WHEATFARM:
                    return 9;
                case eStructs.STRUCT_BAKERS_WORKSHOP:
                    return 10;
                case eStructs.STRUCT_CATTLEFARM:
                    return 11;
                case eStructs.STRUCT_HUNTERS_HUT:
                    return 12;
                case eStructs.STRUCT_APPLEFARM:
                    return 13;
                case eStructs.STRUCT_BREWERS_WORKSHOP:
                    return 14;
                case eStructs.STRUCT_MILL:
                    return 16;
                // Armoury workshops produce different goods depending on
                // their actual production quota: do not assume one output.
                default:
                    return 0;
            }
        }

        internal static long ProducerKey(int owner, ushort good)
        {
            return ((long)owner << 32) | good;
        }

        internal static int CountProductionSources(IEnumerable<Tuple<int, eStructs, bool>> buildings, int owner, ushort good)
        {
            if (buildings == null || owner < 1 || owner > 8)
                throw new ArgumentException("Untrusted building observation");
            int count = 0;
            foreach (var row in buildings)
            {
                if (row == null || row.Item1 < 0 || row.Item1 > 8)
                    throw new ArgumentException("Corrupt native producer record");
                if (row.Item1 == owner && row.Item3 && ObservedOutputForBuilding(row.Item2) == good)
                    count++;
            }

            return count;
        }

        // Rawra GameBuilding exposes the good currently being produced.
        // This is a SOURCE OBSERVATION, not a production-per-day forecast.
        internal static int ObservedDeclaredGood(eStructs kind, eGoods produced)
        {
            if (!IsProductionBuilding(kind))
                return 0;
            int id = (int)produced;
            if (id < 1 || id > 24 || GoodCatalog.Find((ushort)id) == null)
                return 0;
            return id;
        }

        private static bool IsProductionBuilding(eStructs kind)
        {
            switch (kind)
            {
                case eStructs.STRUCT_WOODCUTTERS_HUT:
                case eStructs.STRUCT_IRON_MINE:
                case eStructs.STRUCT_PITCH_DIGGER:
                case eStructs.STRUCT_HUNTERS_HUT:
                case eStructs.STRUCT_FLETCHERS_WORKSHOP:
                case eStructs.STRUCT_BLACKSMITHS_WORKSHOP:
                case eStructs.STRUCT_POLETURNERS_WORKSHOP:
                case eStructs.STRUCT_ARMOURERS_WORKSHOP:
                case eStructs.STRUCT_TANNERS_WORKSHOP:
                case eStructs.STRUCT_BAKERS_WORKSHOP:
                case eStructs.STRUCT_BREWERS_WORKSHOP:
                case eStructs.STRUCT_QUARRY:
                case eStructs.STRUCT_WHEATFARM:
                case eStructs.STRUCT_HOPSFARM:
                case eStructs.STRUCT_APPLEFARM:
                case eStructs.STRUCT_CATTLEFARM:
                case eStructs.STRUCT_MILL:
                    return true;
                default:
                    return false;
            }
        }

        // GameBuilding r_CurrentGoodStackAmount/r_MaxGoodStackAmount and
        // r_LocalStorageGoodType provide a candidate locally observed bound.
        // Only accept self-consistent snapshots that account for ALL of the
        // player's authoritative GetGoodAmount for that specific good.
        // This does NOT prove a shared warehouse capacity or a save-atomic
        // allocation and MUST NOT satisfy IOriginalWarehouseCapacityAttestation by itself.
        internal static bool TryCandidateLocalStackCapacity(int owner, ushort good, int nativeTotal, IEnumerable<Tuple<int, ushort, uint, uint, bool>> observedStacks, out int candidateCapacity)
        {
            candidateCapacity = 0;
            if (owner < 1 || owner > 8 || GoodCatalog.Find(good) == null || nativeTotal < 0 || observedStacks == null)
                return false;
            long current = 0, maximum = 0;
            int matching = 0;
            foreach (var stack in observedStacks)
            {
                if (stack == null || stack.Item1 < 1 || stack.Item1 > 8 || stack.Item3 > stack.Item4)
                    return false;
                if (stack.Item1 != owner || stack.Item2 != good || !stack.Item5)
                    continue;
                matching++;
                if (stack.Item4 == 0)
                    return false;
                current += (long)stack.Item3;
                maximum += (long)stack.Item4;
                if (current > int.MaxValue || maximum > int.MaxValue - 200000)
                    return false;
            }

            if (matching == 0 || current != nativeTotal || maximum < current)
                return false;
            candidateCapacity = (int)maximum;
            return true;
        }

        // A warehouse/supply-capacity contract must identify the actual
        // SIMULATION capacity, not a stockpile building count or a guessed
        // quantity. No adapter in installed ScriptExtender 2.14.1 was shown
        // to attest this per-player/per-good capacity.
        internal interface IOriginalWarehouseCapacityAttestation
        {
            bool TryReadWarehouseCapacity(int owner, ushort good, out int exactMax);
        }

        internal static bool TryNativeBoundStock(int owner, ushort good, int realQuantity, int conservativeReserve, int observedActiveProducerCount, IOriginalWarehouseCapacityAttestation provider, out StockCell stock)
        {
            stock = null;
            if (owner < 1 || owner > 8 || realQuantity < 0 || conservativeReserve < 0 || observedActiveProducerCount < 0 || GoodCatalog.Find(good) == null || provider == null)
                return false;
            int max;
            if (!provider.TryReadWarehouseCapacity(owner, good, out max))
                return false;
            // The original native add-preflight can report no eligible
            // storage. A typed, proven provider may attest an EXACT zero
            // ceiling for an empty and completely inert commodity; it must
            // never be replaced with a fictional positive capacity.
            if (max < 0 || max < realQuantity || max > int.MaxValue - 200000)
                return false;
            // a legitimately active producer can exist before a player
            // builds a warehouse. Production observation is diagnostic,
            // not stock nor warehouse capacity; do not freeze all trade.
            if (max == 0 && (realQuantity != 0 || conservativeReserve != 0))
                return false;
            stock = new StockCell
            {
                Quantity = realQuantity,
                Capacity = max,
                CoreReserve = conservativeReserve,
                DailyConsumption = 0,
                DailyProduction = 0,
                Buildings = 0,
                Military = 0 // observedActiveProducerCount is evidence only
            };
            stock.Validate();
            return true;
        }

        // Official Steam/game integration has not established a warehouse
        // capacity authority. Hard deny: a caller may not substitute a
        // fictitious 200,000 units of free storage.
        internal sealed class UnavailableOriginalWarehouseCapacity : IOriginalWarehouseCapacityAttestation
        {
            public bool TryReadWarehouseCapacity(int owner, ushort good, out int capacity)
            {
                capacity = 0;
                return false;
            }
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // No trusted warehouse capacity implementation has yet been proven
        // for rawra 2.14.1. Retain explicit null authority until reviewed.
        // exact native original capacity, only on our own pinned
        // isolated singleplayer Steam-build game copy. Not network-authorized.
        private readonly OriginalInventoryEvidence.IOriginalWarehouseCapacityAttestation _capacityAuthority = new OriginalSoloWarehouseCapacityReader();

#if DIPLOMACY_PRIVATE_TESTS
        // SHCDESE 2.14.x IsPlayerAlliedTo compares raw native team IDs,
        // including zero == zero. A native team-zero is UNASSIGNED, not a
        // positive alliance that can reduce protected military reserve.
        internal static bool ActualPositiveNativeTeamAlliance(
            int firstTeam, int secondTeam)
        {
            return firstTeam > 0 && secondTeam > 0 &&
                firstTeam == secondTeam;
        }
#endif
        private bool ActualHostileExposure(int player)
        {
            var api = GamePlayerManagerAPI.Instance;
            if (api == null || !api.IsPlayerIdValid(player))
                throw new InvalidOperationException("No verified live player");
            int[] alive = api.GetAlivePlayerIds();
#if DIPLOMACY_PRIVATE_TESTS
            return OriginalInventoryEvidence.HasLivingHostileExposure(
                player,alive,(self,other) =>
                {
                    int teamSelf=api.GetPlayerTeam(self);
                    int teamOther=api.GetPlayerTeam(other);
                    return ActualPositiveNativeTeamAlliance(
                        teamSelf,teamOther) &&
                        api.IsPlayerAlliedTo(self,other) &&
                        api.IsPlayerAlliedTo(other,self);
                });
#else
            return OriginalInventoryEvidence.HasLivingHostileExposure(player, alive, (self, other) => api.IsPlayerAlliedTo(self, other));
#endif
        }

        private List<Tuple<int, ushort, uint, uint, bool>> ObserveOriginalLocalGoodStacks()
        {
            // These are real rawra GameBuilding fields, NOT validated overall
            // warehouse capacity. Observe only under the native inventory lock.
            var api = GameBuildingManagerAPI.Instance;
            if (api == null)
                throw new InvalidOperationException("No building manager");
            var stacks = new List<Tuple<int, ushort, uint, uint, bool>>();
            var all = api.GetBuildingsAsSpan();
            for (int i = 0; i < all.Length; i++)
            {
                GameBuilding building = all[i];
                if (building.r_AliveState != AliveState.IsAlive)
                    continue;
                int owner = (int)building.r_PlayerIdOwner;
                int kind = (int)building.r_LocalStorageGoodType;
                if (kind < 1 || kind > 24 || GoodCatalog.Find((ushort)kind) == null)
                    continue;
                if (owner < 1 || owner > 8)
                    throw new InvalidOperationException("Local storage record has invalid owner");
                if (building.r_MaxGoodStackAmount == 0 && building.r_CurrentGoodStackAmount == 0)
                    continue;
                stacks.Add(Tuple.Create(owner, (ushort)kind, building.r_CurrentGoodStackAmount, building.r_MaxGoodStackAmount, true));
            }

            return stacks;
        }

        // Preserve the existing producer-read ABI used by legacy economy and
        // the separately signed MP roster adapter. Only the canonical reader
        // requests the additional original tradepost observation.
        private Dictionary<long, int> ObserveRealActiveGoodsProducers()
        {
            bool[] ignored;
            return ObserveRealActiveGoodsProducers(out ignored);
        }

        private Dictionary<long, int> ObserveRealActiveGoodsProducers(out bool[] originalTradepostBySlot)
        {
            // Called only under existing native inventory lease after a
            // successful legitimately attested game environment check.
            var api = GameBuildingManagerAPI.Instance;
            if (api == null)
                throw new InvalidOperationException("No building manager");
            var result = new Dictionary<long, int>();
            // One scan already needed for producer observations also captures
            // physical original tradeposts, without an extra per-frame pass.
            var originalMarket = new bool[9];
            var buildings = api.GetBuildingsAsSpan();
            for (int i = 0; i < buildings.Length; i++)
            {
                GameBuilding b = buildings[i];
                if (b.r_AliveState != AliveState.IsAlive)
                    continue;
                int owner = b.r_PlayerIdOwner;
                if (b.r_BuildingType == eStructs.STRUCT_TRADEPOST)
                {
                    // Unowned/inactive slots provide no market permission.
                    // Preserve prior producer-observation behavior for those
                    // rows; never assign such a market to a living player.
                    if (owner >= 1 && owner <= 8)
                        originalMarket[owner] = true;
                }
                if (b.r_ProductionCycleActive == 0 || owner < 1 || owner > 8)
                    continue;
                int good = OriginalInventoryEvidence.ObservedDeclaredGood(b.r_BuildingType, b.r_ProducedGoodId);
                if (good == 0)
                    continue;
                long key = OriginalInventoryEvidence.ProducerKey(owner, (ushort)good);
                int previous;
                result.TryGetValue(key, out previous);
                result[key] = checked(previous + 1);
            }

            originalTradepostBySlot = originalMarket;
            return result;
        }
    }
}
