// original save evidence - Original SaveData CALLBACK readback is a narrow, map- and
// generation-scoped READ-ONLY witness, never a goods/MP/Steam authority.
// This pure classifier accepts only facts captured from original game hooks;
// user-supplied or synthetic objects by themselves have no authority.
using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalSoloSaveFacts
    {
        internal bool OriginalCallbackArchiveExact;
        internal bool OriginalSaveHooksOwned;
        internal bool ActiveCurrentMap;
        internal bool NoKnownExplicitMpState;
        internal bool NoSaveOrTradeOrRecoveryInProgress;
        internal long CurrentGeneration;
        internal long ReadbackGeneration;
        internal string CurrentMap;
        internal string ReadbackMap;
        internal string CurrentRoster;
        internal string ReadbackRoster;
        internal string ReadbackOriginalPath;
    }

    internal static class OriginalSoloSaveEvidence
    {
        // A successful classification proves ONLY that a read-only snapshot
        // belongs to this current original map/roster and same save generation.
        internal static bool TryAcceptReadOnly(OriginalSoloSaveFacts f,
            out string reason)
        {
            reason = "original_save_readback_facts_absent";
            if (f == null) return false;
            if (!f.OriginalCallbackArchiveExact)
            { reason = "original_callback_and_archive_bytes_not_proven"; return false; }
            if (!f.OriginalSaveHooksOwned)
            { reason = "original_save_hooks_not_owned"; return false; }
            if (!f.ActiveCurrentMap)
            { reason = "original_active_map_missing"; return false; }
            if (!f.NoKnownExplicitMpState)
            { reason = "original_game_may_have_remote_human_or_mp_mode"; return false; }
            if (!f.NoSaveOrTradeOrRecoveryInProgress)
            { reason = "save_trade_or_recovery_pending"; return false; }
            if (f.CurrentGeneration <= 0 ||
                f.CurrentGeneration != f.ReadbackGeneration)
            { reason = "original_save_generation_missing_or_stale"; return false; }
            if (String.IsNullOrWhiteSpace(f.CurrentMap) ||
                String.IsNullOrWhiteSpace(f.CurrentRoster) ||
                !String.Equals(f.CurrentMap, f.ReadbackMap, StringComparison.Ordinal) ||
                !String.Equals(f.CurrentRoster, f.ReadbackRoster,
                               StringComparison.Ordinal))
            { reason = "original_save_map_or_roster_mismatch"; return false; }
            try
            {
                if (String.IsNullOrWhiteSpace(f.ReadbackOriginalPath) ||
                    !Path.IsPathRooted(f.ReadbackOriginalPath) ||
                    f.ReadbackOriginalPath.IndexOf('\0') >= 0 ||
                    !String.Equals(Path.GetExtension(f.ReadbackOriginalPath),
                                   ".sav", StringComparison.OrdinalIgnoreCase))
                { reason = "original_game_save_destination_not_pinned"; return false; }
            }
            catch (Exception)
            { reason = "original_game_save_destination_invalid"; return false; }

            reason = "current_original_save_readback_only_not_native_trade_grant";
            return true;
        }

        // A valid pure test input or a genuine original save readback can
        // NEVER directly admit native goods or promote a Steam transaction.
        internal static bool AuthorizeNativeGoodsCommit(
            OriginalSoloSaveFacts ignored) { return false; }
        internal static bool AuthorizeMultiplayer(
            OriginalSoloSaveFacts ignored) { return false; }
    }
}
