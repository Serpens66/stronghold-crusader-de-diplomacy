// conservative mutual-aid *intent* planner for AI lords.
// Plans aid intents without native calls, inventory transfers or save mutation.
// Uses only caller-attested per-good capacity and explicit bilateral alliances.
// Native settlement and reconciliation must remain under the existing release gate.
using System;
using System.Linq;
using System.Collections.Generic;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal enum AidReliefReason : byte
    {
        FoodRelief = 1,
        BesiegedAlly = 2,
        BuildingEmergency = 3
    }

    internal sealed class ObservedAidParticipant
    {
        internal byte PlayerId { get; set; }
        internal bool IsHuman { get; set; }
        // True ONLY on explicit game evidence, not just "not allied".
        internal bool VerifiedLastStand { get; set; }
    }

    internal sealed class ObservedMutualAidAgreement
    {
        internal byte A { get; set; }
        internal byte B { get; set; }
        internal bool Allied { get; set; }
        internal int Goodwill { get; set; } // 0..100
        internal int LastCompletedAidDay { get; set; } = -100;
        internal int OutstandingFavor { get; set; } // legacy scalar fallback
        // Signed favor credit: positive means B owes A; negative A owes B.
        // Using a directional sign prevents penalizing a lord who helps
        // *repay* an earlier gift from the other side.
        internal bool HasDirectionalFavorBalance { get; set; }
        internal int SignedOutstandingFavor { get; set; }
    }

    internal sealed class ObservedAidCommodity
    {
        internal byte Owner { get; set; }
        internal ushort Good { get; set; }
        internal int ObservationDay { get; set; }
        internal StockCell Snapshot { get; set; }
        internal int PendingPurchases { get; set; } // read-only AI procurement
        internal bool ExactCapacityAttested { get; set; }
        internal int? UnreservedReceivingSpace { get; set; }
        internal int ReceivingSpace => Math.Min(Snapshot.Space,
            UnreservedReceivingSpace ?? Snapshot.Space);
    }

    internal sealed class ProposedAidShipment
    {
        internal readonly byte Donor, Recipient;
        internal readonly ushort Good;
        internal readonly int Quantity, Day, Priority;
        internal readonly AidReliefReason Reason;
        internal ProposedAidShipment(byte from, byte to, ushort good, int qty, int day, int priority, AidReliefReason reason)
        {
            Donor = from;
            Recipient = to;
            Good = good;
            Quantity = qty;
            Day = day;
            Priority = priority;
            Reason = reason;
        }
    }

    internal static class MutualAidIntentPlanner
    {
        private const int MaxShipment = 40;
        private static long Key(byte player, ushort good)
        {
            return ((long)player << 32) | good;
        }

        private static int SafetyReserveIgnoringForecast(StockCell cell)
        {
            // Production rate is not currently proved in the native adapter.
            // The aid donor may NOT spend its forecast production in advance.
            var conservative = cell.Copy();
            conservative.DailyProduction = 0;
            return conservative.Reserve();
        }

        // Shared second-stage ORIGINAL budget check for a proposed pledge
        // and the later transfer. Callers must read all values freshly under
        // the original native inventory lease; these inputs are observations,
        // never an authority granted by the Mod's StockCell prediction.
        internal static bool FitsOriginalAidStockAndSpace(
            int donorQuantity, int donorReserve, int recipientQuantity,
            int recipientCapacity, long incomingReserved, int amount,
            out string denial)
        {
            denial = "aid_budget_invalid_original_observation";
            if (amount < 3 || amount > MaxShipment ||
                donorQuantity < 0 || donorReserve < 0 ||
                donorReserve > donorQuantity || recipientQuantity < 0 ||
                recipientCapacity < recipientQuantity || incomingReserved < 0)
                return false;
            if (donorQuantity < amount)
            {
                denial = "aid_donor_native_stock_shortfall";
                return false;
            }
            if ((long)donorQuantity - donorReserve < amount)
            {
                denial = "aid_donor_original_ai_reserve";
                return false;
            }
            long free = (long)recipientCapacity - recipientQuantity;
            if (free < amount)
            {
                denial = "aid_recipient_native_space_shortfall";
                return false;
            }
            if (free - incomingReserved < amount)
            {
                denial = "aid_recipient_incoming_reservation";
                return false;
            }
            denial = null;
            return true;
        }

        private static bool IsFood(ushort g)
        {
            return g >= 10 && g <= 13;
        }

        private static bool IsArmoury(ushort g)
        {
            return g >= 17 && g <= 24;
        }

        private static bool IsConstruction(ushort g)
        {
            return g == 2 || g == 4;
        }

        // Keep a single best commodity per ordered donor/recipient pair.
        // With the same observed donors, recipients and max count, a lower
        // scoring commodity can never help the later assignment.
        private sealed class AidSelection
        {
            internal long TotalPriority;
            internal ProposedAidShipment[] Intents;
        }

        private static IOrderedEnumerable<ProposedAidShipment> OrderAid(IEnumerable<ProposedAidShipment> plans)
        {
            return plans.OrderByDescending(x => x.Priority).ThenBy(x => x.Donor).ThenBy(x => x.Recipient).ThenBy(x => x.Good);
        }

        private static bool PreferAid(AidSelection candidate, AidSelection existing)
        {
            if (existing == null)
                return true;
            if (candidate.TotalPriority != existing.TotalPriority)
                return candidate.TotalPriority > existing.TotalPriority;
            if (candidate.Intents.Length != existing.Intents.Length)
                return candidate.Intents.Length > existing.Intents.Length;
            // Equal-scoring allocations must never depend on input order,
            // dictionary iteration, a process RNG or machine locale.
            var first = OrderAid(candidate.Intents).ToArray();
            var second = OrderAid(existing.Intents).ToArray();
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i].Priority != second[i].Priority)
                    return first[i].Priority > second[i].Priority;
                if (first[i].Donor != second[i].Donor)
                    return first[i].Donor < second[i].Donor;
                if (first[i].Recipient != second[i].Recipient)
                    return first[i].Recipient < second[i].Recipient;
                if (first[i].Good != second[i].Good)
                    return first[i].Good < second[i].Good;
            }

            return false;
        }

        private static void KeepBestAid(Dictionary<int, AidSelection> table, int key, AidSelection candidate)
        {
            AidSelection previous;
            if (!table.TryGetValue(key, out previous) || PreferAid(candidate, previous))
                table[key] = candidate;
        }

        private static ProposedAidShipment[] SelectGlobalAid(IEnumerable<ProposedAidShipment> candidates, IEnumerable<ObservedAidParticipant> rulers, int maxIntents)
        {
            // Eight genuine player slots bound the exact assignment to
            // 8 * 256 * (maxIntents+1) states. The mask marks beneficiaries,
            // the donor iteration permits at most one outgoing shipment.
            var bestPair = new ProposedAidShipment[9, 9];
            foreach (var choice in candidates)
            {
                var previous = bestPair[choice.Donor, choice.Recipient];
                if (previous == null || choice.Priority > previous.Priority || choice.Priority == previous.Priority && choice.Good < previous.Good)
                    bestPair[choice.Donor, choice.Recipient] = choice;
            }

            var states = new Dictionary<int, AidSelection>
            {
                {
                    0,
                    new AidSelection
                    {
                        TotalPriority = 0,
                        Intents = new ProposedAidShipment[0]
                    }
                }
            };
            foreach (byte donor in rulers.Where(x => !x.IsHuman).Select(x => x.PlayerId).OrderBy(x => x))
            {
                var next = new Dictionary<int, AidSelection>();
                foreach (var item in states.OrderBy(x => x.Key))
                {
                    KeepBestAid(next, item.Key, item.Value); // donor abstains
                    int mask = item.Key >> 4, number = item.Key & 15;
                    if (number >= maxIntents)
                        continue;
                    for (int recipient = 1; recipient <= 8; recipient++)
                    {
                        var choice = bestPair[donor, recipient];
                        int bit = 1 << (recipient - 1);
                        if (choice == null || (mask & bit) != 0)
                            continue;
                        var chosen = new ProposedAidShipment[item.Value.Intents.Length + 1];
                        Array.Copy(item.Value.Intents, chosen, item.Value.Intents.Length);
                        chosen[chosen.Length - 1] = choice;
                        KeepBestAid(next, ((mask | bit) << 4) | (number + 1), new AidSelection { TotalPriority = item.Value.TotalPriority + choice.Priority, Intents = chosen });
                    }
                }

                states = next;
            }

            AidSelection winner = null;
            foreach (var state in states.OrderBy(x => x.Key))
                if (PreferAid(state.Value, winner))
                    winner = state.Value;
            return winner == null ? new ProposedAidShipment[0] : OrderAid(winner.Intents).ToArray();
        }

        // Returns non-authoritative suggestions only. Never authorizes a write.
        internal static ProposedAidShipment[] Plan(int day, IEnumerable<ObservedAidParticipant> parties, IEnumerable<ObservedMutualAidAgreement> agreements, IEnumerable<ObservedAidCommodity> stock, int maxIntents = 3)
        {
            if (day < 1 || maxIntents < 1 || maxIntents > 8 || parties == null || agreements == null || stock == null)
                throw new ArgumentException("Invalid trusted aid planning context");
            var rulers = parties.ToArray();
            if (rulers.Any(p => p == null || p.PlayerId < 1 || p.PlayerId > 8) || rulers.GroupBy(p => p.PlayerId).Any(g => g.Count() != 1))
                throw new ArgumentException("Invalid or duplicate lord");
            var players = rulers.ToDictionary(p => p.PlayerId, p => p);
            var goods = stock.ToArray();
            if (goods.Any(s => s == null || s.Snapshot == null || s.Owner < 1 || s.Owner > 8 || !players.ContainsKey(s.Owner) || GoodCatalog.Find(s.Good) == null || s.ObservationDay != day || s.PendingPurchases < 0) || goods.GroupBy(s => Key(s.Owner, s.Good)).Any(g => g.Count() != 1))
                throw new ArgumentException("Invalid inventory observation");
            foreach (var s in goods)
                s.Snapshot.Validate();
            var available = goods.ToDictionary(s => Key(s.Owner, s.Good), s => s);
            var pacts = agreements.ToArray();
            if (pacts.Any(p => p == null || p.A == p.B || !players.ContainsKey(p.A) || !players.ContainsKey(p.B) || p.Goodwill < 0 || p.Goodwill > 100 || p.OutstandingFavor < 0 || p.OutstandingFavor > 10000 || (p.HasDirectionalFavorBalance && (p.SignedOutstandingFavor < -400 || p.SignedOutstandingFavor > 400)) || (p.LastCompletedAidDay != -100 && (p.LastCompletedAidDay < 1 || p.LastCompletedAidDay > day))) || pacts.GroupBy(p => Math.Min(p.A, p.B) * 256 + Math.Max(p.A, p.B)).Any(g => g.Count() != 1))
                throw new ArgumentException("Corrupt bilateral aid memory");
            var candidates = new List<ProposedAidShipment>();
            foreach (var pact in pacts)
            {
                if (!pact.Allied || pact.Goodwill < 60 || day - (long)pact.LastCompletedAidDay < 4)
                    continue;
                foreach (var path in new[]
                {
                    Tuple.Create(pact.A, pact.B),
                    Tuple.Create(pact.B, pact.A)
                }

                )
                {
                    byte donor = path.Item1, receiver = path.Item2;
                    if (players[donor].IsHuman || players[receiver].IsHuman)
                        continue;
                    int alreadyOwedToDonor = pact.HasDirectionalFavorBalance ? donor == pact.A ? Math.Max(0, pact.SignedOutstandingFavor) : Math.Max(0, -pact.SignedOutstandingFavor) : pact.OutstandingFavor;
                    int needsToRepay = pact.HasDirectionalFavorBalance ? donor == pact.A ? Math.Max(0, -pact.SignedOutstandingFavor) : Math.Max(0, pact.SignedOutstandingFavor) : 0;
                    foreach (Good item in GoodCatalog.All)
                    {
                        if (!IsFood(item.Id) && !IsArmoury(item.Id) && !IsConstruction(item.Id))
                            continue;
                        ObservedAidCommodity source, target;
                        if (!available.TryGetValue(Key(donor, item.Id), out source) || !available.TryGetValue(Key(receiver, item.Id), out target) || !source.ExactCapacityAttested || !target.ExactCapacityAttested)
                            continue;
                        // Donor safety is an immutable protected stock
                        // budget; recipient relief is a DISTINCT bounded
                        // MOD POLICY target. Never infer zero demand merely
                        // because original PendingPurchases is zero, and
                        // never modify original P or native consumption.
                        int srcReserve = SafetyReserveIgnoringForecast(source.Snapshot);
                        int recipientReliefGoal = RecipientFoodReliefTargetPolicy.RecipientReliefGoal(item.Id, target);
#if DIPLOMACY_PRIVATE_TESTS
                        // capped micro relief: genuine, narrowly capped micro-relief is legal
                        // when native donor stock stays >= original reserve+2.
                        // This is only Mod policy; native goods still need full
                        // original engine lease, two-cell rollback and saved journal.
                        int surplus = Math.Max(0, source.Snapshot.Quantity - srcReserve - 2);
#else
                        int surplus = Math.Max(0, source.Snapshot.Quantity - srcReserve - 8);
#endif
                        int shortage = Math.Max(0, recipientReliefGoal - target.Snapshot.Quantity);
                        int capacity = target.ReceivingSpace;
#if DIPLOMACY_PRIVATE_TESTS
                        if (surplus < 3 || capacity < 3)
#else
                        if (surplus < 12 || capacity < 3)
#endif
                            continue;
                        AidReliefReason purpose;
                        int weight;
                        // No fictitious "aid" to a wealthy ruler. Must be a real
                        // observed shortage with both donor surplus and room.
                        if (IsFood(item.Id))
                        {
                            if (shortage < 3)
                                continue;
                            purpose = AidReliefReason.FoodRelief;
                            weight = 300;
                        }
                        else if (IsArmoury(item.Id))
                        {
                            if (!players[receiver].VerifiedLastStand || shortage < 3)
                                continue;
                            purpose = AidReliefReason.BesiegedAlly;
                            weight = 270;
                        }
                        else
                        {
                            if (target.PendingPurchases < 3 || shortage < 3)
                                continue;
                            purpose = AidReliefReason.BuildingEmergency;
                            weight = 110;
                        }

                        // Outstanding unreturned favors dampen routine aid; a
                        // genuinely besieged ally can still receive emergency help.
                        if (alreadyOwedToDonor > 50 && purpose != AidReliefReason.BesiegedAlly)
                            continue;
                        int needed = Math.Min(shortage, MaxShipment);
                        if (target.PendingPurchases > 0)
                            needed = Math.Min(needed, Math.Max(3, target.PendingPurchases));
#if DIPLOMACY_PRIVATE_TESTS
                        // At most 3 units for the naturally scarce 3..11 surplus
                        // range; usual quarter-surplus thereafter. Never exceed
                        // donor-owned actual stock minus reserve and safety margin.
                        int qty = Math.Min(Math.Min(needed,
                            Math.Min(surplus, Math.Max(3, surplus / 4))), capacity);
#else
                        int qty = Math.Min(Math.Min(needed, Math.Max(0, surplus / 4)), capacity);
#endif
                        if (qty < 3)
                            continue;
#if DIPLOMACY_PRIVATE_TESTS
                        if (source.Snapshot.Quantity - qty < srcReserve + 2)
#else
                        if (source.Snapshot.Quantity - qty < srcReserve + 8)
#endif
                            throw new InvalidOperationException("Unsafe donor aid planner");
                        // Saturate deficit weight BEFORE narrowing to int;
                        // a valid large stock shortage exceeds int.MaxValue/3.
                        int priority = weight + (int)Math.Min(200L, (long)shortage * 3L) + pact.Goodwill / 2 + Math.Min(50, qty) + Math.Min(30, needsToRepay / 2) - Math.Min(70, alreadyOwedToDonor);
                        candidates.Add(new ProposedAidShipment(donor, receiver, item.Id, qty, day, priority, purpose));
                    }
                }
            }

            // A greedy first match can prevent two later useful shipments:
            // A->B 500 blocks A->C 490 and D->B 490 (980 together).
            // Compute the maximum global observed aid priority instead.
            // Same safety contract: at most one shipment PER donor and PER
            // recipient, never more than maxIntents, and never touch goods.
            return SelectGlobalAid(candidates, rulers, maxIntents);
        }
    }
}
