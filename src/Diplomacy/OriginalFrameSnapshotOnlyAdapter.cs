//  coordinator-merge CANDIDATE. No new EngineInterface hook.
// Restricts the already owned ICanonicalTradeEngineFrame to a SNAPSHOT-only
// interface. NativePort and Durability are intentionally inaccessible from
// the canonical MP reducer path. The SOLO path remains unchanged.
using System;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalFrameSnapshotOnlyAdapter
    {
        private sealed class SnapshotOnly : IObservedOriginalConsentFrame
        {
            private readonly ICanonicalTradeEngineFrame owner;
            private readonly TrustedContext context;
            private readonly ulong[] steamBySlot;
            private readonly byte localOriginalSlot;
            internal SnapshotOnly(ICanonicalTradeEngineFrame existingOwner, TrustedContext originalExistingContext, ulong[] currentOriginalSteamBySlot, byte originalNativeLocalHumanSlot)
            {
                owner = existingOwner;
                context = originalExistingContext;
                steamBySlot = (ulong[])currentOriginalSteamBySlot.Clone();
                localOriginalSlot = originalNativeLocalHumanSlot;
            }

            public bool HoldsOriginalEngineMonitor
            {
                get
                {
                    return owner.OwnsOriginalEngineMonitor;
                }
            }

            public bool VerifiedPostNativeOrderedChoreBoundary
            {
                get
                {
                    return owner.OrderedChoreBoundaryVerified;
                }
            }

            public long OriginalMapGeneration
            {
                get
                {
                    return owner.OriginalMapGeneration;
                }
            }

            public TrustedContext PinnedOriginalNetworkContext
            {
                get
                {
                    return context;
                }
            }

            public ulong[] CurrentOriginalSteamBySlot
            {
                get
                {
                    return (ulong[])steamBySlot.Clone();
                }
            }

            public byte CurrentOriginalLocalHumanSlot
            {
                get
                {
                    return localOriginalSlot;
                }
            }

            public long OriginalSharedSimulationTick
            {
                get
                {
                    return owner.SharedSimulationTick;
                }
            }

            public CanonicalTradeSnapshot Capture(long tick)
            {
                return owner.Capture(tick);
            }
        }

        internal static PendingMpReduction PrepareMpConsentOnly(CanonicalTradeReducer existingCanonicalOwner, AuthenticatedTradeTick alreadyVerifiedOrderedInput, ICanonicalTradeEngineFrame existingOriginalEngineOwner, TrustedContext coordinatorExistingContext, ulong[] freshlyVerifiedOriginalSteamBySlot, byte originalNativeLocalHumanSlot)
        {
            if (existingCanonicalOwner == null || alreadyVerifiedOrderedInput == null || existingOriginalEngineOwner == null || coordinatorExistingContext == null || freshlyVerifiedOriginalSteamBySlot == null || freshlyVerifiedOriginalSteamBySlot.Length != 9 || originalNativeLocalHumanSlot < 1 || originalNativeLocalHumanSlot > 8)
                throw new ArgumentNullException(" existing coordinator original engine frame");
            // NEVER call CanonicalGlobalTradeReducerAdapter.
            // NEVER call CanonicalTradeRuntimeSettlement.
            return existingCanonicalOwner.PrepareVerifiedMpWithoutPublishing(alreadyVerifiedOrderedInput, new SnapshotOnly(existingOriginalEngineOwner, coordinatorExistingContext, freshlyVerifiedOriginalSteamBySlot, originalNativeLocalHumanSlot));
        }
    }
}
