// trade readiness: real original-game gross inventory READ ONLY assessment.
// Reports precisely what the original warehouse currently contains.
// It does NOT confuse gross stock with (reserve-adjusted) tradable supply,
// original AIC demand, exact native warehouse capacity or normal goods rights.
// Any such facts require separate same-tick original-engine provenance.
// This code NEVER accesses native memory, goods setters, SaveData or network.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal enum BarterPreviewState
    {
        OriginalScopeUnavailable = 0,
        InvalidObservedTerms = 1,
        OriginalHumanRawStockInsufficient = 2,
        OriginalAiRawStockInsufficient = 3,
        OriginalGrossStocksOnly = 4,
        AttestedHumanSurplusInsufficient = 5,
        AttestedAiSurplusInsufficient = 6,
        AttestedHumanStorageInsufficient = 7,
        AttestedAiStorageInsufficient = 8,
        OriginalAiDemandNotConfirmed = 9,
        ReadOnlyConditionsMetButNoNativePermission = 10
    }

    internal sealed class OriginalBarterFacts
    {
        // A current, unchanged original-game tick under actual engine monitor.
        internal bool NativeReadSectionHeld { get; set; }
        internal int HumanCurrentRawStock { get; set; }
        internal int AiCurrentRawStock { get; set; }
        internal int HumanGives { get; set; }
        internal int HumanReceives { get; set; }
        // The following facts MUST come from original exact capacity and
        // reserve readers in this SAME native owner/tick, never a guess
        // based on raw quantity or the existence of a BepInEx plugin.
        internal bool ExactOriginalStockAndStorageAttested { get; set; }
        internal int HumanSurplus { get; set; }
        internal int AiSurplus { get; set; }
        internal int HumanReceiveSpace { get; set; }
        internal int AiReceiveSpace { get; set; }
        internal bool ExactOriginalAiNeedAttested { get; set; }
        internal bool ExactOriginalAiNeedMeetsQuote { get; set; }
    }

    internal static class ReadOnlyTradeReadiness
    {
        // One channel is NOT a consent token or authority. This class never
        // returns true/permission and is safe to run in the normal Release.
        internal static BarterPreviewState Assess(OriginalBarterFacts f)
        {
            if (f == null || !f.NativeReadSectionHeld)
                return BarterPreviewState.OriginalScopeUnavailable;
            if (f.HumanCurrentRawStock < 0 || f.AiCurrentRawStock < 0 ||
                f.HumanGives < 1 || f.HumanReceives < 1 ||
                f.HumanGives > 100000 || f.HumanReceives > 100000 ||
                f.HumanCurrentRawStock > int.MaxValue-100000 ||
                f.AiCurrentRawStock > int.MaxValue-100000)
                return BarterPreviewState.InvalidObservedTerms;
            if (f.HumanCurrentRawStock < f.HumanGives)
                return BarterPreviewState.OriginalHumanRawStockInsufficient;
            if (f.AiCurrentRawStock < f.HumanReceives)
                return BarterPreviewState.OriginalAiRawStockInsufficient;
            if (!f.ExactOriginalStockAndStorageAttested)
                return BarterPreviewState.OriginalGrossStocksOnly;
            if (f.HumanSurplus < 0 || f.AiSurplus < 0 ||
                f.HumanReceiveSpace < 0 || f.AiReceiveSpace < 0 ||
                f.HumanSurplus > f.HumanCurrentRawStock ||
                f.AiSurplus > f.AiCurrentRawStock ||
                f.HumanReceiveSpace > 1000000000 ||
                f.AiReceiveSpace > 1000000000)
                return BarterPreviewState.InvalidObservedTerms;
            if (f.HumanSurplus < f.HumanGives)
                return BarterPreviewState.AttestedHumanSurplusInsufficient;
            if (f.AiSurplus < f.HumanReceives)
                return BarterPreviewState.AttestedAiSurplusInsufficient;
            if (f.HumanReceiveSpace < f.HumanReceives)
                return BarterPreviewState.AttestedHumanStorageInsufficient;
            if (f.AiReceiveSpace < f.HumanGives)
                return BarterPreviewState.AttestedAiStorageInsufficient;
            if (!f.ExactOriginalAiNeedAttested || !f.ExactOriginalAiNeedMeetsQuote)
                return BarterPreviewState.OriginalAiDemandNotConfirmed;
            return BarterPreviewState.ReadOnlyConditionsMetButNoNativePermission;
        }

        // Even fully attested positive prerequisites do not unlock a
        // normal Native goods, SaveData or Steam/MP transaction.
        internal static bool AuthorizeNativeGoodsCommit(OriginalBarterFacts ignored)
        {
            return false;
        }

        internal static bool AuthorizeMultiplayer(OriginalBarterFacts ignored)
        {
            return false;
        }
    }
}
