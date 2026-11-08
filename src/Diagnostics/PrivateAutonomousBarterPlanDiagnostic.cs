// PRIVATE read-only snapshot observation AFTER the one real planner call.
// No original game/native/Steam/SaveData reads, writes, authority or re-planning.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal static class PrivateAutonomousBarterPlanDiagnostic
    {
        private const int MaximumRowsPerMapGeneration = 192;
        private const int MaximumCharactersPerRow = 8192;
        private const int ExpectedGoods = 19;
        private const string OptIn = "DIPLOMACY_PRIVATE_AI_BARTER_PLAN_TRACE";

        private static readonly object BudgetGate = new object();
        private static long _budgetGeneration = long.MinValue;
        private static int _lastLoggedDay = -1;
        private static int _loggedRows;

        // The diagnostic may be missing, malformed, out of budget or throw
        // at any point. A committed model, mailbox or plan is NEVER affected.
        internal static void ObserveAfterOriginalPlan(
            CanonicalModel model, CanonicalTradeSnapshot snapshot,
            ManagedGoodsSnapshotPort detachedMemory,
            AutonomousTradeProposal[] returnedPlans)
        {
            try
            {
                if (Environment.GetEnvironmentVariable(OptIn) != "1" ||
                    Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" ||
                    Environment.GetEnvironmentVariable("DIPLOMACY_AI_MILITARY_TEST") != "1")
                    return;
                string scopeReason;
                if (!DisposableGameScope.IsEnabled(out scopeReason) ||
                    snapshot == null || snapshot.MapGeneration < 0)
                    return;

                long generation = snapshot.MapGeneration;
                int day = snapshot.Day;
                lock (BudgetGate)
                {
                    ResetBudgetIfNewGeneration(generation);
                    if (day <= _lastLoggedDay ||
                        _loggedRows >= MaximumRowsPerMapGeneration)
                        return;
                }

                string description;
                if (!TryDescribe(model, snapshot, detachedMemory, returnedPlans,
                                 out description))
                    return;

                lock (BudgetGate)
                {
                    ResetBudgetIfNewGeneration(generation);
                    if (day <= _lastLoggedDay ||
                        _loggedRows >= MaximumRowsPerMapGeneration)
                        return;
                    _lastLoggedDay = day;
                    _loggedRows++;
                }

                // The private headless driver receipt is sealed with SHA256.
                // This bounded row contains numeric data only.
                HeadlessQaDriver.Note(description);
            }
            catch (Exception)
            {
                // Best effort: no exception can reach reducer, enqueue or rollback.
            }
        }

        private static void ResetBudgetIfNewGeneration(long generation)
        {
            if (generation == _budgetGeneration)
                return;
            _budgetGeneration = generation;
            _lastLoggedDay = -1;
            _loggedRows = 0;
        }

        // Pure detached-data formatter; no planner call or native interaction.
        // Failure means no line at all, never an incomplete 19-good picture.
        internal static bool TryDescribe(
            CanonicalModel model, CanonicalTradeSnapshot snapshot,
            ManagedGoodsSnapshotPort detachedMemory,
            AutonomousTradeProposal[] returnedPlans, out string description)
        {
            description = null;
            if (model == null || model.Archive == null || snapshot == null ||
                detachedMemory == null || returnedPlans == null ||
                snapshot.Day < 1 || snapshot.SharedTick < 0 ||
                snapshot.MapGeneration < 0)
                return false;

            CanonicalPlayer[] players = snapshot.Players();
            CanonicalPlayer[] humans = players.Where(x => x.Human).ToArray();
            CanonicalPlayer[] ai = players.Where(x => !x.Human)
                                         .OrderBy(x => x.Slot).ToArray();
            if (players.Length != 3 || humans.Length != 1 || !humans[0].Alive ||
                ai.Length != 2 || !ai[0].Alive || !ai[1].Alive ||
                returnedPlans.Length > 1 || GoodCatalog.All.Count != ExpectedGoods)
                return false;

            byte a = ai[0].Slot;
            byte b = ai[1].Slot;
            TradeRelationship relation = model.Archive.Relationships.FirstOrDefault(
                x => TradeHistoryArchive.PairKey(x.A, x.B) ==
                     TradeHistoryArchive.PairKey(a, b));
            if (relation == null)
                return false;

            Good[] prices = snapshot.Prices();
            Ruler[] rulers = snapshot.Rulers(false);
            if (prices.Length != ExpectedGoods)
                return false;
            Ruler ra = rulers.SingleOrDefault(x => x.PlayerId == a);
            Ruler rb = rulers.SingleOrDefault(x => x.PlayerId == b);
            if (ra == null || rb == null)
                return false;

            var sb = new StringBuilder(5000);
            sb.Append("status=autonomous_ai_barter_original_inputs");
            Field(sb, "v", 1);
            Field(sb, "generation", snapshot.MapGeneration);
            Field(sb, "tick", snapshot.SharedTick);
            Field(sb, "day", snapshot.Day);
            Field(sb, "human", humans[0].Slot);
            Field(sb, "ai_a", a);
            Field(sb, "ai_b", b);
            Field(sb, "plan_count", returnedPlans.Length);
            Field(sb, "goods", ExpectedGoods);
            Field(sb, "cells", ExpectedGoods * 2);
            Field(sb, "relation_goodwill", relation.Goodwill);
            Field(sb, "relation_last_attempt", relation.LastAttemptDay);
            Field(sb, "relation_last_deal", relation.LastDealDay);
            Field(sb, "relation_accepted_trades", relation.AcceptedTrades);
            Field(sb, "relation_rejected_streak", relation.RejectionStreak);
            Field(sb, "relation_last_rejected", relation.LastRejectedDay);
            Field(sb, "relation_last_penalty", relation.LastPenaltyDay);
            Field(sb, "relation_rejected_today", relation.RejectedToday);
            Field(sb, "archive_last_autonomous_day",
                  model.Archive.LastAutonomousDay);

            // Purely reproduce original daily roll from existing detached
            // scope/term/seed. Log numeric 0..9999 ONLY, never any key.
            Field(sb, "derived_original_day_roll",
                  ComputeDaySelection(model, snapshot.Day, a, b));

            RulerRow(sb, ra);
            RulerRow(sb, rb);
            if (!PairRow(sb, snapshot, a, b) ||
                !PairRow(sb, snapshot, b, a))
                return false;

            for (int i = 0; i < ExpectedGoods; i++)
            {
                Good expected = GoodCatalog.All[i];
                Good observed = prices[i];
                if (observed == null || observed.Id != expected.Id)
                    return false;
                sb.Append("|price_").Append(expected.Id).Append('=');
                Decimal(sb, observed.MarketSell);
                sb.Append(',');
                Decimal(sb, observed.MarketBuy);
            }

            // Parity between the detached planner port and its original
            // snapshot. Missing or changed fields suppress the ENTIRE line.
            foreach (byte slot in new[] { a, b })
                foreach (Good good in GoodCatalog.All)
                {
                    StockCell source = snapshot.Read(slot, good.Id);
                    StockCell detached = detachedMemory.Read(slot, good.Id);
                    if (source == null || detached == null ||
                        !SameOriginalCell(source, detached))
                        return false;
                    StockRow(sb, slot, good.Id, source);
                    if (sb.Length > MaximumCharactersPerRow)
                        return false;
                }

            if (returnedPlans.Length == 1)
            {
                AutonomousTradeProposal plan = returnedPlans[0];
                if (plan == null ||
                    !(plan.Requester == a && plan.Recipient == b ||
                      plan.Requester == b && plan.Recipient == a))
                    return false;
                sb.Append("|plan=");
                sb.Append(plan.Requester).Append(',');
                sb.Append(plan.Recipient).Append(',');
                sb.Append(plan.OfferedGood).Append(',');
                sb.Append(plan.RequestedGood).Append(',');
                sb.Append(plan.RequestedAmount).Append(',');
                sb.Append((int)plan.Generosity);
            }

            if (sb.Length > MaximumCharactersPerRow)
                return false;
            description = sb.ToString();
            return true;
        }

        private static bool SameOriginalCell(StockCell a, StockCell b)
        {
            return a.Quantity == b.Quantity && a.Capacity == b.Capacity &&
                   a.CoreReserve == b.CoreReserve &&
                   a.DailyConsumption == b.DailyConsumption &&
                   a.DailyProduction == b.DailyProduction &&
                   a.Buildings == b.Buildings && a.Military == b.Military;
        }

        private static void StockRow(StringBuilder sb, byte player, ushort good,
                                     StockCell stock)
        {
            sb.Append("|stock_").Append(player).Append('_').Append(good).Append('=');
            sb.Append(stock.Quantity).Append(',');
            sb.Append(stock.Capacity).Append(',');
            sb.Append(stock.CoreReserve).Append(',');
            sb.Append(stock.DailyConsumption).Append(',');
            sb.Append(stock.DailyProduction).Append(',');
            sb.Append(stock.Buildings).Append(',');
            sb.Append(stock.Military);
        }

        private static void RulerRow(StringBuilder sb, Ruler ruler)
        {
            sb.Append("|ruler_").Append(ruler.PlayerId).Append('=');
            sb.Append(ruler.Trust).Append(',');
            sb.Append(ruler.Greed).Append(',');
            sb.Append(ruler.Caution).Append(',');
            sb.Append(ruler.Commerce).Append(',');
            sb.Append(ruler.AtWar ? 1 : 0);
            // Ruler.Name is deliberately never accessed or serialized.
        }

        private static bool PairRow(StringBuilder sb,
                                    CanonicalTradeSnapshot snapshot,
                                    byte from, byte to)
        {
            ObservedTradePairState state;
            bool ab, ba, tradePact;
            if (!snapshot.TryCopyPrivateRawPair(from, to, out state,
                                                out ab, out ba, out tradePact) ||
                state == null || state.Requester != from ||
                state.Recipient != to || state.Day != snapshot.Day)
                return false;

            sb.Append("|pair_").Append(from).Append('_').Append(to).Append('=');
            Bit(sb, state.Authoritative); sb.Append(',');
            Bit(sb, state.RequesterAlive); sb.Append(',');
            Bit(sb, state.RecipientAlive); sb.Append(',');
            Bit(sb, state.Hostile); sb.Append(',');
            Bit(sb, state.HasTradeRelation); sb.Append(',');
            Bit(sb, ab); sb.Append(',');
            Bit(sb, ba); sb.Append(',');
            Bit(sb, tradePact); sb.Append(',');
            sb.Append(state.Goodwill).Append(',');
            sb.Append(state.Trust).Append(',');
            sb.Append(state.Grudge).Append(',');
            sb.Append(state.Threat).Append(',');
            sb.Append(state.Vassal).Append(',');
            sb.Append(state.Overlord);
            return true;
        }

        private static void Bit(StringBuilder sb, bool value)
        {
            sb.Append(value ? '1' : '0');
        }
        private static void Decimal(StringBuilder sb, decimal value)
        {
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Field(StringBuilder sb, string key, long value)
        {
            sb.Append('|').Append(key).Append('=')
              .Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static int ComputeDaySelection(CanonicalModel model, int day, byte a, byte b)
        {
            string material = model.MatchScope.ToString("N") + "|" +
                model.MatchTerm.ToString(CultureInfo.InvariantCulture) + "|" +
                model.WorldSeed.ToString(CultureInfo.InvariantCulture) + "|" +
                day.ToString(CultureInfo.InvariantCulture) + "|" +
                Math.Min(a, b).ToString(CultureInfo.InvariantCulture) + "|" +
                Math.Max(a, b).ToString(CultureInfo.InvariantCulture) +
                "|natural_trading";
            using (var hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(material));
                uint word = (uint)bytes[0] | ((uint)bytes[1] << 8) |
                            ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
                return (int)(word % 10000);
            }
        }
    }
}
#endif
