// Private, bounded, strictly original AI-to-AI Gift warm test.
// No game start, role/team editing, fabricated native authority or stock setup.
// The original selected-save loader and sole saved archive owner remain unchanged.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using System.Diagnostics;
using CrusaderDE;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    internal static partial class HeadlessQaDriver
    {
        private static bool _originalAiWarmInputLogged;
        private static float SealStarted;

        // original ally aid scenario: observe a REAL current original-game pair, never invent
        // a team, set native stocks, or treat a pending intent as delivery.
        // Every index must be a valid original game slot before consulting
        // the original 9x9 relation matrix.
        private static bool TryObserveOriginalMutualAiPair(
            MilitaryQaWorldSnapshot s, out int first, out int second,
            out string reason)
        {
            first = second = 0;
            reason = "actual_original_single_human_and_two_ai_not_observed";
            if (s == null || s.Local < 1 || s.Local > 8 ||
                s.Humans == null || s.Humans.Length != 1 ||
                s.Humans[0] != s.Local ||
                s.Ai == null || s.Ai.Length < 2 || s.Ai.Length > 7)
                return false;

            reason = "original_native_ai_slots_not_unique_or_attested";
            var ai = s.Ai.OrderBy(x => x).ToArray();
            for (int i = 0; i < ai.Length; i++)
                if (ai[i] < 1 || ai[i] > 8 || ai[i] == s.Local ||
                    (i > 0 && ai[i] == ai[i - 1]))
                    return false;

            reason = "original_ai_alliance_matrix_or_native_team_unavailable";
            if (s.Alliances == null || s.Alliances.GetLength(0) < 9 ||
                s.Alliances.GetLength(1) < 9 ||
                s.OriginalNativeTeams == null || s.OriginalNativeTeams.Length != 9)
                return false;

            for (int i = 0; i < ai.Length; i++)
                for (int j = i + 1; j < ai.Length; j++)
                {
                    // Do not use saved treaty text, player team predictions or
                    // a mod-owned convenience alliance flag for Native aid.
                    int a = ai[i], b = ai[j];
                    if (s.OriginalNativeTeams[a] <= 0 ||
                        s.OriginalNativeTeams[b] != s.OriginalNativeTeams[a] ||
                        !s.Alliances[a, b] || !s.Alliances[b, a])
                        continue;
                    first = a;
                    second = b;
                    reason = "exact_original_mutual_ai_pair_observed_no_gift_claimed";
                    return true;
                }

            reason = "actual_mutually_allied_AI_pair_absent_no_role_rewrite";
            return false;
        }


        private static void TickOriginalAidWarm(MilitaryQaWorldSnapshot s)
        {
            if (s == null)
                return;
            if (stage == 30)
            {
                if (!_originalAiWarmInputLogged)
                {
                    _originalAiWarmInputLogged = true;
                    bool humansKnown = s.Humans != null && s.Humans.Length >= 1;
                    bool aiKnown = s.Ai != null && s.Ai.Length >= 2;
                    bool alliancesKnown = s.Alliances != null &&
                        s.Alliances.GetLength(0) > 3 && s.Alliances.GetLength(1) > 3;
                    Note("status=original_ai_warm_input_observed" +
                         "|local=" + s.Local + "|generation=" + s.Generation +
                         "|humans=" + (humansKnown ? string.Join(",", s.Humans) : "unknown") +
                         "|ai=" + (aiKnown ? string.Join(",", s.Ai) : "unknown") +
                         "|alliance_2_to_3=" + (alliancesKnown ? s.Alliances[2, 3].ToString() : "unknown") +
                         "|alliance_3_to_2=" + (alliancesKnown ? s.Alliances[3, 2].ToString() : "unknown") +
                         "|alliances_known=" + alliancesKnown +
                         "|original_native_team_2=" +
                         (s.OriginalNativeTeams != null && s.OriginalNativeTeams.Length > 2
                            ? s.OriginalNativeTeams[2].ToString() : "unavailable") +
                         "|original_native_team_3=" +
                         (s.OriginalNativeTeams != null && s.OriginalNativeTeams.Length > 3
                            ? s.OriginalNativeTeams[3].ToString() : "unavailable") +
                         "|known=" + (humansKnown && aiKnown && alliancesKnown));
                }

                // Observe the exact already-loaded original save. The new
                // warm scenario MUST NOT use the earlier military QA
                // RestartSkirmishGame team-selection mutator.
                int originalAiA, originalAiB;
                string originalPairReason;
                if (!TryObserveOriginalMutualAiPair(
                        s, out originalAiA, out originalAiB,
                        out originalPairReason))
                {
                    StopAiMilitaryQa(false, originalPairReason);
                    return;
                }
                Note("status=original_ai_native_pair_preflight" +
                     "|a=" + originalAiA + "|b=" + originalAiB +
                     "|source=actual_original_game_relations" +
                     "|native_gift_written=false");

                if (s.Aid != null)
                {
                    StopAiMilitaryQa(false,
                        "unexpected_unbound_gift_before_this_warm_trial");
                    return;
                }

                AidWarmGeneration = s.Generation;
                AidWarmStartTick = s.Tick;
                AidWarmOriginalDay = s.Day;
                if (!Save("BeforeOriginalAiAid.sav"))
                    return;
                stage = 33;
                Note("status=original_ai_warm_baseline_save_completed" +
                     "|generation=" + s.Generation + "|day=" + s.Day +
                     "|original_existing_ai_pair=true|synthetic_stock_calls=0" +
                     "|goodwill_changes=0|new_participants=0");
                return;
            }

            if (stage == 33)
            {
                if (s.Generation != AidWarmGeneration)
                {
                    StopAiMilitaryQa(false,
                        "original_generation_changed_during_warm_baseline_save");
                    return;
                }

                // The existing Save() owns the native+Director pause. Its
                // bookkeeping MUST be released before any native simulation
                // restarts, or the following Save() can appear paused while
                // its native tick is actually running.
                if (savePauseOwned)
                {
                    FinishSave();
                    if (savePauseOwned || savePauseRestorePending)
                        return;
                }

                bool previous;
                if (!DiplomacyFeature.SetSoloTestPaused(false, out previous))
                {
                    AidLifecycleWait("original_ai_warm_resume_wait",
                        "strict_original_native_pause_not_yet_released", s);
                    return;
                }

                if (Director.instance == null)
                {
                    AidLifecycleWait("original_ai_warm_resume_wait",
                        "original_director_unavailable", s);
                    return;
                }

                Director.instance.SetPausedState(false);
                AidWarmStartTick = s.Tick;
                AidWarmOriginalDay = s.Day;
                AidWarmStarted = Time.realtimeSinceStartup;
                NextAidWaitNote = 0f;
                Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "1");
                stage = 31;
                Note("status=original_ai_warm_native_unpaused" +
                     "|generation=" + AidWarmGeneration + "|day=" + s.Day +
                     "|original_tick=" + s.Tick +
                     "|gift_observed=false|original_alliance_and_stocks_unchanged=true");
                return;
            }
            if (stage == 31)
            {
                if (s.Generation != AidWarmGeneration ||
                    s.Day < AidWarmOriginalDay)
                {
                    StopAiMilitaryQa(false,
                        "original_ai_warm_world_or_day_changed_before_gift");
                    return;
                }

                if (s.Aid == null)
                {
                    AidLifecycleWait("original_ai_warm_waiting",
                        "real_native_gift_and_committed_journal_not_yet_observed", s);
                    return;
                }

                // A current-process observation is NOT sufficient by itself:
                // exactly two original writer callbacks and the complete
                // matching Delivered+Favor ledger are independently needed.
                string reason;
                if (!ObservedCurrentNativeAiGift(s, out reason))
                {
                    StopAiMilitaryQa(false,
                        "original_gift_or_ledger_contradiction:" + reason);
                    return;
                }

                // Once the one real gift is seen, stop generating new AI
                // offers while waiting for the existing strict solo pause.
                Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "0");
                bool previous;
                if (!DiplomacyFeature.SetSoloTestPaused(true, out previous))
                {
                    AidLifecycleWait("original_ai_gift_save_pause_wait",
                        "native_engine_original_monitor_busy", s);
                    return;
                }

                nativeTradePauseOwned = true;
                if (Director.instance != null)
                    Director.instance.SetPausedState(true);
                stage = 32;
                Note("status=original_ai_gift_ready_for_original_save" +
                     "|source_pid=" + s.Aid.SourcePid +
                     "|generation=" + s.Generation +
                     "|id=" + s.Aid.Id +
                     "|day=" + s.Aid.Day +
                     "|donor=" + s.Aid.Donor +
                     "|recipient=" + s.Aid.Recipient +
                     "|good=" + s.Aid.Good +
                     "|quantity=" + s.Aid.Quantity +
                     "|observed_original_two_native_writes=true" +
                     "|ledger_delivered_and_favor_validated=true" +
                     "|original_save_not_yet_committed=true");
                return;
            }

            if (stage == 32)
            {
                if (s.Generation != AidWarmGeneration ||
                    s.Aid == null)
                {
                    StopAiMilitaryQa(false,
                        "original_gift_or_generation_lost_before_save");
                    return;
                }

                if (!Save("AfterNativeAiAid.sav"))
                    return;
                // The original SaveData owner just committed its native
                // transaction, and may still hold the save-owned engine
                // pause. Never interpret a busy monitor as missing history,
                // and NEVER issue a second SaveData call on retry.
                SealStarted = Time.realtimeSinceStartup;
                stage = 35;
                Note("status=original_aid_save_completed_seal_deferred" +
                     "|source_pid=" + Process.GetCurrentProcess().Id +
                     "|real_gift_id=" + s.Aid.Id +
                     "|original_save=AfterNativeAiAid.sav" +
                     "|native_retry_attempts=0");
                return;
            }

            if (stage == 35)
            {
                if (s.Generation != AidWarmGeneration ||
                    s.Aid == null || s.Aid.SourcePid != Process.GetCurrentProcess().Id ||
                    s.Aid.Generation != AidWarmGeneration)
                {
                    StopAiMilitaryQa(false, "original_gift_or_generation_changed_after_save");
                    return;
                }

                // Restore ONLY the save-owned native/Director pause. Keep
                // the independent gift-observation pause held. No native
                // mutation, no new save, no replay of the original Gift.
                if (savePauseOwned || savePauseRestorePending)
                {
                    FinishSave();
                    if (savePauseOwned || savePauseRestorePending)
                        return;
                    return; // let ORIGINAL engine monitor turn over once
                }
                if (Time.realtimeSinceStartup - SealStarted > 30f)
                {
                    StopAiMilitaryQa(false,
                        "original_aid_save_durable_receipt_timeout");
                    return;
                }

                string reason;
                if (!DiplomacyFeature.WriteAidColdExpectation(
                    "AfterNativeAiAid.sav", out reason))
                {
                    if (reason == "engine_busy" || reason == "state_busy" ||
                        reason == "original_save_history_receipt_not_authenticated")
                    {
                        AidLifecycleWait("waiting_for_original_save_durable_receipt",
                            reason, s);
                        return;
                    }
                    StopAiMilitaryQa(false,
                        "original_aid_save_or_history_receipt_missing:" + reason);
                    return;
                }

                Note("status=original_ai_warm_save_and_cold_expectation_sealed" +
                     "|source_pid=" + Process.GetCurrentProcess().Id +
                     "|generation=" + s.Generation +
                     "|id=" + s.Aid.Id +
                     "|module_mvid=" +
                     typeof(DiplomacyFeature).Assembly.ManifestModule.ModuleVersionId.ToString("D") +
                     "|exact_save=AfterNativeAiAid.sav" +
                     "|full_original_save_SHA_and_private_aid_receipt_required=true" +
                     "|cold_new_process_still_required=true");
                StopAiMilitaryQa(true,
                    "real_original_gift_two_cell_ledger_save_receipt_ready_for_new_process");
                return;
            }

            StopAiMilitaryQa(false, "unexpected_original_ai_warm_stage");
        }
    }
}
#endif
