// Explicit private original-game automation. No UI input events or stock setup.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using CrusaderDE;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    internal static partial class HeadlessQaDriver
    {
        private static float NextPoll, ReplayStarted;
        private static long ReplayStartTick;
        // Original-restart continuation: wall-clock watchdog and sparse diagnostics.
        private const float OriginalRestartTimeoutSeconds = 90f;
        private static float RestartStarted, RestartNextTelemetry;
        private static string RestartLastReason, RestartNativePauseState;
        private static bool RestartBriefingResumed, RestartLocalPauseChecked;
        private static int RestartOriginalBriefingResumeCount;
        private static float RestartLastOriginalBriefingResumeAt;
        // Private test only: allow original delayed load callbacks to settle.
        private const float OriginalBriefingStableSeconds = 4f;
        private static float RestartBriefingVisibleSince = -1f;
        private static bool RestartDirectorPauseReleased;
        // Never wait the full 90 seconds when the *new original map* has
        // already been refused as an active MP lobby. No authority override.
        private static int RestartOriginalLobbyRefusalSamples;
        private static int Ally, qaEnemy, aiMilitaryCommand, SaveAttempts;
        private static bool NativeOriginalTribeEventsArmed;
        private static float NextGateObservationAt;
        private static int EnemyAttempts;
        private static long CommandTick, OldGeneration;
        // One bounded aid-specific run. It never changes real AI teams,
        // positions, stocks, goodwill, original map identity or native roles.
        private const float RealAiAidWarmBudgetSeconds = 180f;
        private const float RealAiAidColdBudgetSeconds = 90f;
        private static float AidWarmStarted, ColdProofStarted, NextAidWaitNote;
        private static long AidWarmGeneration, AidWarmStartTick;
        private static int AidWarmOriginalDay;
        private static bool ColdAuthorityBaselineSaved;
        private static bool NativeDecisionSeen, NativeActiveSeen;
        private static bool ThisDecisionLogged, ThisCommandActive;
        private static bool AiMilitaryQaAction(string value)
        {
            return value == "LOAD_AI_MILITARY" || value == "LOAD_AI_VERIFY" ||
                value == "LOAD_AI_AID_WARM";
        }

        private static bool GenuineAidWarmAction
        {
            get { return action == "LOAD_AI_AID_WARM"; }
        }

        private static bool ColdAction
        {
            get
            {
                return action == "LOAD_AI_VERIFY";
            }
        }

        internal static bool IsAiMilitaryColdMode
        {
            get
            {
                return ColdAction && Environment.GetEnvironmentVariable("DIPLOMACY_AI_MILITARY_TEST") == "1";
            }
        }

        private static bool AnyColdAction
        {
            get
            {
                return action == "LOAD_VERIFY" || ColdAction;
            }
        }

        private static void Begin()
        {
            if (Environment.GetEnvironmentVariable("DIPLOMACY_AI_MILITARY_TEST") != "1")
                throw new InvalidDataException("Explicit AI/military QA environment absent");
            Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "0");
            Environment.SetEnvironmentVariable("DIPLOMACY_AID_PROBE", "1");
            // Original GameAction is still exclusively owned by APIShared.
            // These three private read-only UI/Director hooks are test-only.
            if (action == "LOAD_AI_MILITARY" &&
                !OriginalBriefingCallbackTrace.TryInstall())
            {
                StopAiMilitaryQa(false, "original_UI_instrumentation_unavailable");
                return;
            }
            NextPoll = 0;
            SaveAttempts = 0;
            stage = ColdAction ? 70 : GenuineAidWarmAction ? 30 : 10;
            AidWarmStarted = 0f;
            ColdProofStarted = ColdAction ? Time.realtimeSinceStartup : 0f;
            ColdAuthorityBaselineSaved = false;
            NextAidWaitNote = 0f;
            AidWarmGeneration = -1;
            AidWarmStartTick = -1;
            AidWarmOriginalDay = -1;
            RestartStarted = 0;
            RestartNextTelemetry = 0;
            RestartLastReason = null;
            RestartBriefingResumed = false;
            RestartLocalPauseChecked = false;
            RestartDirectorPauseReleased = false;
            RestartOriginalBriefingResumeCount = 0;
            RestartOriginalLobbyRefusalSamples = 0;
            RestartLastOriginalBriefingResumeAt = -1f;
            Note("status=ai_military_qa_started|cold=" + ColdAction + "|autonomous_ai_military_authorized=false");
        }

        private static void StopAiMilitaryQa(bool passed, string reason)
        {
            if (NativeOriginalTribeEventsArmed)
            {
                // Dispose original EventAPI listeners before stopping the
                // original game, then read out only the recorded event data.
                // The later native identity is NOT a CPU caller identity.
                DiplomacyFeature.StopOriginalCpuTribeReadOnly(
                    "original_human_ally_orders_finished");
                NativeOriginalTribeEventsArmed=false;
            }
            if (action == "LOAD_AI_MILITARY")
                OriginalBriefingCallbackTrace.Stop();
            Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "0");
            Note("status=" + (ColdAction ? "cold_ai_military_finished" :
                GenuineAidWarmAction ? "original_ai_aid_warm_finished" :
                "ai_military_finished") + "|passed=" + passed + "|reason=" +
                reason + "|observed_native_decision=" + NativeDecisionSeen +
                "|observed_native_active=" + NativeActiveSeen);
            FinishSave();
            started = Time.realtimeSinceStartup;
            if (Director.instance != null)
                typeof(Director).GetField("paused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(Director.instance, true);
            stage = 4;
        }

        private static bool Save(string save)
        {
            string why;
            bool saved = DiplomacyFeature.SaveOriginalQa(save, out why);
            Note("status=ai_military_original_save_attempt|save=" + save + "|verified=" + saved + "|reason=" + why);
            if (!saved && ++SaveAttempts >= 20)
            {
                StopAiMilitaryQa(false, "original_save_not_verified_after_bounded_wait");
                return false;
            }

            if (saved)
                SaveAttempts = 0;
            return saved;
        }


        // This private test is a POSITIVE ORIGINAL ORDER OBSERVATION, not an
        // invented AI reply or proof of troop arrival/victory. Original Native
        // emits a Post acceptance and a later matching copied PlayState tuple.
        // Only one current human, alliance, GameAction and native input path
        // can supply the checked fields; this method never writes native state.
        private static bool TryObservePositiveOriginalHumanAllyOrder(
            MilitaryQaOrderObservation o, int opcode, int consulted,
            int local, int target, out string reason)
        {
            reason = "not_a_confirmed_original_ally_activity";
            if (o == null || local < 1 || local > 8 || consulted < 1 ||
                consulted > 8 || consulted == local ||
                (opcode != 11 && (opcode < 1 || opcode > 6)))
                return false;

            reason = "original_evaluator_post_not_accepting";
            if (o.Consulted != consulted || o.Opcode != opcode ||
                o.Requester != local || o.Target != (opcode == 11 ? 0 : target) ||
                !o.NativeDecision || o.PolicyDecision || o.DecisionUnbound ||
                o.Accepted != true || o.ReplySequence <= 0)
                return false;

            reason = "new_original_native_tuple_not_attested";
            if (!o.NativeReadable || o.NativeRequester != local ||
                o.Activity != AllyOrderActivityRelation.MatchingLaterSnapshot ||
                !o.RawNativeAvailable ||
                o.RawNativeSource != OriginalAllyRawSource.CopiedOriginalPlayStateOrderArrays ||
                o.RawNativeFromPlayerField != local)
                return false;

            int expectedKind = opcode == 11 ? 2 : 1;
            if (o.NativeKind != expectedKind ||
                o.RawNativeKind != expectedKind ||
                (opcode == 11
                    ? o.NativeTarget != 0
                    : target < 1 || target > 8 || target == local ||
                      target == consulted || o.NativeTarget != target ||
                      o.RawNativeOrderPlayerField != target))
                return false;

            // Defend's native order_player raw field is NOT an attested target
            // and is deliberately not compared, as in the production bridge.
            reason = "accepted_original_post_and_new_copied_order_observed_no_causal_ack";
            return true;
        }



        private static Dictionary<uint,OriginalAiNativeTribeTarget> OriginalAiTribeTargetsBefore;
        private static int NewEnemyTargetsMax,AllTargetsMax,SameIdsMax,ObjectivesChangedMax;
        private static bool ObjectivesChangedLogged;
        private static bool NewTargetLogged;
        private static void CaptureBefore(MilitaryQaWorldSnapshot s)
        {
            OriginalAiTribeTargetsBefore=s.OriginalAiTribeTargets==null ? null :
                s.OriginalAiTribeTargets
                    .Where(pair=>pair.Value!=null && pair.Value.Owner==Ally)
                    .ToDictionary(pair=>pair.Key,pair=>pair.Value);
            NewEnemyTargetsMax=AllTargetsMax=SameIdsMax=ObjectivesChangedMax=0;
            ObjectivesChangedLogged=false;
            NewTargetLogged=false;
            Note("status=original_native_tribes_before" +
                "|owner="+Ally+"|original_tribes="+
                (OriginalAiTribeTargetsBefore==null?-1:OriginalAiTribeTargetsBefore.Count)+
                "|native_target_changes=0|native_writes=0");
        }
        private static void ObserveAfterAcceptedAttack(MilitaryQaWorldSnapshot s)
        {
            if(aiMilitaryCommand!=1 || !NativeActiveSeen ||
               OriginalAiTribeTargetsBefore==null || s.OriginalAiTribeTargets==null)
                return;
            var order=s.Military[Ally];
            if(order==null || order.Accepted!=true ||
               !order.NativeDecision || order.PolicyDecision ||
               order.DecisionUnbound || order.ReplySequence<=0)
                return;
            int same=0,current=0,newTargets=0,changedObjectives=0;
            foreach(var row in OriginalAiTribeTargetsBefore)
            {
                OriginalAiNativeTribeTarget now;
                if(row.Value==null ||
                   !s.OriginalAiTribeTargets.TryGetValue(row.Key,out now) ||
                   now==null || now.Owner!=Ally ||
                   now.GlobalId!=row.Key || row.Value.Owner!=Ally)
                    continue;
                ++same;
                if(now.ObjectiveX!=row.Value.ObjectiveX ||
                   now.ObjectiveY!=row.Value.ObjectiveY)
                    ++changedObjectives;
                if(now.AttackTargetOwner!=qaEnemy)continue;
                ++current;
                if(row.Value.AttackTargetOwner!=qaEnemy)
                    ++newTargets;
            }
            NewEnemyTargetsMax=Math.Max(NewEnemyTargetsMax,newTargets);
            AllTargetsMax=Math.Max(AllTargetsMax,current);
            SameIdsMax=Math.Max(SameIdsMax,same);
            ObjectivesChangedMax=Math.Max(ObjectivesChangedMax,
                changedObjectives);
            if(!ObjectivesChangedLogged && changedObjectives>0)
            {
                ObjectivesChangedLogged=true;
                Note("status=original_AI_tribe_objectives_changed" +
                    "|consulted="+Ally+"|original_enemy="+qaEnemy+
                    "|same_global_tribes="+same+
                    "|original_changed_objective_coordinates="+changedObjectives+
                    "|original_attack_target_enemy="+current+
                    "|native_game_original_only=true"+
                    "|causal_ack_not_proven=true|native_writes=0");
            }
            if(!NewTargetLogged && newTargets>0)
            {
                NewTargetLogged=true;
                Note("status=new_original_native_ai_tribe_attack_target" +
                     "|human_ally="+Ally+"|original_enemy="+qaEnemy+
                     "|new_tribes_targeting_enemy="+newTargets+
                     "|all_tribes_targeting_enemy="+current+
                     "|same_original_tribe_ids="+same+
                     "|changed_original_objectives="+changedObjectives+
                     "|original_game_field_r_AttackTargetOwnerPlayerId=true"+
                     "|causal_ack_not_claimed=true|native_writes=0");
            }
        }

        private static Dictionary<uint,NativeAiUnitPosition>
            BaselineUnits;
        private static float AcceptedSince;
        private static bool MovementNoted;
        private static int MovedCount, MatchedCount;
        private static void CaptureBaseline(MilitaryQaWorldSnapshot s)
        {
            BaselineUnits = s.NativeAiUnitPositions == null ?
                null : new Dictionary<uint,NativeAiUnitPosition>(
                    s.NativeAiUnitPositions.Where(x => x.Value != null &&
                        x.Value.Owner == Ally).ToDictionary(x => x.Key,x => x.Value));
            CaptureBefore(s);
            AcceptedSince=0f;
            MovementNoted=false;
            MovedCount=MatchedCount=0;
            Note("status=original_ai_unit_position_baseline" +
                 "|consulted=" + Ally + "|native_combat_units=" +
                 (BaselineUnits == null ? -1 : BaselineUnits.Count) +
                 "|original_managed_pause=false|native_writes=0");
        }
        private static void MeasureNativeMovement(
            MilitaryQaWorldSnapshot s)
        {
            // Observation is admitted ONLY AFTER a confirmed original Post
            // acceptance AND a new matching raw PlayState order.
            if (!NativeActiveSeen || BaselineUnits == null ||
                s.NativeAiUnitPositions == null || BaselineUnits.Count == 0)
                return;
            int moved=0,matched=0;
            foreach (var old in BaselineUnits)
            {
                NativeAiUnitPosition now;
                if (!s.NativeAiUnitPositions.TryGetValue(old.Key,out now) ||
                    now == null || now.Owner != Ally ||
                    now.TribeId != old.Value.TribeId)
                    continue;
                ++matched;
                if(Math.Abs(now.WorldX-old.Value.WorldX) +
                   Math.Abs(now.WorldY-old.Value.WorldY)>=2)
                    ++moved;
            }
            if(moved>MovedCount)MovedCount=moved;
            if(matched>MatchedCount)MatchedCount=matched;
            if(moved>0 && !MovementNoted)
            {
                MovementNoted=true;
                Note("status=original_ai_combat_units_moved" +
                    "|consulted=" + Ally+"|opcode="+aiMilitaryCommand+
                    "|moved_same_global_ids=" + moved +
                    "|matched_same_global_ids=" + matched+
                    "|native_original_positions=true" +
                    "|causal_command_to_movement_not_proven=true" +
                    "|no_native_unit_or_tribe_mutation=true" +
                    "|troop_arrival_not_claimed=true");
            }
        }

        private static void MilitarySample(MilitaryQaWorldSnapshot s)
        {
            var o = s.Military[Ally];
            if (o == null)
                return;
            // The QA DTO exposes only the latest HumanSource-bound status. A policy
            // PreSkip or ambiguous Post is still telemetry, never a current AI reply.
            bool current = o.Opcode == aiMilitaryCommand && o.Requester == s.Local && o.Target == (aiMilitaryCommand == 11 ? 0 : qaEnemy);
            bool boundPost = current && o.NativeDecision && !o.PolicyDecision && !o.DecisionUnbound && o.Accepted.HasValue && o.ReplySequence > 0;
            if (boundPost)
                NativeDecisionSeen = true;
            if (current && o.NativeDecision && o.ReplySequence > 0 && !ThisDecisionLogged)
            {
                ThisDecisionLogged = true;
                string origin = o.PolicyDecision ? "pre_skip_policy" : o.DecisionUnbound ? "unbound_original_post" : o.Accepted.HasValue ? "bound_original_post" : "unconfirmed_original_post";
                Note("status=actual_ally_decision_observation|origin=" + origin + "|opcode=" + aiMilitaryCommand + "|consulted=" + Ally + "|requester=" + o.Requester + "|target=" + o.Target + "|accepted=" + (o.Accepted.HasValue ? o.Accepted.Value.ToString() : "unconfirmed") + "|reply_sequence=" + o.ReplySequence + "|latest_human_source=true" + "|policy_decision=" + o.PolicyDecision + "|decision_unbound=" + o.DecisionUnbound + "|bound_post_value=" + boundPost + "|immutable_original_cpu_result_claimed=false|causal_ack_claimed=false");
            }

            // A complete *live* native tuple is separate from the evaluator callback:
            // defend kind=2,target=0,from=local; attack kind=1,target=enemy,from=local.
            bool exact = o.NativeReadable && o.NativeRequester == s.Local && (aiMilitaryCommand == 11 ? o.NativeKind == 2 && o.NativeTarget == 0 : o.NativeKind == 1 && o.NativeTarget == qaEnemy);
            if (exact && !ThisCommandActive)
            {
                string relation = o.Activity == AllyOrderActivityRelation.MatchingRetainedSnapshot ? "retained_snapshot" : o.Activity == AllyOrderActivityRelation.MatchingAlreadyPresent ? "already_present" : o.Activity == AllyOrderActivityRelation.MatchingLaterSnapshot ? "later_snapshot" : o.Activity == AllyOrderActivityRelation.NoMatchingOrder ? "status_nonmatching" : "unclassified";
                // Raw fields are read-only telemetry from the same native-
                // derived PlayState. A defence order's raw order_player field
                // is uninterpreted; native_target=0 above is a projection.
                Note("status=actual_ally_native_activity_observed|opcode=" + aiMilitaryCommand + "|consulted=" + Ally + "|native_kind=" + o.NativeKind + "|native_target=" + o.NativeTarget + "|native_from=" + o.NativeRequester + "|raw_available=" + o.RawNativeAvailable + "|raw_kind=" + (o.RawNativeAvailable ? o.RawNativeKind.ToString() : "unavailable") + "|raw_order_player_field=" + (o.RawNativeAvailable ? o.RawNativeOrderPlayerField.ToString() : "unavailable") + "|raw_from_player_field=" + (o.RawNativeAvailable ? o.RawNativeFromPlayerField.ToString() : "unavailable") + "|raw_source=" + o.RawNativeSource + "|target_projection=" + o.NativeTargetProjection + "|defend_raw_target_meaning_attested=false" + "|activity=" + o.Activity + "|activity_relation=" + relation + "|tick=" + s.Tick + "|causal_success_claimed=false|causal_ack_claimed=false|troop_arrival_claimed=false|victory_claimed=false");
            }

            if (exact)
            {
                ThisCommandActive = true;
                string positiveWitness;
                if (!NativeActiveSeen && TryObservePositiveOriginalHumanAllyOrder(
                        o, aiMilitaryCommand, Ally, s.Local,
                        aiMilitaryCommand == 11 ? 0 : qaEnemy, out positiveWitness))
                {
                    // Only a positive evaluator Post AND a new original
                    // PlayState tuple may set this positive QA observation.
                    NativeActiveSeen = true;
                    Note("status=positive_original_ally_active_observed" +
                         "|opcode=" + aiMilitaryCommand + "|consulted=" + Ally +
                         "|reason=" + positiveWitness +
                         "|causal_ack_claimed=false|troop_arrival_claimed=false");
                }
            }
        }

        private static bool RestartOriginalAiScenario(MilitaryQaWorldSnapshot s, out string reason)
        {
            reason = "original_restart_context_absent";
            if (s.Local != 1 || !s.Humans.SequenceEqual(new[] { 1 }) || !s.Ai.SequenceEqual(new[] { 2, 3 }) || !s.Alliances[1, 2] || !s.Alliances[2, 1] || s.Alliances[1, 3] || s.Alliances[3, 1])
            {
                reason = "exact_existing_three_player_restart_configuration_not_applicable";
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var vm = MainViewModel.Instance;
            var menu = vm == null ? null : vm.HUDIngameMenu;
            if (menu == null)
                return false;
            var field = menu.GetType().GetField("restartSkirmishMapInfo", flags);
            object original = field == null ? null : field.GetValue(menu);
            if (original == null)
                return false;
            Type type = original.GetType();
            var encode = type.GetMethod("encode", flags, null, Type.EmptyTypes, null);
            var decode = type.GetMethod("decode", flags, null, new[] { typeof(byte[]), typeof(bool) }, null);
            if (encode == null || decode == null)
            {
                reason = "original_restart_codec_absent";
                return false;
            }

            // Original MapFileManager passes false for an ordinary non-trail save.
            object copy = decode.Invoke(null, new object[] { encode.Invoke(original, null), false });
            if (copy == null)
            {
                reason = "original_restart_codec_returned_no_ordinary_map";
                return false;
            }

            var teamsField = type.GetField("teams", flags);
            var lordsField = type.GetField("lordTypes", flags);
            var teams = teamsField == null ? null : teamsField.GetValue(copy) as IList<int>;
            var lords = lordsField == null ? null : lordsField.GetValue(copy) as IList<int>;
            Note("status=original_restart_members_observed|teams=" + (teams == null ? "absent" : string.Join(",", teams)) + "|lord_types=" + (lords == null ? "absent" : string.Join(",", lords)));
            // The real restart packet has eight slots. The original MPLobby ctor
            // recognizes -9999 as a dummy to kick; nonnegative lords are original AI.
            // Verify the exact three live slots against the actual PlayState above.
            if (teams == null || lords == null || teams.Count != 8 || lords.Count != 8 || lords[0] != -1 || lords[1] < 0 || lords[2] < 0 || Enumerable.Range(3, 5).Any(i => lords[i] != -9999 || teams[i] != 0) || teams[0] != teams[1] || teams[0] == teams[2])
            {
                reason = "original_restart_member_team_mapping_not_attested";
                return false;
            }

            // A fresh ordinary skirmish with the SAME real three lords. Change only
            // original pregame team selection: two AI on the existing opposing team.
            // Original start owns every actual new-map/team/resource initialization.
            int old = teams[1];
            teams[1] = teams[2];
            object front = vm.GetType().GetField("FRONTMultiplayer", flags).GetValue(vm);
            if (front == null)
            {
                reason = "original_front_multiplayer_absent";
                return false;
            }

            var restart = front.GetType().GetMethod("RestartSkirmishGame", flags, null, new[] { type }, null);
            if (restart == null)
            {
                reason = "original_restart_entry_absent";
                return false;
            }

            OldGeneration = s.Generation;
            Note("status=original_fresh_ai_scenario_restart_requested|active_members=3|slots=8|human=1|ai=2,3|original_team_before=" + old + "|original_team_after=" + teams[1] + "|native_goods_setup_calls=0");
            // The original entry owns native map start/load and the post-update briefing.
            // Start the wall-clock deadline after that synchronous entry has returned.
            try
            {
                OriginalBriefingCallbackTrace.Arm();
                restart.Invoke(front, new[] { copy });
            }
            catch (Exception e)
            {
                reason = "original_restart_entry_exception:" + (e is TargetInvocationException && e.InnerException != null ? e.InnerException.GetType().Name : e.GetType().Name);
                return false;
            }

            RestartStarted = Time.realtimeSinceStartup;
            RestartNextTelemetry = 0;
            RestartLastReason = null;
            RestartBriefingResumed = false;
            RestartLocalPauseChecked = false;
            RestartDirectorPauseReleased = false;
            RestartOriginalBriefingResumeCount = 0;
            RestartOriginalLobbyRefusalSamples = 0;
            RestartLastOriginalBriefingResumeAt = -1f;
            RestartBriefingVisibleSince = -1f;
            RestartNativePauseState = "unread";
            Note("status=original_restart_entry_returned|fresh_map_not_yet_verified|stage20_timeout_seconds=" + OriginalRestartTimeoutSeconds);
            reason = "original_restart_entry_returned_not_yet_verified";
            return true;
        }

        private static void RestartWait(string reason)
        {
            float now = Time.realtimeSinceStartup;
            if (reason == RestartLastReason && now < RestartNextTelemetry)
                return;
            RestartLastReason = reason;
            RestartNextTelemetry = now + 12f;
            string directorPaused = "unavailable", story = "unavailable", visible = "unavailable", prePaused = "unavailable", mode = "unavailable";
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var director = Director.instance;
                if (director != null)
                {
                    var f = typeof(Director).GetField("paused", flags);
                    if (f != null)
                        directorPaused = Convert.ToString(f.GetValue(director));
                }

                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    var type = typeof(MainViewModel);
                    var f = type.GetField("BriefingFromStory", flags);
                    if (f != null)
                        story = Convert.ToString(f.GetValue(vm));
                    f = type.GetField("preBriefingWasPaused", flags);
                    if (f != null)
                        prePaused = Convert.ToString(f.GetValue(vm));
                    f = type.GetField("BriefingMode", flags);
                    if (f != null)
                        mode = Convert.ToString(f.GetValue(vm));
                    var p = type.GetProperty("Show_HUD_Briefing", flags);
                    if (p != null && p.GetGetMethod(true) != null)
                        visible = Convert.ToString(p.GetValue(vm, null));
                }
            }
            catch (Exception e)
            {
                reason += ":diagnostic_" + e.GetType().Name;
            }

            Note("status=original_restart_stage20_wait|elapsed_seconds=" + (int)(now - RestartStarted) + "|reason=" + reason + "|director_paused=" + directorPaused + "|briefing_from_story=" + story + "|briefing_visible=" + visible + "|briefing_mode=" + mode + "|pre_briefing_paused=" + prePaused + "|native_local_paused=" + (RestartNativePauseState ?? "unread") + "|resume_callback_completed=" + RestartBriefingResumed);
        }

        // Runs BEFORE snapshot acquisition; the original post-update callback must
        // create a genuine FromStory briefing before we issue its original Resume.
        private static bool ContinueOriginalRestart()
        {
            float elapsed = Time.realtimeSinceStartup - RestartStarted;
            if (RestartStarted <= 0 || elapsed >= OriginalRestartTimeoutSeconds)
            {
                RestartWait("bounded_original_restart_timeout");
                StopAiMilitaryQa(false, "original_restart_stage20_timeout:" + RestartLastReason);
                return false;
            }

            var vm = MainViewModel.Instance;
            var director = Director.instance;
            if (vm == null || director == null)
            {
                RestartWait("original_view_or_director_unavailable");
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = typeof(MainViewModel);
            var fromStory = type.GetField("BriefingFromStory", flags);
            var briefingMode = type.GetField("BriefingMode", flags);
            var prePaused = type.GetField("preBriefingWasPaused", flags);
            var shown = type.GetProperty("Show_HUD_Briefing", flags);
            if (fromStory == null || fromStory.FieldType != typeof(bool) || briefingMode == null || briefingMode.FieldType != typeof(int) || prePaused == null || prePaused.FieldType != typeof(bool) || shown == null || shown.PropertyType != typeof(bool) || shown.GetGetMethod(true) == null)
            {
                StopAiMilitaryQa(false, "pinned_original_briefing_state_accessors_absent");
                return false;
            }

            // original restart observation: The genuine original restart can re-open another FromStory
            // briefing after the first Resume. Never treat the first callback as
            // permanent evidence: only a freshly visible actual original
            // Noesis briefing may trigger another ORIGINAL Resume.
            if (RestartBriefingResumed)
            {
                bool againStory, againVisible;
                int againMode;
                try
                {
                    againStory = (bool)fromStory.GetValue(vm);
                    againVisible = (bool)shown.GetValue(vm, null);
                    againMode = (int)briefingMode.GetValue(vm);
                }
                catch (Exception e)
                {
                    StopAiMilitaryQa(false, "original_repeat_briefing_read_exception:" +
                        e.GetType().Name);
                    return false;
                }
                if (againStory && againVisible && againMode == 1)
                {
                    // Avoid immediate double-resume of one UI frame.
                    if (Time.realtimeSinceStartup -
                            RestartLastOriginalBriefingResumeAt < 1f)
                        return false;
                    if (RestartOriginalBriefingResumeCount >= 3)
                    {
                        StopAiMilitaryQa(false,
                            "original_story_briefing_reopened_beyond_bounded_3_resumes");
                        return false;
                    }
                    RestartBriefingResumed = false;
                    RestartBriefingVisibleSince = -1f;
                    RestartLocalPauseChecked = false;
                    RestartDirectorPauseReleased = false;
                    Note("status=actual_original_story_briefing_reopened" +
                         "|prior_resumes=" + RestartOriginalBriefingResumeCount +
                         "|no_synthetic_hud_clicks=true");
                }
            }

            if (!RestartBriefingResumed)
            {
                bool story, visible;
                int mode;
                try
                {
                    story = (bool)fromStory.GetValue(vm);
                    visible = (bool)shown.GetValue(vm, null);
                    mode = (int)briefingMode.GetValue(vm);
                }
                catch (Exception e)
                {
                    StopAiMilitaryQa(false, "original_briefing_state_read_exception:" + e.GetType().Name);
                    return false;
                }

                if (!story || !visible || mode != 1)
                {
                    RestartBriefingVisibleSince = -1f;
                    RestartWait("waiting_for_actual_original_from_story_briefing");
                    return false;
                }

                var hud = type.GetField("HUDRoot", flags);
                var panel = type.GetField("HUDBriefingPanel", flags);
                if (hud == null || panel == null)
                {
                    StopAiMilitaryQa(false, "original_briefing_hud_fields_absent");
                    return false;
                }

                if (hud.GetValue(vm) == null || panel.GetValue(vm) == null || GameData.Instance == null || GameData.Instance.lastGameState == null)
                {
                    RestartWait("original_briefing_hud_or_playstate_not_ready");
                    return false;
                }

                string sessionReason;
                if (!DiplomacyFeature.IsSoloEngineSession(out sessionReason))
                {
                    RestartWait("original_briefing_private_solo_session_not_ready:" + sessionReason);
                    return false;
                }

                // original resume guard: never press Original Resume during a transient
                // pre-load story screen. The public managed callback can
                // re-enter LoadCampaignMap when the previous campaign
                // difficulty/game type remains active. This is an observed
                // original ABI branch, not a synthetic UI/controller action.
                GameData originalWorld = GameData.Instance;
                if (originalWorld == null)
                {
                    RestartWait("original_world_unavailable_before_resume");
                    return false;
                }
                int originalGameType = (int)originalWorld.game_type;
                int originalDifficulty = (int)originalWorld.difficulty_level;
                if (originalGameType == 0 && originalDifficulty != -1 &&
                    originalDifficulty != 1)
                {
                    RestartWait("original_resume_would_reload_campaign" +
                        "|type=" + originalGameType +
                        "|difficulty=" + originalDifficulty);
                    return false;
                }

                if (RestartBriefingVisibleSince < 0f)
                {
                    RestartBriefingVisibleSince = Time.realtimeSinceStartup;
                    Note("status=original_restart_visible_briefing_stability_started" +
                        "|game_type=" + originalGameType +
                        "|difficulty=" + originalDifficulty +
                        "|no_synthetic_input=true");
                    return false;
                }
                if (Time.realtimeSinceStartup -
                        RestartBriefingVisibleSince < OriginalBriefingStableSeconds)
                    return false;

                var resume = type.GetMethod("ButtonBriefingResume", flags, null, new[] { typeof(object) }, null);
                if (resume == null || resume.ReturnType != typeof(void))
                {
                    StopAiMilitaryQa(false, "pinned_original_briefing_resume_overload_absent");
                    return false;
                }

                try
                {
                    resume.Invoke(vm, new object[] { null });
                }
                catch (Exception e)
                {
                    StopAiMilitaryQa(false, "original_briefing_resume_exception:" + (e is TargetInvocationException && e.InnerException != null ? e.InnerException.GetType().Name : e.GetType().Name));
                    return false;
                }

                if ((bool)fromStory.GetValue(vm) || (bool)shown.GetValue(vm, null))
                {
                    StopAiMilitaryQa(false, "original_briefing_resume_did_not_restore_ui");
                    return false;
                }

                RestartBriefingResumed = true;
                ++RestartOriginalBriefingResumeCount;
                RestartLastOriginalBriefingResumeAt = Time.realtimeSinceStartup;
                RestartBriefingVisibleSince = -1f;
                Note("status=original_restart_briefing_resumed_via_original_callback|method=MainViewModel.ButtonBriefingResume(object)" + "|original_action_1045=true|story_action_1071=true|pre_briefing_paused=" + prePaused.GetValue(vm));
                return false;
            }

            if (!RestartLocalPauseChecked)
            {
                bool localPaused;
                string pauseReason;
                if (!DiplomacyFeature.TryReadOriginalRestartSoloPause(out localPaused, out pauseReason))
                {
                    RestartWait("original_local_pause_monitor_wait:" + pauseReason);
                    return false;
                }

                RestartNativePauseState = localPaused ? "true" : "false";
                if (localPaused)
                {
                    bool previous;
                    if (!DiplomacyFeature.SetSoloTestPaused(false, out previous))
                    {
                        RestartWait("strict_solo_original_local_unpause_not_acquired");
                        return false;
                    }

                    Note("status=" + (previous ? "original_restart_local_pause_cleared" : "original_restart_local_pause_already_clear_on_recheck") + "|previous_local_paused=" + previous + "|used_existing_strict_solo_pause_method=true|inventory_authority_unchanged=true");
                    RestartNativePauseState = "false";
                }
                else
                    Note("status=original_restart_local_pause_already_clear|under_original_monitor=true");
                RestartLocalPauseChecked = true;
            }

            // The original ButtonBriefingResume only clears Director.paused
            // when !Director.MultiplayerGame(). A one-human original skirmish
            // can still report game networking enabled with no remote peer.
            // Use the existing ORIGINAL managed SetPausedState(false), as the
            // same private QA driver already does for stage-10 simulation,
            // only after checking the real original Solo identity and after
            // the latest FromStory briefing is demonstrably closed.
            if (!RestartDirectorPauseReleased)
            {
                bool story, visible;
                try
                {
                    story = (bool)fromStory.GetValue(vm);
                    visible = (bool)shown.GetValue(vm, null);
                }
                catch (Exception e)
                {
                    StopAiMilitaryQa(false,
                        "original_restart_before_unpause_briefing_read_" +
                        e.GetType().Name);
                    return false;
                }
                if (story || visible)
                {
                    RestartWait("wait_actual_original_briefing_close_before_director_unpause");
                    return false;
                }
                string soloReason;
                if (!DiplomacyFeature.IsSoloEngineSession(out soloReason))
                {
                    RestartWait("one_human_original_solo_required_for_director_resume:" +
                        soloReason);
                    return false;
                }
                var directorPaused = typeof(Director).GetField(
                    "paused", flags);
                if (directorPaused == null ||
                    directorPaused.FieldType != typeof(bool))
                {
                    StopAiMilitaryQa(false,
                        "original_director_pause_field_contract_absent");
                    return false;
                }
                bool wasDirectorPaused = (bool)directorPaused.GetValue(director);
                if (wasDirectorPaused)
                {
                    // No native-goods write and no unmanaged pause-field edit.
                    // The original managed method is also used elsewhere in
                    // the existing private military test flow.
                    director.SetPausedState(false);
                    if ((bool)directorPaused.GetValue(director))
                    {
                        StopAiMilitaryQa(false,
                            "original_director_managed_resume_rejected");
                        return false;
                    }
                }
                RestartDirectorPauseReleased = true;
                Note("status=private_restarted_director_managed_resume_verified" +
                     "|was_paused=" + wasDirectorPaused +
                     "|real_original_solo=true|original_callback_resumes=" +
                     RestartOriginalBriefingResumeCount +
                     "|no_native_goods_mutation=true");
            }

            return true;
        }

        // No scenario result is inferred from a favorable counter or
        // a restored Delivered record. The one original process must have
        // observed an exact two-cell Gift and committed that SAME pledge.
        private static bool ObservedCurrentNativeAiGift(MilitaryQaWorldSnapshot s,
            out string reason)
        {
            reason = "original_native_gift_not_observed";
            var a = s == null ? null : s.Aid;
            if (a == null)
                return false;
            if (s.Generation != AidWarmGeneration ||
                a.SourcePid != Process.GetCurrentProcess().Id ||
                a.Generation != s.Generation || a.Map != s.Map ||
                a.Roster != s.Roster || a.Tick < AidWarmStartTick ||
                a.Day < AidWarmOriginalDay || a.Day > s.Day ||
                !s.Ai.Contains(a.Donor) || !s.Ai.Contains(a.Recipient) ||
                a.Donor == a.Recipient || a.Quantity < 3 || a.Quantity > 40 ||
                a.Good < 1 || a.Good > 24 ||
                !s.Alliances[a.Donor, a.Recipient] ||
                !s.Alliances[a.Recipient, a.Donor] ||
                a.DonorAfter != a.DonorBefore - a.Quantity ||
                a.RecipientAfter != a.RecipientBefore + a.Quantity)
            {
                reason = "current_original_native_gift_identity_or_roles_mismatch";
                return false;
            }

            int calls;
            if (s.AidWriteAttempts == null ||
                !s.AidWriteAttempts.TryGetValue(a.Id, out calls) ||
                calls != 2 || s.AidLedger == null)
            {
                reason = "original_two_cell_write_and_ledger_not_attested";
                return false;
            }

            try
            {
                var ledger = AidPromiseLedgerCodec.Decode(s.AidLedger,
                    s.Map, s.Roster, s.Day);
                if (ledger.Promises.Count(p => p.Id == a.Id &&
                    p.Status == AidPledgeStatus.Delivered &&
                    p.Donor == a.Donor && p.Recipient == a.Recipient &&
                    p.Good == a.Good && p.Quantity == a.Quantity &&
                    p.IssuedDay < a.Day && p.DueDay >= a.Day) != 1)
                {
                    reason = "original_committed_gift_missing_from_validated_ledger";
                    return false;
                }

                reason = "same_original_process_generation_two_cells_and_delivered_ledger";
                return true;
            }
            catch (Exception)
            {
                reason = "original_aid_ledger_decode_or_scope_refused";
                return false;
            }
        }

        private static void AidLifecycleWait(string status, string reason,
            MilitaryQaWorldSnapshot s)
        {
            if (Time.realtimeSinceStartup < NextAidWaitNote)
                return;
            NextAidWaitNote = Time.realtimeSinceStartup + 12f;
            Note("status=" + status + "|reason=" + reason + "|generation=" +
                (s == null ? -1 : s.Generation) + "|day=" +
                (s == null ? -1 : s.Day) +
                "|native_gift_claimed=false|no_stock_or_goodwill_adjustment=true");
        }

        private static void TickAiMilitaryQa()
        {
            if (Time.realtimeSinceStartup < NextPoll)
                return;
            NextPoll = Time.realtimeSinceStartup + 0.5f;
            // Bounded monotonic waits, including when the original
            // snapshot/lease is merely not ready. None can become a Gift.
            if ((stage == 21 || stage == 31) && AidWarmStarted > 0f &&
                Time.realtimeSinceStartup - AidWarmStarted >= RealAiAidWarmBudgetSeconds)
            {
                StopAiMilitaryQa(false, "bounded_no_new_original_native_aid_within_warm_budget");
                return;
            }

            if (ColdAction && (stage == 70 || stage == 71 || stage == 72 || stage == 73) &&
                ColdProofStarted > 0f &&
                Time.realtimeSinceStartup - ColdProofStarted >= RealAiAidColdBudgetSeconds)
            {
                StopAiMilitaryQa(false, "bounded_cold_aid_verification_not_completed");
                return;
            }

            // Stage 20 must advance the original UI/connection pause before a ready-map read.
            if (stage == 20 && !ContinueOriginalRestart())
                return;
            // original lobby: Observe, never mutate, the original network lobby.
            // The fact that one human slot existed in the OLD game does not
            // authorize native calls after RestartSkirmishGame. Only refuse
            // after this NEW generation is durably disabled as a live lobby,
            // in three consecutive managed observations over eight seconds.
            if (stage == 20)
            {
                long checkedGeneration;
                bool disabledNewMap, originalLobbyActive;
                if (DiplomacyFeature.TryReadQaRestartAuthority(
                        out checkedGeneration, out disabledNewMap,
                        out originalLobbyActive) &&
                    checkedGeneration > OldGeneration &&
                    disabledNewMap && originalLobbyActive)
                {
                    ++RestartOriginalLobbyRefusalSamples;
                    if (RestartOriginalLobbyRefusalSamples >= 3 &&
                        Time.realtimeSinceStartup - RestartStarted >= 8f)
                    {
                        Note("status=original_new_lobby_authority_refused" +
                            "|old_generation=" + OldGeneration +
                            "|new_generation=" + checkedGeneration +
                            "|actual_multiplayer_lobby_active=true" +
                            "|map_already_disabled=true" +
                            "|other_humans_not_fabricated=true" +
                            "|native_aid_not_authorized=true");
                        StopAiMilitaryQa(false,
                            "original_new_generation_active_multiplayer_lobby_fail_closed");
                        return;
                    }
                }
                else
                    RestartOriginalLobbyRefusalSamples = 0;
            }
            string why;
            MilitaryQaWorldSnapshot s;
            if (!DiplomacyFeature.TryReadAiMilitaryQa(out s, out why))
            {
                if (stage == 20)
                    RestartWait("actual_restarted_snapshot_wait:" + why);
                else if (why != "engine_busy" && why != "state_busy" && why != "map_not_ready")
                    Note("status=ai_military_observation_waiting|reason=" + why);
                return;
            }

            if (GenuineAidWarmAction)
            {
                TickOriginalAidWarm(s);
                return;
            }

            if (stage == 10)
            {
                if (!Save("HumanMilitaryBaseline.sav"))
                    return;
                bool previouslyPaused;
                if (!DiplomacyFeature.SetSoloTestPaused(false, out previouslyPaused))
                    return;
                Director.instance.SetPausedState(false);
                // First choose ONLY a verified original POSITIVE Native
                // human/AI team, never accidental 0==0 relation equality.
                EnemyAttempts = 1;
                int localTeam = s.OriginalNativeTeams != null &&
                    s.OriginalNativeTeams.Length == 9 ?
                    s.OriginalNativeTeams[s.Local] : 0;
                Ally = localTeam > 0 ?
                    s.Ai.FirstOrDefault(p =>
                        s.OriginalNativeTeams[p] == localTeam &&
                        s.Alliances[s.Local, p] &&
                        s.Alliances[p, s.Local]) : 0;
                qaEnemy = s.Ai.FirstOrDefault(p => p != Ally &&
                    (localTeam <= 0 || s.OriginalNativeTeams[p] != localTeam) &&
                    !s.Alliances[s.Local, p] &&
                    !s.Alliances[p, s.Local] &&
                    (Ally == 0 || !s.Alliances[Ally, p] &&
                     !s.Alliances[p, Ally]));
                Note("status=original_team_military_route" +
                     "|original_local_team=" + localTeam +
                     "|original_verified_ally=" + Ally +
                     "|original_enemy=" + qaEnemy +
                     "|real_active_AI=" + string.Join(",", s.Ai) +
                     "|native_authority_unchanged=true");
                if (Ally == 0 || qaEnemy == 0)
                {
                    StopAiMilitaryQa(false, "actual_human_ally_and_enemy_required");
                    return;
                }

                aiMilitaryCommand = 11;
                CaptureBaseline(s);
                ThisDecisionLogged = false;
                ThisCommandActive = false;
                if (!DiplomacyFeature.SubmitExplicitHumanAllyOrder(11, Ally, 0, out why))
                {
                    Note("status=human_command_waiting|reason=" + why);
                    return;
                }

                CommandTick = s.Tick;
                stage = 11;
                Note("status=actual_original_human_defend_input_retained|consulted=" + Ally);
                return;
            }

            if (stage == 11 || stage == 13)
            {
                MilitarySample(s);
                var o = s.Military[Ally];
                // Only the latest HUMAN-origin, tuple-matched, bound original Post
                // evaluator decline may end the wait early. A PreSkip policy value or
                // unbound/stale Post cannot terminate the current real command.
                bool denied = o != null && o.Opcode == aiMilitaryCommand && o.Requester == s.Local && o.Target == (aiMilitaryCommand == 11 ? 0 : qaEnemy) && o.NativeDecision && !o.PolicyDecision && !o.DecisionUnbound && o.Accepted == false && o.ReplySequence > 0;
                bool elapsed = s.Tick - CommandTick >= 768;
                if (ThisCommandActive)
                {
                    if(AcceptedSince==0f)
                        AcceptedSince=Time.realtimeSinceStartup;
                    MeasureNativeMovement(s);
                    ObserveAfterAcceptedAttack(s);

                    // original native attack: this is an EXTRA mod-owned native order bound to
                    // a real accepted HUMAN command, not a synthetic CPU ACK.
                    // No retry after a started SDK call and no original
                    // consent/queue/Steam gate bypass.
                    if (stage == 13 &&
                        Environment.GetEnvironmentVariable(
                            "DIPLOMACY_NATIVE_HUMAN_ALLY_GOAL_CANARY")=="1")
                    {
                        string issuedReason;
                        bool invoked=DiplomacyFeature.TryIssueAcceptedHumanNativeAttack(
                            s,Ally,qaEnemy,out issuedReason);
                        if (invoked || issuedReason.StartsWith(
                            "original_native_order_outcome_unknown") ||
                            issuedReason.StartsWith(
                            "mod_native_wrapper_returned_unconfirmed"))
                        {
                            Note("status=mod_owned_accepted_human_native_attack"+
                                 "|attempted="+invoked+
                                 "|reason="+issuedReason+
                                 "|consulted="+Ally+"|enemy="+qaEnemy+
                                 "|original_cpu_ack_claimed=false");
                        }
                        // original ally native gate: sparse read-only provenance for the REAL
                        // original decision. Unknown/busy is never an ACK,
                        // never a native issue. No latency-driven retry.
                        if (!invoked &&
                            Time.realtimeSinceStartup >=
                                NextGateObservationAt)
                        {
                            NextGateObservationAt =
                                Time.realtimeSinceStartup + 6f;
                            Note("status=real_original_ally_native_gate" +
                                 "|reason=" + issuedReason +
                                 "|opcode=" + aiMilitaryCommand +
                                 "|consulted=" + Ally + "|enemy=" + qaEnemy +
                                 "|no_native_order_fabricated=true");
                        }
                        string goalNote;
                        if (DiplomacyFeature.ObserveActualNativeAttackGoal(
                                out goalNote))
                        {
                            Note("status=original_native_enemy_staging_goal_observed"+
                                 "|consulted="+Ally+"|target="+qaEnemy+
                                 "|event="+goalNote+
                                 "|mod_owned_native_move=true"+
                                 "|arrival_or_combat_not_proven=true");
                        }
                    }

                    // Keep the genuinely accepted ORIGINAL order active
                    // long enough for native simulation to move troops.
                    // A lack of observed motion is NOT converted to false
                    // acceptance or a synthetic order.
                    if(Time.realtimeSinceStartup-AcceptedSince<
                        (aiMilitaryCommand==1?115f:18f))
                        return;
                    Note("status=accepted_original_order_observation_finished" +
                         "|consulted="+Ally+"|opcode="+aiMilitaryCommand+
                         "|original_units_moved="+MovedCount+
                         "|same_original_units_tracked="+MatchedCount+
                         "|movement_does_not_establish_causal_ack=true" +
                         "|original_native_unit_writes=0");
                }
                if (!denied && !ThisCommandActive && !elapsed)
                    return;
                if (!DiplomacyFeature.SubmitExplicitHumanAllyOrder(10, Ally, 0, out why))
                    return;
                string basis = ThisCommandActive ? "full_native_tuple_observed" : denied ? "bound_original_post_decline" : "bounded_observation_window_elapsed";
                Note("status=actual_original_human_withdraw_input_retained|after_opcode=" + aiMilitaryCommand + "|transition_basis=" + basis + "|bound_post_declined=" + denied + "|native_activity_observed=" + ThisCommandActive + "|policy_or_unbound_decision_used=false|causal_ack_claimed=false");
                CommandTick = s.Tick;
                stage = stage == 11 ? 12 : 14;
                return;
            }

            if (stage == 12)
            {
                var o = s.Military[Ally];
                if ((o == null || o.Queued || o.InFlight) && s.Tick - CommandTick < 384)
                    return;
                aiMilitaryCommand = 1;
                // Actual originally loaded enemy keep & ally keep-door
                // coordinates, not the map name/start keep metadata.
                bool enemyDoorKnown = s.OriginalKeepDoorKnown != null &&
                    s.OriginalKeepDoorKnown.Length == 9 &&
                    qaEnemy >= 1 && qaEnemy <= 8 &&
                    s.OriginalKeepDoorKnown[qaEnemy];
                bool allyDoorKnown = s.OriginalKeepDoorKnown != null &&
                    s.OriginalKeepDoorKnown.Length == 9 &&
                    Ally >= 1 && Ally <= 8 &&
                    s.OriginalKeepDoorKnown[Ally];
                Note("status=original_keep_door_live_native_api" +
                    "|enemy="+qaEnemy+
                    "|enemy_known="+enemyDoorKnown+
                    "|enemy_x="+(enemyDoorKnown?
                          s.OriginalKeepDoorX[qaEnemy]:-1)+
                    "|enemy_y="+(enemyDoorKnown?
                          s.OriginalKeepDoorY[qaEnemy]:-1)+
                    "|ally="+Ally+
                    "|ally_known="+allyDoorKnown+
                    "|ally_x="+(allyDoorKnown?
                          s.OriginalKeepDoorX[Ally]:-1)+
                    "|ally_y="+(allyDoorKnown?
                          s.OriginalKeepDoorY[Ally]:-1)+
                    "|no_original_game_mutation=true");
                CaptureBaseline(s);
                ThisDecisionLogged = false;
                ThisCommandActive = false;
                if (!DiplomacyFeature.SubmitExplicitHumanAllyOrder(1, Ally, qaEnemy, out why))
                {
                    Note("status=human_attack_input_waiting|reason=" + why);
                    return;
                }

                // Original user's real managed GameAction input is retained.
                // Only NOW subscribe to the Original native tribe-order
                // entry hooks; do not alter orders, returns or AI decisions.
                string originalEntryReason;
                NativeOriginalTribeEventsArmed =
                    DiplomacyFeature.TryStartOriginalCpuTribeReadOnly(
                        out originalEntryReason);
                Note("status=original_native_order_callbacks_subscribed" +
                     "|installed="+NativeOriginalTribeEventsArmed +
                     "|reason="+originalEntryReason+
                     "|AI_requester_or_native_ack_claimed=false");
                if(!NativeOriginalTribeEventsArmed)
                {
                    StopAiMilitaryQa(false,
                        "original_readonly_native_tribe_event_probe_unavailable");
                    return;
                }

                Note("status=actual_original_human_attack_input_retained|consulted=" + Ally + "|target=" + qaEnemy);
                CommandTick = s.Tick;
                stage = 13;
                return;
            }

            if (stage == 14)
            {
                var o = s.Military[Ally];
                if ((o == null || o.Queued || o.InFlight) &&
                    s.Tick - CommandTick < 384)
                    return;
                // NO RestartSkirmishGame: it creates a new Multiplayer lobby.
                // Try another already-observed ORIGINAL enemy under the same
                // one-human, positive Native ally team and current generation.
                // These are new genuine local HUMAN source requests through
                // the existing shared GameAction broker, not guessed AI ACKs.
                int next = !NativeActiveSeen && EnemyAttempts < 6 &&
                    s.OriginalNativeTeams != null &&
                    s.OriginalNativeTeams.Length == 9 &&
                    s.OriginalNativeTeams[s.Local] > 0 &&
                    s.OriginalNativeTeams[Ally] ==
                        s.OriginalNativeTeams[s.Local] ?
                    s.Ai.FirstOrDefault(p => p > qaEnemy && p != Ally &&
                        s.OriginalNativeTeams[p] !=
                            s.OriginalNativeTeams[s.Local] &&
                        !s.Alliances[s.Local, p] &&
                        !s.Alliances[p, s.Local] &&
                        !s.Alliances[Ally, p] &&
                        !s.Alliances[p, Ally]) : 0;
                if (next > 0)
                {
                    qaEnemy = next;
                    ++EnemyAttempts;
                    aiMilitaryCommand = 1;
                    ThisDecisionLogged = false;
                    ThisCommandActive = false;
                    if (DiplomacyFeature.SubmitExplicitHumanAllyOrder(
                            1, Ally, qaEnemy, out why))
                    {
                        CommandTick = s.Tick;
                        stage = 13;
                        Note("status=original_human_attack_next_enemy" +
                             "|consulted=" + Ally + "|target=" + qaEnemy +
                             "|native_team=" +
                                s.OriginalNativeTeams[Ally] +
                             "|existing_original_gameaction_only=true" +
                             "|accepted=false_until_original_post_and_native_tuple");
                        return;
                    }
                    Note("status=next_enemy_request_refused" +
                         "|reason=" + why);
                }
                Note("status=original_tribe_target_final_summary"+
                     "|new_targeted_groups="+NewEnemyTargetsMax+
                     "|current_targeted_groups="+AllTargetsMax+
                     "|same_original_groups="+SameIdsMax+
                     "|changed_original_objectives="+ObjectivesChangedMax+
                     "|no_causal_ack_or_arrival_claim=true");
                Note("status=original_military_unit_tracking_summary" +
                     "|max_original_ai_units_moved=" + MovedCount +
                     "|same_original_unit_ids_tracked="+MatchedCount+
                     "|causal_ack_claimed=false|troop_arrival_claimed=false");
                Note("status=original_military_routes_finished" +
                     "|positive_original_native_execution=" + NativeActiveSeen +
                     "|real_original_post_observed=" + NativeDecisionSeen +
                     "|original_map_generation_unchanged=true" +
                     "|new_multiplayer_lobby_not_opened=true" +
                     "|fake_ai_acceptance=false");
                StopAiMilitaryQa(NativeActiveSeen,
                    NativeActiveSeen ?
                    "real_original_evaluator_accepted_and_new_playstate_order" :
                    "no_original_accepted_and_executed_order");
                return;
            }

            if (stage == 20)
            {
                if (s.Generation <= OldGeneration)
                {
                    RestartWait("actual_snapshot_still_previous_generation");
                    return;
                }

                if (s.Local != 1 || !s.Humans.SequenceEqual(new[] { 1 }) || !s.Ai.SequenceEqual(new[] { 2, 3 }) || !s.Alliances[2, 3] || !s.Alliances[3, 2] || s.Alliances[1, 2] || s.Alliances[2, 1] || s.Alliances[1, 3] || s.Alliances[3, 1])
                {
                    StopAiMilitaryQa(false, "original_restarted_human_ai_roles_or_mutual_alliances_not_verified");
                    return;
                }

                if (s.Ai.Any(p => s.Military[p] != null && (s.Military[p].Queued || s.Military[p].InFlight || s.Military[p].OriginalHumanRetained)))
                {
                    StopAiMilitaryQa(false, "old_human_military_input_survived_actual_new_map");
                    return;
                }

                Note("status=original_fresh_ai_scenario_verified|old_generation=" + OldGeneration + "|generation=" + s.Generation + "|local_human=1|mutually_allied_ai=2,3|human_opposes_ai_2_and_3=true" + "|original_briefing_resumed=" + RestartBriefingResumed + "|old_human_orders_retained=false");
                if (!Save("NativeAiAidBaseline.sav"))
                    return;
                // Save() owns a full native+Director pause. Enabling
                // relief here left the simulation paused for the entire
                // stage-21 wait. Release that save-owned pause FIRST.
                AidWarmGeneration = s.Generation;
                stage = 34;
                Note("status=original_ai_baseline_saved_waiting_for_native_unpause" +
                     "|generation=" + s.Generation + "|aid_enabled=false");
                return;
            }

            if (stage == 34)
            {
                if (s.Generation != AidWarmGeneration)
                {
                    StopAiMilitaryQa(false,
                        "original_generation_changed_after_aid_baseline_save");
                    return;
                }

                if (savePauseOwned)
                {
                    FinishSave();
                    if (savePauseOwned || savePauseRestorePending)
                        return;
                }

                bool wasPaused;
                if (!DiplomacyFeature.SetSoloTestPaused(false, out wasPaused))
                {
                    AidLifecycleWait("original_aid_resume_wait",
                        "original_native_pause_boundary_not_available", s);
                    return;
                }

                if (Director.instance == null)
                {
                    AidLifecycleWait("original_aid_resume_wait",
                        "original_director_not_available", s);
                    return;
                }

                Director.instance.SetPausedState(false);
                AidWarmStartTick = s.Tick;
                AidWarmOriginalDay = s.Day;
                AidWarmStarted = Time.realtimeSinceStartup;
                NextAidWaitNote = 0f;
                Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "1");
                stage = 21;
                Note("status=original_ai_aid_started_after_save_pause_released" +
                     "|generation=" + s.Generation + "|original_day=" + s.Day +
                     "|simulation_paused=false|native_inventory_altered=false");
                return;
            }

            if (stage == 21)
            {
                if (s.Aid == null)
                {
                    AidLifecycleWait("original_ai_aid_waiting",
                        "no_original_native_gift_receipt_yet", s);
                    return;
                }

                if (!ObservedCurrentNativeAiGift(s, out why))
                {
                    StopAiMilitaryQa(false,
                        "original_native_gift_or_journal_unverified:" + why);
                    return;
                }
                bool previous;
                if (!DiplomacyFeature.SetSoloTestPaused(true, out previous))
                    return;
                nativeTradePauseOwned = true;
                Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "0");
                stage = 22;
                Note("status=actual_native_ai_aid_original_simulation_paused|id=" + s.Aid.Id);
                return;
            }

            if (stage == 22)
            {
                if (!Save("AfterNativeAiAid.sav"))
                    return;
                if (!DiplomacyFeature.WriteAidColdExpectation("AfterNativeAiAid.sav", out why))
                {
                    StopAiMilitaryQa(false, why);
                    return;
                }

                StopAiMilitaryQa(true, "actual_original_ai_aid_and_saved_expectation_ready");
                return;
            }

            if (stage == 70)
            {
                if (!DiplomacyFeature.VerifyColdAid(true, out why))
                {
                    // These are normal readiness states,
                    // not contradicted native provenance. Retry within the
                    // immutable monotonic cold budget, never grant a Gift.
                    if (why == "awaiting_original_durable_aid_receipt" ||
                        why == "engine_busy" || why == "state_busy" ||
                        why == "map_not_ready")
                        AidLifecycleWait("cold_ai_aid_reattest_wait", why, s);
                    else
                        StopAiMilitaryQa(false, why);
                    return;
                }

                stage = 72;
                return;
            }

            if (stage == 72)
            {
                // One and ONLY one strict, coherent original cold save
                // proves the loaded generation. Repeating Save() every time
                // the authority check reports not-yet-ready could overwrite
                // its first complete witness and hold simulation paused.
                if (!ColdAuthorityBaselineSaved)
                {
                    if (!Save("ColdAiReplayBaseline.sav"))
                        return;
                    ColdAuthorityBaselineSaved = true;
                    Note("status=cold_exact_generation_baseline_saved_once" +
                         "|generation=" + s.Generation + "|no_replay_claim=true");
                }

                if (!DiplomacyFeature.ReadActualNativeEligibility(out why))
                {
                    AidLifecycleWait("cold_actual_native_authority_wait", why, s);
                    return;
                }

                Note("status=cold_actual_native_inventory_authority_verified" +
                     "|method=unchanged_production_contract" +
                     "|save_pause_still_owned=true");
                stage = 73;
                return;
            }

            if (stage == 73)
            {
                // A successful baseline Save still owns the native/Director
                // pause. The post-cold ten-second comparison only has meaning
                // after the ORIGINAL process actually resumes its simulation.
                if (savePauseOwned)
                {
                    FinishSave();
                    if (savePauseOwned || savePauseRestorePending)
                        return;
                }

                bool wasPaused;
                if (!DiplomacyFeature.SetSoloTestPaused(false, out wasPaused))
                {
                    AidLifecycleWait("cold_original_simulation_resume_wait",
                        "strict_original_pause_not_released", s);
                    return;
                }

                if (Director.instance == null)
                {
                    AidLifecycleWait("cold_original_simulation_resume_wait",
                        "original_director_unavailable", s);
                    return;
                }

                Director.instance.SetPausedState(false);
                Environment.SetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID", "1");
                ReplayStarted = Time.realtimeSinceStartup;
                ReplayStartTick = s.Tick;
                stage = 71;
                Note("status=cold_actual_original_simulation_resumed" +
                     "|generation=" + s.Generation +
                     "|native_pause_cleared=true" +
                     "|historical_gift_replay_claimed=false");
                return;
            }

            if (stage == 71)
            {
                if (Time.realtimeSinceStartup - ReplayStarted < 10)
                    return;
                if (s.Tick <= ReplayStartTick)
                {
                    AidLifecycleWait("cold_original_simulation_progress_wait",
                        "original_tick_not_advanced", s);
                    return;
                }
                bool verified = DiplomacyFeature.VerifyColdAid(false, out why);
                if (!verified && (why == "engine_busy" ||
                    why == "state_busy" || why == "map_not_ready" ||
                    why == "awaiting_original_durable_aid_receipt"))
                {
                    AidLifecycleWait("cold_after_original_pump_verification_wait",
                        why, s);
                    return;
                }

                if (verified)
                    Note("status=cold_original_simulation_progress" +
                         "|start_tick=" + ReplayStartTick + "|actual_tick=" + s.Tick);
                StopAiMilitaryQa(verified, why);
                return;
            }
        }
    }

    // Read-only pause probe for stage 20. Reuses the same original-monitor and
    // one-human/no-remote-peer predicates as SetSoloTestPaused; this does
    // not grant native inventory authority or replace the original resume.
    internal sealed partial class DiplomacyFeature
    {
        internal static bool TryReadOriginalRestartSoloPause(out bool paused, out string reason)
        {
            paused = false;
            reason = "private_restart_scope_absent";
            string scopeReason;
            if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" || Environment.GetEnvironmentVariable("DIPLOMACY_AI_MILITARY_TEST") != "1" || !DisposableGameScope.IsEnabled(out scopeReason))
                return false;
            try
            {
                if (!IsSoloEngineSession(out reason))
                    return false;
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "original_monitor_busy";
                    return false;
                }

                using (lease)
                {
                    if (!IsSoloEngineSession(out reason))
                        return false;
                    var api = SHCDESE.API.GamePlayerManagerAPI.Instance;
                    if (api == null)
                    {
                        reason = "original_player_manager_unavailable";
                        return false;
                    }

                    paused = api.IsLocalPaused();
                    reason = "original_local_pause_observed_under_solo_monitor";
                    return true;
                }
            }
            catch (Exception e)
            {
                reason = "original_restart_pause_probe_" + e.GetType().Name;
                return false;
            }
        }
    }
}
#endif
