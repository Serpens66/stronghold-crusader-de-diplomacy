// solo preflight: inert, offline compiled prerequisite policy, NOT original-game write authority.
// This file is intentionally absent from the 176 selected V5 sources.
// Future integration must establish every fact from live trusted engine/Save callbacks.
#if DIPLOMACY_PRIVATE_TESTS
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class SoloFacts
    {
        // Each bit is a required independent live observation, NOT a user/config setting.
        internal bool ExactOriginalExeAndLoadedNativeFile;
        internal bool LoadedNativeRuntimeBytesVerified;
        internal bool ManagedGameAndRawraMethodPinsVerified;
        internal bool CorrectManagedApiDependenciesLoaded;
        internal bool UniqueOriginalEngineMonitorOwned;
        internal bool VerifiedGameThread;
        internal bool ExactlyOneLocalHuman;
        internal bool NoOtherHumanOrRemoteSteamPeer;
        internal bool AtLeastOneLiveAiOpponent;
        internal bool LiveRosterAndNativePlayerIdsAgree;
        internal bool ActiveMapGenerationUnchanged;
        internal bool SaveBelongsToSameMapAndRoster;
        internal bool AllThreeOwnedSaveHooksInstalled;
        internal bool SameGenerationOriginalSaveReceiptObserved;
        internal bool SaveIsNotSuppressedOrFailed;
        internal bool NoUnattestedPreparedOrReplayableTrade;
        internal bool NoOtherPendingNativeMutation;
        internal bool OriginalAtomicBoundaryIdleAndNotQuarantined;
        internal bool AllFourNativeCellsAndCapacitiesRecheckedUnderLease;
        internal bool OriginalMarketTermsAndTradePairRechecked;
        internal bool HumanConsentIsOriginalProductionDecision;
        internal bool TradeIdNotPreviouslyCommittedOrReserved;
        // The original offline engine can report networked=true. Do not use this
        // bit as a proxy for other human players.
        internal bool NetworkTransportReported;
        internal long SessionGeneration, OriginalSaveGeneration;
        internal int LocalHumanSlot, RequesterSlot, RecipientSlot;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount;
        internal ulong TradeId;
        internal string MapSha = string.Empty, RosterSha = string.Empty, OriginalSaveSha = string.Empty;
        internal int[] Before = Array.Empty<int>(), Capacities = Array.Empty<int>();
        internal SoloFacts Clone() => (SoloFacts)MemberwiseClone();
    }

    // Pure, nonauthoritative decision. No native reader, API, save hook,
    // loaded game DLL, game installation, writer, filesystem, or network calls.
    internal static class SoloPreflight
    {
        private static bool Deny(out string denial, string message)
        { denial = message; return false; }

        private static bool Sha(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9')))
                    return false;
            return true;
        }

        internal static bool CheckPrerequisitesOnly(SoloFacts x, out string denial)
        {
            denial = "missing_untrusted_input";
            if (x == null) return false;
            // All 22 bits must originate in separately verified live authorities.
            if (!x.ExactOriginalExeAndLoadedNativeFile) return Deny(out denial, "native_process_provenance");
            if (!x.LoadedNativeRuntimeBytesVerified) return Deny(out denial, "runtime_native_image");
            if (!x.ManagedGameAndRawraMethodPinsVerified) return Deny(out denial, "managed_runtime_abi");
            if (!x.CorrectManagedApiDependenciesLoaded) return Deny(out denial, "api_dependencies");
            if (!x.UniqueOriginalEngineMonitorOwned) return Deny(out denial, "original_engine_lease");
            if (!x.VerifiedGameThread) return Deny(out denial, "game_thread");
            if (!x.ExactlyOneLocalHuman) return Deny(out denial, "single_human");
            if (!x.NoOtherHumanOrRemoteSteamPeer) return Deny(out denial, "remote_peer");
            if (!x.AtLeastOneLiveAiOpponent) return Deny(out denial, "ai_opponent");
            if (!x.LiveRosterAndNativePlayerIdsAgree) return Deny(out denial, "native_roster");
            if (!x.ActiveMapGenerationUnchanged) return Deny(out denial, "map_generation");
            if (!x.SaveBelongsToSameMapAndRoster) return Deny(out denial, "save_owner");
            if (!x.AllThreeOwnedSaveHooksInstalled) return Deny(out denial, "save_hooks");
            if (!x.SameGenerationOriginalSaveReceiptObserved) return Deny(out denial, "save_receipt");
            if (!x.SaveIsNotSuppressedOrFailed) return Deny(out denial, "save_failure");
            if (!x.NoUnattestedPreparedOrReplayableTrade) return Deny(out denial, "prepared_replay");
            if (!x.NoOtherPendingNativeMutation) return Deny(out denial, "native_pending");
            if (!x.OriginalAtomicBoundaryIdleAndNotQuarantined) return Deny(out denial, "atomic_boundary");
            if (!x.AllFourNativeCellsAndCapacitiesRecheckedUnderLease) return Deny(out denial, "native_four_cells");
            if (!x.OriginalMarketTermsAndTradePairRechecked) return Deny(out denial, "market_or_pair");
            if (!x.HumanConsentIsOriginalProductionDecision) return Deny(out denial, "human_consent");
            if (!x.TradeIdNotPreviouslyCommittedOrReserved) return Deny(out denial, "duplicate_quote");
            if (x.SessionGeneration <= 0 || x.OriginalSaveGeneration != x.SessionGeneration)
                return Deny(out denial, "save_generation_mismatch");
            if (!Sha(x.MapSha) || !Sha(x.RosterSha) || !Sha(x.OriginalSaveSha))
                return Deny(out denial, "digest_not_verified");
            if (x.LocalHumanSlot < 1 || x.LocalHumanSlot > 8 ||
                x.RequesterSlot < 1 || x.RequesterSlot > 8 ||
                x.RecipientSlot < 1 || x.RecipientSlot > 8 ||
                x.RequesterSlot == x.RecipientSlot ||
                (x.LocalHumanSlot != x.RequesterSlot && x.LocalHumanSlot != x.RecipientSlot))
                return Deny(out denial, "not_local_human_trade");
            if (x.TradeId < 1001 || x.OfferedGood == 0 || x.RequestedGood == 0 ||
                x.OfferedGood == x.RequestedGood ||
                x.OfferedAmount < 1 || x.RequestedAmount < 1 ||
                x.OfferedAmount > 100000 || x.RequestedAmount > 100000)
                return Deny(out denial, "invalid_quote_terms");
            if (x.Before == null || x.Capacities == null ||
                x.Before.Length != 4 || x.Capacities.Length != 4)
                return Deny(out denial, "missing_four_cells");
            for (int i = 0; i < 4; i++)
                if (x.Before[i] < 0 || x.Capacities[i] < x.Before[i] ||
                    x.Capacities[i] > int.MaxValue - 200000)
                    return Deny(out denial, "invalid_native_cell_" + i);
            if (x.Before[0] < x.OfferedAmount ||
                x.Before[2] < x.RequestedAmount ||
                (long)x.Before[1] + x.OfferedAmount > x.Capacities[1] ||
                (long)x.Before[3] + x.RequestedAmount > x.Capacities[3])
                return Deny(out denial, "stocks_or_capacity_changed");
            denial = "prerequisites_satisfied_not_a_native_write_grant";
            return true;
        }

        // Always false: only a future reviewed runtime implementation may
        // grant an exclusive, one-shot native commit under the original monitor.
        internal static bool AuthorizeRealGoodsMutation(SoloFacts ignored) => false;
    }
}
#endif
