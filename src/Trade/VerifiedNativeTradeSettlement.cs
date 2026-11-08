using System;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal interface ICanonicalTradeDurability
    {
        // Called under the original engine monitor and stable-save section.
        // The parent implements engine-native + managed save durability.
        void PersistPreparedBeforeNative(byte[] beforeState);
        void PublishCompletedCanonicalState(byte[] completedState);
    }

    internal static class CanonicalTradeRuntimeSettlement
    {
        // Caller already holds the ORIGINAL engine monitor. Do not replace it
        // with a new private lock. No shared tick or save may interleave here.
        internal static void ApplyUnderOriginalEngineMonitor(CanonicalTradeReducer controller, PreparedCanonicalTransition transition, long sharedTick, ICanonicalTradeNativePort native, ICanonicalTradeDurability durability)
        {
            if (controller == null || transition == null || native == null || durability == null)
                throw new ArgumentNullException();
            ExactTradeCell[] cells = transition.Cells();
            if (cells.Length != 4)
                throw new InvalidOperationException("Expected exactly four cells");
            // Allocate payload copies before any native write.
            byte[] before = transition.BeforeState();
            byte[] success = transition.AfterState();
            byte[] failure = transition.FailureState();
            byte[] quarantine = transition.QuarantineState();
            // Failure here performs no native writes. The parent may retry
            // durability or abandon this runtime session; the controller remains
            // prepared and cannot advance or accept a second preparation.
            durability.PersistPreparedBeforeNative(before);
            bool[] attemptedWrite = new bool[cells.Length];
            try
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    ExactTradeCell cell = cells[i];
                    StockCell observed = native.Read(cell.Player, cell.Good);
                    if (!SameCell(observed, cell.BeforeCell()))
                        throw new InvalidOperationException("Initial native cell changed");
                }

                for (int i = 0; i < cells.Length; i++)
                {
                    ExactTradeCell cell = cells[i];
                    // Mark before calling: a throwing native call may have applied.
                    attemptedWrite[i] = true;
                    native.WriteAbsolute(cell.Player, cell.Good, cell.AfterQuantity);
                }

                // CommitPrepared independently reads back all four quantities.
                controller.CommitPrepared(transition, sharedTick, native);
            }
            catch
            {
                bool ambiguous = false;
                {
                    // Each cell is distinct. Compensate only attempted cells
                    // still holding our exact result, including a throwing call.
                    for (int i = cells.Length - 1; i >= 0; i--)
                    {
                        if (!attemptedWrite[i])
                            continue;
                        try
                        {
                            StockCell observed = native.Read(cells[i].Player, cells[i].Good);
                            if (observed == null)
                            {
                                ambiguous = true;
                                continue;
                            }

                            observed.Validate();
                            if (observed.Quantity == cells[i].BeforeQuantity)
                                continue;
                            if (observed.Quantity != cells[i].AfterQuantity)
                            {
                                ambiguous = true;
                                continue;
                            }

                            // Only an exact owned after-value may be compensated.
                            // Unattempted or foreign cells remain untouched and
                            // force the existing canonical quarantine on readback.
                            native.WriteAbsolute(cells[i].Player, cells[i].Good, cells[i].BeforeQuantity);
                        }
                        catch
                        {
                            ambiguous = true;
                        }
                    }
                }

                controller.RejectPrepared(transition, sharedTick, native, ambiguous);
                // Native rollback uncertainty remains sticky in canonical state.
                durability.PublishCompletedCanonicalState(controller.Quarantined ? quarantine : failure);
                return;
            }

            // A durability exception here must stop the parent runtime from
            // continuing. Do not rollback an already published native commit.
            // Recovery from the durable PreparingNative state quarantines.
            durability.PublishCompletedCanonicalState(success);
        }

        private static bool SameCell(StockCell a, StockCell b)
        {
            if (a == null || b == null)
                return false;
            a.Validate();
            b.Validate();
            return a.Quantity == b.Quantity && a.Capacity == b.Capacity && a.CoreReserve == b.CoreReserve && a.DailyConsumption == b.DailyConsumption && a.DailyProduction == b.DailyProduction && a.Buildings == b.Buildings && a.Military == b.Military;
        }
    }
}
