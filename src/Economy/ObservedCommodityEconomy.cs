//  economic model: derive conservative *observed net drawdown* from
// actual game inventories, not fabricated production/consumption rates.
// The forecast is isolated by map lifecycle and never grants native writes.
using System;
using System.Collections.Generic;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ObservedCommodityEconomy
    {
        private sealed class Evidence
        {
            internal int Day, Quantity, LastNetPerDay, Samples;
            internal long KnownExternalAdjustments;
        }

        private readonly Dictionary<long, Evidence> _observed = new Dictionary<long, Evidence>();
        private const int MaximumEntries = 8 * 19;
        private const int MaximumRate = 100000;
        private static long Key(int player, int good)
        {
            if (player < 1 || player > 8 || good < 1 || good > 24)
                throw new ArgumentOutOfRangeException("player/good");
            return ((long)player << 32) | (uint)good;
        }

        internal void Reset()
        {
            _observed.Clear();
        }

        private Evidence Get(int player, int good, int day, int quantity)
        {
            if (day < 1 || quantity < 0)
                throw new InvalidOperationException("Invalid original inventory observation");
            long key = Key(player, good);
            Evidence value;
            if (!_observed.TryGetValue(key, out value))
            {
                if (_observed.Count >= MaximumEntries)
                    throw new InvalidOperationException("Observed economy ledger exceeds its bounded roster");
                value = new Evidence
                {
                    Day = day,
                    Quantity = quantity
                };
                _observed.Add(key, value);
            }

            return value;
        }

        // Call only when an already permitted game-ledger transfer has finished.
        // A deliberate trade is not evidence of permanent native production.
        internal void NoteExternalTransfer(int player, int good, int delta)
        {
            Evidence existing;
            if (!_observed.TryGetValue(Key(player, good), out existing))
                return;
            existing.KnownExternalAdjustments += delta;
            if (existing.KnownExternalAdjustments > int.MaxValue || existing.KnownExternalAdjustments < int.MinValue)
                throw new InvalidOperationException("External stock accounting overflow");
        }

        internal void Observe(int player, ushort good, int day, int quantity, StockCell cell)
        {
            if (cell == null || cell.Quantity != quantity)
                throw new InvalidOperationException("Exact native stock snapshot required");
            if (cell.Capacity == 0)
                return; // No warehouse; no fictitious consumption.
            Evidence value = Get(player, good, day, quantity);
            if (day < value.Day)
            {
                // New or rewound game: never transfer forecast from another epoch.
                value.Day = day;
                value.Quantity = quantity;
                value.Samples = 0;
                value.LastNetPerDay = 0;
                value.KnownExternalAdjustments = 0;
                return;
            }

            if (day > value.Day)
            {
                int elapsed = day - value.Day;
                if (elapsed <= 0 || elapsed > 7)
                {
                    value.LastNetPerDay = 0;
                    value.Samples = 0;
                }
                else
                {
                    long difference = (long)quantity - value.Quantity - value.KnownExternalAdjustments;
                    long daily = difference / elapsed;
                    daily = Math.Max(-MaximumRate, Math.Min(MaximumRate, daily));
                    int measured = (int)daily;
                    // Three later samples dampen the enormous influence of a single
                    // one-off construction or automatic market purchase. All integer
                    // operations and observation order are deterministic.
                    int weight = Math.Min(3, value.Samples);
                    value.LastNetPerDay = (int)(((long)value.LastNetPerDay * weight + measured) / (weight + 1));
                    value.Samples = Math.Min(3, value.Samples + 1);
                }

                value.Day = day;
                value.Quantity = quantity;
                value.KnownExternalAdjustments = 0;
            }

            // Net gain MAY be a purchase or gift, not proven production. Never
            // invent production to allow AI to spend goods prematurely.
            int measuredDrain = Math.Max(0, -value.LastNetPerDay);
            cell.DailyProduction = 0;
            cell.DailyConsumption = Math.Max(cell.DailyConsumption, measuredDrain);
            cell.Validate();
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private readonly ObservedCommodityEconomy _economy = new ObservedCommodityEconomy();
        private void RecordCommittedTransfer(NativeTransferProbe probe)
        {
            if (probe == null || probe.RollbackPending)
                return;
            int sender = ProbeSender(probe);
            _economy.NoteExternalTransfer(sender, probe.GoodId, -probe.Amount);
            _economy.NoteExternalTransfer(probe.TargetPlayer, probe.GoodId, probe.Amount);
        }

        private void RecordCommittedBarter(TradeRecord trade)
        {
            if (trade == null)
                return;
            // Exact completed goods lines are booked by ExecuteNativeBarter.
            // Keep known active-pair stock deltas out of net-consumption estimates.
            _economy.NoteExternalTransfer(trade.Requester, trade.OfferedGood, -trade.OfferedAmount);
            _economy.NoteExternalTransfer(trade.Recipient, trade.OfferedGood, trade.OfferedAmount);
            _economy.NoteExternalTransfer(trade.Recipient, trade.RequestedGood, -trade.RequestedAmount);
            _economy.NoteExternalTransfer(trade.Requester, trade.RequestedGood, trade.RequestedAmount);
        }
    }
}
