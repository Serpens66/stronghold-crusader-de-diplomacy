// Sparse, private-only diagnostic of ALREADY captured ORIGINAL planner inputs.
// The caller supplies the exact AidTerms and per-player/per-good StockCell
// observations it ALREADY read under the original engine monitor.
// ZERO additional GamePlayerManagerAPI, GameAIManagerAPI, GoodAmount,
// original engine, native warehouse, game save, packet or Steam calls.
// This cannot mint a pledge, change goodwill, or authorize native inventory.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal static class ObservedAidEligibilityDiagnostic
    {
        private const int MinimumGoodwillObservedInPlanner = 60;
        private const int OriginalAidCooldownDays = 4;
        private const int MaximumRoutineOwedToDonor = 50;
        private const int MinimumDonorSurplus = 3;
        private const int MinimumRecipientShortageOrSpace = 3;
        private const int DonorSafetyBuffer = 2;
        private const int MaximumShipment = 40;
        // One bounded line per captured mutual-AI pact on the actual
        // original planning day. A row is a *diagnostic projection*,
        // never a statement that the original Gift journal has committed.
        internal static bool TryDescribe(int day, ObservedMutualAidAgreement capturedPact, bool recordedTradeGoodwillUsed, IEnumerable<ObservedAidCommodity> capturedCells, out string description)
        {
            description = null;
            if (day < 1 || capturedPact == null || capturedCells == null || capturedPact.A < 1 || capturedPact.B > 8 || capturedPact.A >= capturedPact.B)
                return false;
            try
            {
                var lookup = capturedCells.Where(x => x != null && (x.Owner == capturedPact.A || x.Owner == capturedPact.B) && x.Good >= 10 && x.Good <= 13).ToDictionary(x => ((int)x.Owner << 8) | x.Good);
                var s = new StringBuilder(1400);
                s.Append("scope=captured_mod_planner_inputs_only");
                Number(s, "day", day);
                Number(s, "a", capturedPact.A);
                Number(s, "b", capturedPact.B);
                s.Append("|real_mutual_pair_prevalidated=").Append(capturedPact.Allied ? "true" : "false");
                s.Append("|goodwill_source=").Append(recordedTradeGoodwillUsed ? "archived_trade_relationship" : "mutual_alliance_fallback");
                Number(s, "goodwill", capturedPact.Goodwill);
                Number(s, "last_completed_original_aid_day", capturedPact.LastCompletedAidDay);
                Number(s, "signed_favor_balance", capturedPact.SignedOutstandingFavor);
                s.Append("|directional_favor_attested=").Append(capturedPact.HasDirectionalFavorBalance ? "true" : "false");
                Number(s, "outstanding_favor_fallback", capturedPact.OutstandingFavor);
                s.Append("|mod_policy=").Append(RecipientFoodReliefTargetPolicy.PolicyId);
                s.Append("|goodwill_gate=").Append(capturedPact.Goodwill >= MinimumGoodwillObservedInPlanner ? "pass" : "blocked");
                s.Append("|cooldown_gate=").Append(day - (long)capturedPact.LastCompletedAidDay >= OriginalAidCooldownDays ? "pass" : "blocked");
                // Both directions are independent: B repaying A is not
                // penalized by A's previous genuine gift. No ledger writes.
                for (int route = 0; route < 2; route++)
                {
                    int donor = route == 0 ? capturedPact.A : capturedPact.B;
                    int receiver = route == 0 ? capturedPact.B : capturedPact.A;
                    int owed = capturedPact.HasDirectionalFavorBalance ? (donor == capturedPact.A ? Math.Max(0, capturedPact.SignedOutstandingFavor) : Math.Max(0, -capturedPact.SignedOutstandingFavor)) : capturedPact.OutstandingFavor;
                    s.Append("|direction=").Append(donor).Append(">").Append(receiver);
                    Number(s, "owed_to_donor", owed);
                    s.Append("|favor_gate=").Append(owed <= MaximumRoutineOwedToDonor ? "pass" : "blocked");
                    for (ushort good = 10; good <= 13; good++)
                    {
                        s.Append("|food=").Append(good).Append(":");
                        ObservedAidCommodity from, to;
                        if (!lookup.TryGetValue((donor << 8) | good, out from) || !lookup.TryGetValue((receiver << 8) | good, out to) || from.Snapshot == null || to.Snapshot == null)
                        {
                            s.Append("missing_captured_original_cell");
                            continue;
                        }

                        if (!from.ExactCapacityAttested || !to.ExactCapacityAttested)
                        {
                            s.Append("unattested_capacity");
                            continue;
                        }

                        from.Snapshot.Validate();
                        to.Snapshot.Validate();
                        // Same conservative donor reserve calculation as
                        // MutualAidIntentPlanner. Ignore forecast production
                        // ONLY on a copy, never on original stocks.
                        StockCell conservative = from.Snapshot.Copy();
                        conservative.DailyProduction = 0;
                        int protectedReserve = conservative.Reserve();
                        int availableSurplus = Math.Max(0, from.Snapshot.Quantity - protectedReserve - DonorSafetyBuffer);
                        int goal = RecipientFoodReliefTargetPolicy.RecipientReliefGoal(good, to);
                        int need = Math.Max(0, goal - to.Snapshot.Quantity);
                        int free = to.ReceivingSpace;
                        int shipment = Math.Min(Math.Min(need, MaximumShipment), Math.Min(Math.Min(availableSurplus, Math.Max(3, availableSurplus / 4)), free));
                        if (to.PendingPurchases > 0)
                            shipment = Math.Min(shipment, Math.Max(3, to.PendingPurchases));
                        bool eligible = capturedPact.Allied && capturedPact.Goodwill >= MinimumGoodwillObservedInPlanner && day - (long)capturedPact.LastCompletedAidDay >= OriginalAidCooldownDays && owed <= MaximumRoutineOwedToDonor && availableSurplus >= MinimumDonorSurplus && need >= MinimumRecipientShortageOrSpace && free >= MinimumRecipientShortageOrSpace && shipment >= MinimumRecipientShortageOrSpace;
                        s.Append("donorQ=").Append(from.Snapshot.Quantity).Append(",donorC=").Append(from.Snapshot.Capacity).Append(",donorP=").Append(from.PendingPurchases).Append(",donorBuildings=").Append(from.Snapshot.Buildings).Append(",donorProtectedReserve=").Append(protectedReserve).Append(",donorSurplus=").Append(availableSurplus).Append(",recipientQ=").Append(to.Snapshot.Quantity).Append(",recipientC=").Append(to.Snapshot.Capacity).Append(",recipientP=").Append(to.PendingPurchases).Append(",recipientBuildings=").Append(to.Snapshot.Buildings).Append(",recipientGoal=").Append(goal).Append(",recipientNeed=").Append(need).Append(",recipientSpace=").Append(free).Append(",maxHypotheticalIntent=").Append(shipment).Append(",foodGates=").Append(eligible ? "pass" : "blocked");
                    }
                }

                // A planning-day-only opt-in line per actual mutual
                // relationship; no potentially unbounded per-frame logging.
                description = s.ToString();
                return true;
            }
            catch (Exception)
            {
                // Diagnostics are strictly optional. An unavailable
                // summary must never change the normal planner outcome.
                description = null;
                return false;
            }
        }

        private static void Number(StringBuilder b, string key, int value)
        {
            b.Append('|').Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
#endif
