// Explicit private, file-commanded main-thread driver. Never part of release.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Diagnostics;
using CrusaderDE;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    // Pure deadline parsing. The operator supplies an absolute UTC deadline;
    // the driver converts its remaining delay once to Unity's monotonic clock.
    // A test budget never participates in game or inventory authorization.
    internal static class HeadlessQaDeadline
    {
        internal const long MaximumHorizonMilliseconds = 1800000;

        // A short-lived collision with the SAME original
        // EngineInterface.threadLock is a WAIT, not evidence that a
        // verified original one-human Skirmish suddenly became multiplayer.
        // No alternative reason qualifies, and this never authorizes a
        // native transfer. The existing outer deadline remains mandatory.
        internal static bool IsTransientSoloMonitorObservation(string reason)
        {
            return string.Equals(reason,
                "session_engine_monitor_busy", StringComparison.Ordinal);
        }
        internal static bool TryGetDelayMilliseconds(string text, long nowUnixMilliseconds, out long delay)
        {
            delay = -1;
            if (string.IsNullOrEmpty(text))
                return true;
            long deadline;
            if (nowUnixMilliseconds <= 0 ||
                !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out deadline) ||
                deadline <= 0 ||
                (deadline > nowUnixMilliseconds && deadline - nowUnixMilliseconds > MaximumHorizonMilliseconds))
                return false;
            delay = deadline <= nowUnixMilliseconds ? 0 : deadline - nowUnixMilliseconds;
            return true;
        }
    }

    internal static partial class HeadlessQaDriver
    {
        private static int stage;
        // A separate read-only diagnostic, never an AI/military test or
        // original warm/cold success. Not compiled in ordinary profiles.
        private static bool CpuTribeReadOnlyAction(string value)
        {
#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
            return value == "LOAD_CPU_TRIBE_OBSERVE";
#else
            return false;
#endif
        }
        private const string AutonomousMilitaryAction = "LOAD_AUTONOMOUS_MILITARY";
        private static float started, loaded;
        private static bool operatorDeadlineInitialized, operatorStopRequested;
        private static float operatorStopAt = -1f, operatorStopRequestedAt;
        // Diagnostic scheduling is never readiness scheduling. A changing
        // solo/engine_busy observation cannot emit more than once per second.
        private const float LoadedSessionMinimumNoteSeconds = 1f;
        private const float LoadedSessionUnchangedNoteSeconds = 12f;
        private static float lastLoadedSessionNoteAt = -1f;
        private static string lastLoadedSessionNote;
        private static bool soloAdmissionLogged;
        private static bool ShouldNoteLoadedSession(float now, string observation)
        {
            if (float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
                return false;
            // Apply the hard floor BEFORE looking at the changed payload:
            // alternating true/busy frames never bypass this guard.
            if (lastLoadedSessionNoteAt >= 0f &&
                now - lastLoadedSessionNoteAt < LoadedSessionMinimumNoteSeconds)
                return false;
            if (lastLoadedSessionNoteAt >= 0f &&
                string.Equals(lastLoadedSessionNote, observation, StringComparison.Ordinal) &&
                now - lastLoadedSessionNoteAt < LoadedSessionUnchangedNoteSeconds)
                return false;
            lastLoadedSessionNoteAt = now;
            lastLoadedSessionNote = observation;
            return true;
        }
        private static string action, filename;
        // original map generation: each verified original SaveData/ABI proof is consumed once,
        // never re-emitted every Unity tick during an AI observer wait.
        private static bool _originalSaveReadbackDone, _originalSaveOnlyDone, _originalRuntimeObservationDone;
        private static long _originalMapGeneration = -1;
        private static bool savePauseOwned, saveWasPaused, saveNativeWasPaused, savePauseRestorePending;
        private static bool coldNativePauseArmed;
        private static bool ArmColdNativePause()
        {
            if (coldNativePauseArmed)
                return true;
            string path = Path.Combine(DisposableGameScope.ProfileRoot, "Saves", filename);
            if (!DiplomacyFeature.PauseExactPrivateColdLoad(path))
                return false;
            // The original Director still needs to collect the first paused PlayState
            // and initialize the restored scene. Pausing the Director before that
            // snapshot deadlocks bootstrap; the original native simulation stays paused.
            typeof(Director).GetField("paused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SetValue(Director.instance, false);
            coldNativePauseArmed = true;
            Note("status=exact_cold_save_native_paused_before_snapshot");
            return true;
        }

        internal static bool PrepareSave()
        {
            if (savePauseRestorePending)
                return false;
            var director = Director.instance;
            if (director == null)
                return false;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            if (!savePauseOwned)
            {
                saveWasPaused = (bool)typeof(Director).GetField("paused", flags).GetValue(director);
                if (!DiplomacyFeature.SetSoloTestPaused(true, out saveNativeWasPaused))
                    return false;
                typeof(Director).GetField("paused", flags).SetValue(director, true);
                savePauseOwned = true;
                return false;
            }

            return !(bool)typeof(Director).GetField("InEngine", flags).GetValue(director) && (int)typeof(Director).GetField("FramesToProcess", flags).GetValue(director) == 0;
        }

        internal static void FinishSave()
        {
            if (!savePauseOwned)
                return;
            bool ignored;
            if (!DiplomacyFeature.SetSoloTestPaused(saveNativeWasPaused, out ignored))
            {
                if (!savePauseRestorePending)
                    Note("status=save_pause_restore_waiting|reason=original_engine_busy");
                savePauseRestorePending = true;
                return;
            }

            savePauseOwned = false;
            savePauseRestorePending = false;
            if (Director.instance != null)
                typeof(Director).GetField("paused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SetValue(Director.instance, saveWasPaused);
        }

        private static string Root
        {
            get
            {
                return Path.GetDirectoryName(DisposableGameScope.ProfileRoot);
            }
        }

        internal static void Note(string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(Root, "qa", "HeadlessDriver.log"), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|pid=" + Process.GetCurrentProcess().Id + "|" + text + Environment.NewLine);
            }
            catch
            {
            }
        }


        // interactive gui load: In a PRIVATE visible GUI load a SHA-pinned ORIGINAL SOLO
        // game via the SAME original FrontendMenus callback used by proven
        // headless military qa/military order source tests. This is not an alternative native reader,
        // simulated human or invented offline/Steam identity. A real active
        // multiplayer lobby is never overridden. No Headless QA driver.
        // original team alliance: only the engine may clear an active MP lobby.
        // An early menu-lobby reading is NOT permanent proof of multiplayer.
        // Bounded retry does not authorize any native game activity. At the
        // deadline only a clear error is reported, never a fake solo world.
        private static bool LobbyWaitOrStop(
            bool actualLobby, float now, float firstLobby, float horizon)
        {
            if (!actualLobby)
                return false;
            return firstLobby >= 0f && now >= firstLobby &&
                now - firstLobby >= horizon;
        }
        private static float _lobbyObservedFirstAt = -1f;
        private static float _lobbyLastNoteAt = -1f;

        private static int _guiLoadState;
        private static float _guiLoadedAt, _guiLastObservationAt;
        private static void NoteGui(string msg)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(DisposableGameScope.ProfileRoot,
                        "OriginalGuiDiplomacyActivation.log"),
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) +
                    "|status=" + msg + Environment.NewLine);
            }
            catch {}
        }
        private static void TickRealInteractiveGuiLoad()
        {
            string requested = Environment.GetEnvironmentVariable(
                "DIPLOMACY_PRIVATE_GUI_AUTOLOAD_SAVE");
            if (requested != "MilitaryBaseline.sav" &&
                requested != "AiAidBaseline.sav")
                return;
            if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") == "1" ||
                _guiLoadState >= 4)
                return;
            try
            {
                if (_guiLoadState == 0)
                {
                    if (Time.realtimeSinceStartup < 50f ||
                        Director.instance == null ||
                        EditorDirector.instance == null ||
                        MainViewModel.Instance == null ||
                        MainViewModel.Instance.HUDIngameMenu == null)
                        return;
                    string reason;
                    if (!DisposableGameScope.IsEnabled(out reason))
                    {
                        NoteGui("blocked_disposable_scope_unverified|reason=" + reason);
                        _guiLoadState = 4;
                        return;
                    }
                    bool actualLobby =
                        SHCDESE.API.GameNetworkAPI.IsLobbyEnvironment();
                    if (actualLobby)
                    {
                        // No original frontend callback is ever called
                        // while real game network code reports any lobby.
                        if (_lobbyObservedFirstAt < 0f)
                            _lobbyObservedFirstAt =
                                Time.realtimeSinceStartup;
                        if (_lobbyLastNoteAt < 0f ||
                            Time.realtimeSinceStartup -
                                _lobbyLastNoteAt >= 15f)
                        {
                            _lobbyLastNoteAt =
                                Time.realtimeSinceStartup;
                            NoteGui(
                                "original_multiplayer_lobby_real_wait" +
                                "|no_original_save_loaded" +
                                "|native_goods_and_steam_denied=true");
                        }
                        if (LobbyWaitOrStop(
                                true,Time.realtimeSinceStartup,
                                _lobbyObservedFirstAt,160f))
                        {
                            NoteGui(
                                "original_lobby_remained_active_160s" +
                                "|GUI_NOT_ACTIVE_START_REAL_SOLO_SESSION" +
                                "|no_gameplay_or_release_approval_claimed=true");
                            _guiLoadState = 4;
                        }
                        return;
                    }
                    if (_lobbyObservedFirstAt >= 0f)
                    {
                        NoteGui(
                           "lobby_cleared_by_original_game" +
                           "|now_real_frontend_callback_may_load_once");
                        _lobbyObservedFirstAt = -1f;
                    }
                    string path = Path.Combine(
                        DisposableGameScope.ProfileRoot,"Saves",requested);
                    string expect = requested == "MilitaryBaseline.sav"
                        ? "fc8b087afe3e0c03565216e0a745f3f599ea931a8ab0b094aada8541ff74399f"
                        : "e04f412b55ee8f43652fb09843ab38f1f07c4967c9a1f60f384af1cfbcb39a6a";
                    if (!File.Exists(path) ||
                        !String.Equals(DisposableGameScope.HashFile(path),
                            expect,StringComparison.OrdinalIgnoreCase))
                    {
                        NoteGui("blocked_original_save_file_SHA_unverified");
                        _guiLoadState = 4;
                        return;
                    }
                    var header = MapFileManager.Instance
                        .GetFileInfoFromFileName(path,path,0,true);
                    if (header == null || !header.skirmishMap)
                    {
                        NoteGui("blocked_original_skirmish_save_header_missing");
                        _guiLoadState = 4;
                        return;
                    }
                    var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static;
                    var callbacks = typeof(FrontendMenus).GetNestedTypes(flags)
                        .Where(t => t.GetMethod("<ButtonClicked>b__73_11",
                                               flags) != null).ToArray();
                    if (callbacks.Length != 1)
                    {
                        NoteGui("blocked_original_frontend_callback_binding_changed");
                        _guiLoadState = 4;
                        return;
                    }
                    var target = callbacks[0].GetField("<>9", flags)
                        .GetValue(null);
                    NoteGui("original_frontend_singleplayer_save_load_begin" +
                        "|save=" + requested + "|no_native_goods_written=true");
                    // Consume ONCE even if the ORIGINAL frontend throws:
                    // never replay an unknown map-load outcome.
                    _guiLoadState = 1;
                    _guiLoadedAt = Time.realtimeSinceStartup;
                    callbacks[0].GetMethod("<ButtonClicked>b__73_11", flags)
                        .Invoke(target,new object[] {path,header});
                    NoteGui("original_frontend_singleplayer_save_load_returned" +
                        "|save=" + requested);
                    _guiLoadState = 2;
                    return;
                }
                if (_guiLoadState == 2 &&
                    Time.realtimeSinceStartup - _guiLoadedAt >= 10f &&
                    Time.realtimeSinceStartup - _guiLastObservationAt >= 3f)
                {
                    _guiLastObservationAt = Time.realtimeSinceStartup;
                    bool active,disabled;
                    int qaStage;
                    bool observed = DiplomacyFeature.ReadQaState(
                        out active,out disabled,out qaStage);
                    string reason = "no_original_game_world";
                    bool solo = false;
                    if (GameData.Instance != null &&
                        GameData.Instance.lastGameState != null)
                        solo = DiplomacyFeature.IsSoloEngineSession(out reason);
                    bool liveLobby=SHCDESE.API.GameNetworkAPI.IsLobbyEnvironment();
                    NoteGui("post_original_frontend_load_observed"+
                        "|observed=" + observed + "|active=" + active +
                        "|disabled=" + disabled + "|solo=" + solo +
                        "|reason=" + reason + "|actual_lobby=" + liveLobby +
                        "|qa_stage=" + qaStage);
                    if (observed && active && !disabled && solo && !liveLobby)
                    {
                        // Native map/solo activation is NOT proof that
                        // Noesis rendered even one diplomacy element.
                        NoteGui("DIPLOMACY_GUI_MAP_ACTIVE_NOT_YET_HUD_VISIBLE" +
                            "|real_allies_window_readback_required=true" +
                            "|manual_click_and_fuerstenchat_unproven=true");
                        _guiLoadState = 3;
                        bool popup = DiplomacyFeature.ShowOriginalDiplomacyActivationPopup();
                        NoteGui(popup
                            ? "original_gui_HUD_popup_method_invoked"
                            : "original_gui_HUD_popup_not_proven");
                        return;
                    }
                    if (observed && disabled)
                    {
                        NoteGui("DIPLOMACY_GUI_INACTIVE_ORIGINAL_MAP_REFUSED"+
                            "|never_enable_goods_or_MP_on_unverified_lobby=true");
                        _guiLoadState = 4;
                        return;
                    }
                    if (Time.realtimeSinceStartup - _guiLoadedAt > 80f)
                    {
                        NoteGui("DIPLOMACY_GUI_ACTIVATION_TIMEOUT"+
                            "|no_gameplay_approval_claimed=true");
                        _guiLoadState = 4;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                NoteGui("DIPLOMACY_GUI_LOAD_FAILED" +
                    "|exception=" + ex.GetType().Name +
                    "|detail=" + ex.Message.Replace('|','_'));
                _guiLoadState = 4;
            }
        }

        internal static void Tick()
        {
            TickRealInteractiveGuiLoad();
            if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" || stage >= 90)
                return;
            try
            {
                if (!operatorDeadlineInitialized)
                {
                    long delay;
                    if (!HeadlessQaDeadline.TryGetDelayMilliseconds(
                        Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_STOP_UNIX_MS"),
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), out delay))
                    {
                        Note("status=blocked|reason=invalid_operator_stop_deadline");
                        stage = 90;
                        Application.Quit();
                        return;
                    }
                    operatorStopAt = delay < 0 ? -1f : Time.realtimeSinceStartup + delay / 1000f;
                    operatorDeadlineInitialized = true;
                }
                if (!operatorStopRequested && operatorStopAt >= 0 && Time.realtimeSinceStartup >= operatorStopAt)
                {
                    operatorStopRequested = true;
                    operatorStopRequestedAt = Time.realtimeSinceStartup;
                    // A late cleanup timeout overrides an earlier scenario success.
                    // Do not emit a second AI terminal receipt while already closing.
                    Note("status=operator_deadline_abort|native_scenario_success=false|stage=" + stage);
                    if (stage != 4 && AiMilitaryQaAction(action))
                    {
                        StopAiMilitaryQa(false, "operator_time_budget_exhausted_stage_" + stage);
                        return;
                    }
                    Note("status=blocked|reason=operator_time_budget_exhausted|stage=" + stage);
                    stage = 4;
                }
                // The ordinary shutdown releases owned test pauses. A blocked
                // engine lease must not leave a disposable process running forever.
                if (operatorStopRequested && Time.realtimeSinceStartup - operatorStopRequestedAt >= 5f)
                {
#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                    if (CpuTribeReadOnlyAction(action))
                        DiplomacyFeature.StopOriginalCpuTribeReadOnly("operator_grace_exhausted");
#endif
                    Note("status=operator_shutdown_grace_exhausted|native_scenario_success=false");
                    stage = 90;
                    Application.Quit();
                    return;
                }
                if (savePauseRestorePending)
                {
                    FinishSave();
                    if (savePauseRestorePending)
                        return;
                }

                if (started == 0)
                    started = Time.realtimeSinceStartup;
                // One bounded, honest original AI-aid warm trial. Its
                // exact save and later cold run are separate processes; this
                // budget never grants a trade, goodwill or Gift.
                if (Time.realtimeSinceStartup - started > (action == "LOAD_AI_MILITARY" ? 900 :
                    action == "LOAD_AI_AID_WARM" ? 300 : action == "LOAD_TRADE" ? 600 : 240))
                {
                    if (AiMilitaryQaAction(action))
                    {
                        StopAiMilitaryQa(false, "driver_timeout_stage_" + stage);
                        return;
                    }

#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                    if (CpuTribeReadOnlyAction(action))
                    {
                        DiplomacyFeature.StopOriginalCpuTribeReadOnly("driver_timeout");
                        stage = 4;
                        return;
                    }
#endif
                    Note("status=blocked|reason=driver_timeout|stage=" + stage);
                    stage = 90;
                    Application.Quit();
                    return;
                }

#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                // Pure observation window; no gameplay operation or military sender.
                if (stage == 12 && CpuTribeReadOnlyAction(action))
                {
                    if (Time.realtimeSinceStartup - loaded >= 36f)
                    {
                        DiplomacyFeature.StopOriginalCpuTribeReadOnly("observer_wall_clock_budget");
                        stage = 4;
                    }
                    else
                        return;
                }
#endif
#if HOST_DIRECTED_AI_TRIBE_CANARY
                if (stage == 13 && action == AutonomousMilitaryAction)
                {
                    if (Time.realtimeSinceStartup - loaded < 36f)
                        return;
                    string observation;
                    int attempts, approvals;
                    if (!DiplomacyFeature.TryReadAutonomousMilitaryObservation(out observation,
                            out attempts, out approvals))
                        return;
                    if (attempts == 0 && DiplomacyFeature.VerifyMilitaryTransport &&
                        Time.realtimeSinceStartup - loaded < 110f)
                        return;
                    if (approvals > 0 && DiplomacyFeature.VerifyMilitaryTransport &&
                        Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_SAVE_ONLY") == "1")
                    {
                        bool saved;
                        string evidence;
                        if (!DiplomacyFeature.TryOriginalSaveOnly(out saved, out evidence) || !saved)
                            return;
                        Note(evidence ?? "status=military_save_evidence_missing");
                    }
                    Note("status=autonomous_military_observation_finished|" + observation);
                    stage = 4;
                    return;
                }
#endif
                if (stage >= 10 && stage < 90 && AiMilitaryQaAction(action))
                {
                    TickAiMilitaryQa();
                    return;
                }

                if (stage == 0)
                {
                    string path = Path.Combine(Root, "qa", "HeadlessCommand.txt");
                    if (!File.Exists(path))
                        return;
                    string[] command = File.ReadAllText(path).Trim().Split('|');
                    if (command.Length != 2 || (command[0] != "LOAD_PROBE" && command[0] != "LOAD_TRADE" && command[0] != "LOAD_VERIFY" && command[0] != AutonomousMilitaryAction && !AiMilitaryQaAction(command[0]) && !CpuTribeReadOnlyAction(command[0])) || Path.GetFileName(command[1]) != command[1] || !command[1].EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid explicit private QA command");
                    action = command[0];
                    filename = command[1];
                    _originalSaveReadbackDone = false;
                    _originalSaveOnlyDone = false;
                    _originalRuntimeObservationDone = false;
                    _originalMapGeneration = -1;
                    lastLoadedSessionNoteAt = -1f;
                    lastLoadedSessionNote = null;
                    soloAdmissionLogged = false;
                    // Batchmode has no physical Keyboard/Mouse. Register inert virtual
                    // devices so the game's input readers can initialize; inject no events.
                    var input = System.Reflection.Assembly.Load("Unity.InputSystem");
                    var system = input.GetType("UnityEngine.InputSystem.InputSystem", true);
                    foreach (string layout in new[]
                    {
                        "Keyboard",
                        "Mouse"
                    }

                    )
                    {
                        var device = input.GetType("UnityEngine.InputSystem." + layout, true);
                        if (device.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null, null) == null)
                            system.GetMethod("AddDevice", new[] { typeof(string), typeof(string), typeof(string) }).Invoke(null, new object[] { layout, "Inert" + layout, null });
                    }

                    typeof(IntroSequence).GetField("forceSkipIntro", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, true);
                    Note("status=batch_inert_devices_ready|input_events_injected=0");
                    File.Move(path, path + "." + DateTime.UtcNow.Ticks + ".consumed");
                    Note("status=explicit_command_consumed|action=" + action + "|save=" + filename);
                    stage = 1;
                }

                if (stage == 1)
                {
                    if (Time.realtimeSinceStartup < 45 || Director.instance == null || EditorDirector.instance == null || MainViewModel.Instance == null || MainViewModel.Instance.HUDIngameMenu == null)
                        return;
                    string scope;
                    if (!DisposableGameScope.IsEnabled(out scope))
                        return;
                    string path = Path.Combine(DisposableGameScope.ProfileRoot, "Saves", filename);
                    var header = MapFileManager.Instance.GetFileInfoFromFileName(path, path, 0, true);
                    if (header == null)
                        throw new InvalidDataException("Original save header absent");
                    Note("status=original_load_begin|sha256=" + DisposableGameScope.HashFile(path));
                    stage = 2;
                    loaded = Time.realtimeSinceStartup;
                    if (AnyColdAction)
                        InstallAtomicColdLoadPause();
                    // Invoke the original frontend's selected-save callback. It owns scene
                    // initialization, simulation stop, native load, and post-load setup.
                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                    var callbacks = typeof(FrontendMenus).GetNestedTypes(flags).Where(t => t.GetMethod("<ButtonClicked>b__73_11", flags) != null).ToArray();
                    if (callbacks.Length != 1)
                        throw new MissingMethodException("Original frontend load callback changed");
                    var target = callbacks[0].GetField("<>9", flags).GetValue(null);
                    callbacks[0].GetMethod("<ButtonClicked>b__73_11", flags).Invoke(target, new object[] { path, header });
                    if (AnyColdAction)
                    {
                        ArmColdNativePause();
                    }

                    Note("status=original_load_returned");
                    return;
                }

                if (stage == 2)
                {
                    if (AnyColdAction && !ArmColdNativePause())
                        return;
                    var game = GameData.Instance;
                    if (game == null || game.lastGameState == null || Time.realtimeSinceStartup - loaded < 10)
                        return;
                    string reason;
                    bool solo = DiplomacyFeature.IsSoloEngineSession(out reason);
                    float observationNow = Time.realtimeSinceStartup;
                    // The note is diagnostic only. Poll the real session and
                    // original load gate on EVERY Tick, including suppressed
                    // notes. Build the expensive payload at most once/second.
                    if (lastLoadedSessionNoteAt < 0f ||
                        observationNow - lastLoadedSessionNoteAt >= LoadedSessionMinimumNoteSeconds)
                    {
                        string observation = "status=loaded_session_observed|solo=" + solo + "|reason=" + reason + "|map=" + game.currentMapName + "|human_slots=" + string.Join(",", Enumerable.Range(1, 8).Where(id => game.lastGameState.is_valid_player(id))) + "|ai_slots=" + string.Join(",", Enumerable.Range(1, 8).Where(id => game.lastGameState.is_skirmish_player(id))) + "|networked=" + SHCDESE.API.GameNetworkAPI.IsNetworkedEnvironment() + "|local_host=" + SHCDESE.API.GameNetworkAPI.IsLocalHost();
                        if (ShouldNoteLoadedSession(observationNow, observation))
                            Note(observation);
                    }
#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                    if (CpuTribeReadOnlyAction(action))
                    {
                        string probeReason;
                        if (!DiplomacyFeature.TryStartOriginalCpuTribeReadOnly(
                                out probeReason))
                        {
                            Note("status=original_cpu_tribe_observer_unavailable|reason=" +
                                probeReason + "|AI_caller_proven=false|queue_proven=false");
                            stage = 4;
                            return;
                        }
                        Note("status=original_cpu_tribe_observer_started" +
                            "|read_only=true|original_caller_RVA=unknown" +
                            "|no_native_order_calls=true|model_restore_not_attested=true");
                        stage = 12;
                        return;
                    }
#endif
                    // Observe only the original producer's read-only
                    // load-gate status. Unknown/uninitialized/engine-busy is
                    // a WAIT, never success. Known, fully applied but unable
                    // to bind is a single negative warm terminal; existing
                    // stage 4 then performs the ordinary original shutdown.
                    if (GenuineAidWarmAction)
                    {
                        bool observerInstalled;
                        string phaseOrReason;
                        if (!DiplomacyFeature.TryObserveOriginalLoadGate(
                            out observerInstalled, out phaseOrReason))
                            return;
                        if (!observerInstalled)
                        {
                            string detail = string.IsNullOrEmpty(phaseOrReason) ?
                                "unspecified_original_binding_failure" : phaseOrReason;
                            if (detail.Length > 160)
                                detail = detail.Substring(0, 160);
                            detail = detail.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
                            StopAiMilitaryQa(false, "original_load_gate_not_installed:" + detail);
                            return;
                        }
                    }
                    bool active, disabled;
                    int qaStage;
                    if (action == "LOAD_PROBE")
                    {
                        // original map generation is only used for the explicit private original mod ai
                        // detached observer. Never accept old successful
                        // prereqs from an earlier map/session generation.
                        if (Environment.GetEnvironmentVariable(
                            "DIPLOMACY_QA_ORIGINAL_MOD_AI_DRYRUN") == "1")
                        {
                            long generation;
                            if (!DiplomacyFeature.TryObservedMapGeneration(
                                    out generation))
                                return;
                            if (_originalMapGeneration < 0)
                                _originalMapGeneration = generation;
                            else if (_originalMapGeneration != generation)
                            {
                                Note("status=original_map_generation_changed" +
                                     "|no_previous_prerequisites_reused=true" +
                                     "|native_goods_writes=0");
                                stage = 90;
                                Application.Quit();
                                return;
                            }
                        }
                        // original runtime observation is an explicit, private, read-only original-game
                        // prerequisite snapshot, NOT a native trade permission.
                        // On engine-busy it waits; hard wall-clock deadlines
                        // remain enforced by the unchanged QA driver.
                        if (!_originalSaveReadbackDone &&
                            Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_SAVE_READBACK") == "1")
                        {
                            bool finished;
                            string report;
                            if (!DiplomacyFeature.TryQaOriginalNormalSave(out finished,out report))
                                return;
                            if (!finished || report == null ||
                                !report.Contains("status=normal_original_save_readback_verified"))
                            {
                                Note(report ?? "status=original_save_readback_refused");
                                stage=90;
                                Application.Quit();
                                return;
                            }
                            _originalSaveReadbackDone = true;
                            Note(report);
                        }
                        if (!_originalSaveOnlyDone &&
                            Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_SAVE_ONLY") == "1")
                        {
                            bool done;
                            string saved;
                            if (!DiplomacyFeature.TryOriginalSaveOnly(out done, out saved))
                                return;
                            if (!done || saved == null ||
                                !saved.Contains("status=original_save_verified"))
                            {
                                Note(saved ?? "status=original_save_only_refused");
                                stage = 90;
                                Application.Quit();
                                return;
                            }
                            _originalSaveOnlyDone = true;
                            Note(saved);
                        }
                        if (!_originalRuntimeObservationDone &&
                            Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_RUNTIME_READONLY") == "1")
                        {
                            string report;
                            if (!DiplomacyFeature.TryObserveOriginalReadOnly(out report))
                                return;
                            if (report == null || !report.Contains(
                                    "status=original_runtime_observed"))
                            {
                                Note(report ?? "status=original_runtime_abi_unavailable");
                                stage = 90;
                                Application.Quit();
                                return;
                            }
                            _originalRuntimeObservationDone = true;
                            Note(report);
                        }
                        // original mod ai: only when explicitly opted in inside disposable
                        // QA, evaluate genuine original AI data through one
                        // throwaway managed reducer. No original goods write,
                        // human click, gameplay owner or SaveData publication.
                        if (Environment.GetEnvironmentVariable(
                            "DIPLOMACY_QA_ORIGINAL_MOD_AI_DRYRUN")=="1")
                        {
                            bool finished;
                            string dryrun;
                            if (!DiplomacyFeature.TryReadOnlyOriginalModAiDraft(
                                    out finished,out dryrun))
                                return;
                            if (!finished) return;
                            Note(dryrun);
                        }
                        stage = 90;
                        Application.Quit();
                        return;
                    }

                    if (!DiplomacyFeature.ReadQaState(out active, out disabled, out qaStage))
                        return;
                    if (disabled)
                    {
                        Note("status=blocked|reason=map_disabled_after_load");
                        stage = 90;
                        Application.Quit();
                        return;
                    }

                    if (!active)
                        return;
                    if (!solo)
                    {
                        // The original simulation may temporarily own its monitor.
                        // Continue only after its actual solo roster is verified.
                        if (HeadlessQaDeadline.IsTransientSoloMonitorObservation(reason))
                            return;
                        Note("status=blocked|reason=solo_inventory_not_authorized");
                        stage = 90;
                        Application.Quit();
                        return;
                    }
                    if (!soloAdmissionLogged)
                    {
                        soloAdmissionLogged = true;
                        var state = game.lastGameState;
                        Note("status=original_solo_admitted|reason=" + reason +
                             "|human_slots=" + string.Join(",", Enumerable.Range(1, 8).Where(state.is_valid_player)) +
                             "|ai_slots=" + string.Join(",", Enumerable.Range(1, 8).Where(state.is_skirmish_player)));
                    }
#if HOST_DIRECTED_AI_TRIBE_CANARY
                    if (action == AutonomousMilitaryAction)
                    {
                        if (!DiplomacyFeature.PrivateMilitarySupportRequested)
                        {
                            Note("status=blocked|reason=private_autonomous_military_opt_in_required");
                            stage = 4;
                            return;
                        }
                        Director.instance.SetPausedState(false);
                        loaded = Time.realtimeSinceStartup;
                        stage = 13;
                        return;
                    }
#endif
                    if (AiMilitaryQaAction(action))
                    {
                        Begin();
                        return;
                    }

                    if (AnyColdAction)
                    {
                        string coldReason;
                        bool verified = DiplomacyFeature.VerifyColdExpectation(out coldReason);
                        if (!verified && (coldReason == "engine_busy" || coldReason == "state_busy" || coldReason == "map_not_ready"))
                            return;
                        Note("status=cold_verification_finished|passed=" + verified + "|reason=" + coldReason);
                        // Stop further simulation requests, then release only the test-owned
                        // native pause before the original shutdown joins its engine thread.
                        typeof(Director).GetField("paused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SetValue(Director.instance, true);
                        stage = 4;
                        return;
                    }

                    bool previousNativePause;
                    if (!DiplomacyFeature.SetSoloTestPaused(false, out previousNativePause))
                        return;
                    File.WriteAllText(Path.Combine(Root, "qa", "RUN_DISPOSABLE_NATIVE_QA.txt"), "RUN_ISOLATED_NATIVE_TEST");
                    Director.instance.SetPausedState(false);
                    stage = 3;
                    Note("status=production_native_qa_requested");
                    return;
                }

                if (stage == 3)
                {
                    bool active, disabled;
                    int qaStage;
                    if (!DiplomacyFeature.ReadQaState(out active, out disabled, out qaStage))
                        return;
                    if (disabled || qaStage >= 90)
                    {
                        Note("status=production_native_qa_finished|passed=" + (!disabled && qaStage == 100) + "|qa_stage=" + qaStage);
                        typeof(Director).GetField("paused", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SetValue(Director.instance, true);
                        stage = 4;
                        return;
                    }
                }

                if (stage == 4)
                {
#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                    if (CpuTribeReadOnlyAction(action))
                        DiplomacyFeature.StopOriginalCpuTribeReadOnly("observer_shutdown");
#endif
                    bool previous;
                    if ((coldNativePauseArmed || nativeTradePauseOwned || coldConnectionPauseOwned) && !DiplomacyFeature.SetSoloTestPaused(false, out previous))
                        return;
                    coldNativePauseArmed = false;
                    nativeTradePauseOwned = false;
                    coldConnectionPauseOwned = false;
                    Note("status=test_pause_released_for_original_shutdown");
                    stage = 90;
                    Application.Quit();
                }
            }
            catch (Exception e)
            {
                Note("status=driver_fault|detail=" + e);
#if ORIGINAL_CPU_TRIBE_ORDER_PROBE
                if (CpuTribeReadOnlyAction(action))
                    DiplomacyFeature.StopOriginalCpuTribeReadOnly("driver_fault");
#endif
                if (AiMilitaryQaAction(action))
                {
                    StopAiMilitaryQa(false, "driver_fault_" + e.GetType().Name);
                    return;
                }

                stage = 90;
                Application.Quit();
            }
        }
    }
}
#endif
