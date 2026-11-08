// Pure engine-owner adapter contract for the ONE canonical global trade
// reducer. This file registers NO hooks/packets, owns NO game resources,
// and never bypasses the original engine/save authority.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal interface ICanonicalTradeEngineFrame : ICanonicalTradeSnapshotSource, ICanonicalTradeNativePort, ICanonicalTradeDurability
    {
        // The coordinator acquires the ORIGINAL EngineInterface.threadLock
        // before invoking a frame, and holds it across the entire due
        // reduction, native four-cell commit, managed SaveData journal and
        // final readback; not a private imitation lock.
        bool OwnsOriginalEngineMonitor { get; }

        // This is the original shared game simulation tick, identical for
        // each peer. Neither local frames nor local wall-clock qualify.
        long SharedSimulationTick { get; }

        // The order/activation Chore has been verified at the post-native
        // managed engine lane; no signed parser runs inside a native callback.
        bool OrderedChoreBoundaryVerified { get; }

        // Epoch of the actual map and original player slot registry.
        long OriginalMapGeneration { get; }
    }

    internal static class CanonicalGlobalTradeReducerAdapter
    {
        // Called ONLY by the coordinator's own post-ReceiveChore managed
        // callback while it holds its original native engine monitor.
        // Never invoke from raw Steam, UI, render Tick, or a native detour.
        internal static byte[] AdvanceOneVerifiedGameTick(CanonicalTradeReducer controller, AuthenticatedTradeTick signedInput, ICanonicalTradeEngineFrame originalFrame)
        {
            if (controller == null || signedInput == null || originalFrame == null)
                throw new ArgumentNullException("canonical frame");
            if (!originalFrame.OwnsOriginalEngineMonitor || !originalFrame.OrderedChoreBoundaryVerified || originalFrame.SharedSimulationTick < 1 || originalFrame.OriginalMapGeneration < 1)
                throw new InvalidOperationException("Canonical reduction requires one original engine/chore owner");
            // The exact ICanonicalTradeSnapshotSource includes:
            //   all live slot identities and human/AI classification,
            //   verified Steam IDs and map generation,
            //   all 19 goods for every participant and actual capacity,
            //   exact current buy/sell prices,
            //   directed pairs with actual trade authorization,
            //   one shared tick and original map/roster hashes.
            // One reduction may produce ZERO or ONE 4-cell transaction.
            PreparedCanonicalTransition prepared = controller.AdvanceSharedTick(originalFrame.SharedSimulationTick, signedInput, originalFrame);
            if (prepared != null)
                CanonicalTradeRuntimeSettlement.ApplyUnderOriginalEngineMonitor(controller, prepared, originalFrame.SharedSimulationTick, originalFrame, originalFrame);
            // Returning a byte[] is not permission to create a second
            // SaveData handler. The coordinator alone embeds this exact
            // canonical payload into its existing schema18 GameSave state.
            return controller.SaveBytes();
        }
    }
}
