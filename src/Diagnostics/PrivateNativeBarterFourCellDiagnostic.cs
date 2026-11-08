// Observation of an already verified native barter receipt.
// Called only after native four-cell settlement, managed journal publication,
// and the stable-save section have completed successfully.
// No game/native/Steam/SaveData reads or writes. No policy or release authority.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // A private opt-in; it does not share the singled-out QA expected ID.
        // The call site in CommitNativeTradePackage catches every diagnostic
        // exception so logger/format/allocation failure cannot enter rollback.
        private void TraceCommittedNativeBarterFourCells(ObservedNativeTrade receipt)
        {
            if (Environment.GetEnvironmentVariable("DIPLOMACY_NATIVE_TRADE_FOUR_CELL_PROBE") != "1")
                return;
            int processId;
            using (Process process = Process.GetCurrentProcess())
                processId = process.Id;
            string message;
            if (TryFormatCommittedNativeBarterFourCells(receipt, processId, _sessionGeneration, out message))
                Log("trade_four_cell", message);
        }

        // Pure formatter, explicitly fail-closed for incomplete four-cell data.
        // It reads only fields from the already captured, immutable receipt.
        // It does NOT validate a new native state or alter the committed trade.
        private static bool TryFormatCommittedNativeBarterFourCells(ObservedNativeTrade receipt, int processId, long currentGeneration, out string message)
        {
            message = null;
            if (receipt == null || receipt.Id == 0 || processId <= 0 || receipt.Players == null || receipt.Goods == null || receipt.Before == null || receipt.After == null || receipt.Players.Length != 4 || receipt.Goods.Length != 4 || receipt.Before.Length != 4 || receipt.After.Length != 4)
                return false;
            var b = new StringBuilder(512);
            b.Append("status=committed_native_barter_four_cells");
            b.Append("|origin=preexisting_observed_native_trade");
            b.Append("|id=").Append(receipt.Id.ToString(CultureInfo.InvariantCulture));
            b.Append("|pid=").Append(processId.ToString(CultureInfo.InvariantCulture));
            b.Append("|generation=").Append(currentGeneration.ToString(CultureInfo.InvariantCulture));
            b.Append("|receipt_generation=").Append(receipt.Generation.ToString(CultureInfo.InvariantCulture));
            b.Append("|receipt_tick=").Append(receipt.Tick.ToString(CultureInfo.InvariantCulture));
            b.Append("|cell_count=4");
            // All 16 numeric cell values are emitted with explicit, stable
            // keys. Nothing is emitted until all four array lengths agree.
            for (int i = 0; i < 4; i++)
            {
                string prefix = "|cell" + i.ToString(CultureInfo.InvariantCulture);
                b.Append(prefix).Append("_player=").Append(receipt.Players[i].ToString(CultureInfo.InvariantCulture));
                b.Append(prefix).Append("_good=").Append(receipt.Goods[i].ToString(CultureInfo.InvariantCulture));
                b.Append(prefix).Append("_before=").Append(receipt.Before[i].ToString(CultureInfo.InvariantCulture));
                b.Append(prefix).Append("_after=").Append(receipt.After[i].ToString(CultureInfo.InvariantCulture));
            }

            message = b.ToString();
            return true;
        }
    }
}
#endif
