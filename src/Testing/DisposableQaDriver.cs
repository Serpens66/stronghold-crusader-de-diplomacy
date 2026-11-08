// Explicit disposable-run automation, never included in a normal gameplay build.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using StrongholdEuropeDE.TradeNegotiation;
using SHCDESE.API;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private long _qaNextFrame, _qaGeneration = -1;
        private int _qaStage, _qaAttempts, _qaOfferWaitAttempts;
        // Isolated private QA only; in the production DLL this file does not compile.
        // original save private on-screen mode: ORIGINAL save method, never a
        // synthetic SaveData receipt or native inventory grant.
        private long _autoSaveGeneration = -1;
        private long _autoSaveNextTick;
        private int _autoSaveAttempts;
        private bool _qaDeclineOnly;
        private long _qaDeclineColdReportedGeneration = -1;
        // Private headless-only test terms are captured ONCE when the
        // explicitly marked RUN_DISPOSABLE_NATIVE_QA request is consumed.
        // A Steam packet/UI cannot set them; no production build includes
        // this code, and the original AI still freely accepts/declines.
        private int _qaChosenOriginalAiSlot, _qaChosenRequestedAmount = 5;
        private ulong _qaRequest, _qaExpectedTrade;
        private ObservedNativeTrade _qaObservedTrade;
        private static string QaRoot
        {
            get
            {
                return Path.GetDirectoryName(DisposableGameScope.ProfileRoot) + "\\qa";
            }
        }

        private void QaNote(string message)
        {
            Log("qa_native_trade", message);
            try
            {
                File.AppendAllText(Path.Combine(QaRoot, "NativeTradeQa.log"), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|generation=" + _sessionGeneration + "|" + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private bool QaSave(string filename)
        {
            var type = typeof(CrusaderDE.MainViewModel).Assembly.GetType("EngineInterface", true);
            var method = type.GetMethod("SaveSaveGame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool) }, null);
            if (method == null)
                return false;
            string dir = Path.Combine(DisposableGameScope.ProfileRoot, "Saves");
            Directory.CreateDirectory(dir);
            // If the original Director.SafeToSave gate refuses, Harmony never enters
            // SavePrefix. Keep that reason distinct from a failed save preflight.
            _savePreflightReason = "original_managed_save_prefix_not_called";
            if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") == "1" && !HeadlessQaDriver.PrepareSave())
                return false;
            try
            {
                return (bool)method.Invoke(null, new object[] { Path.Combine(dir, filename), 0, 0, 0, 0, false, false, false });
            }
            finally
            {
                HeadlessQaDriver.FinishSave();
            }
        }

        private void TryInteractiveOriginalBaselineSave()
        {
            // Only the fully isolated, unchanged verified game EXE and its
            // exact original profile. The normal release excludes this code.
            if (Environment.GetEnvironmentVariable(
                    "DIPLOMACY_INTERACTIVE_AUTO_BASELINE_SAVE") != "1" ||
                Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") == "1" ||
                _coherentSaveGeneration == _sessionGeneration)
                return;
            if (_autoSaveGeneration != _sessionGeneration)
            {
                _autoSaveGeneration = _sessionGeneration;
                _autoSaveAttempts = 0;
                // Do not save on the first map frame or while the original
                // post-loading callbacks are still initializing the engine.
                _autoSaveNextTick = _featureTicks + 240;
                QaNote("status=original_GUI_autosave_waiting_for_stable_map" +
                       "|fresh_generation=true|no_native_writes=true");
            }
            if (_autoSaveAttempts >= 8 ||
                _featureTicks < _autoSaveNextTick)
                return;
            _autoSaveNextTick = _featureTicks + 720;
            if (!_saveBridgeInstalled || !_saveHandlerRegistered ||
                _saveSuppressed || _saveFailurePending != null ||
                _inventoryRestorePending || _tradeFaulted ||
                _pendingPackage != null || _nativeTransferProbe != null ||
                _pendingTeamTransition != null)
                return;
            // Absolutely no actual MP lobby, unknown second human or
            // fabricated roles. An original solo game may have an underlying
            // networked transport; its REAL player roster must prove solo.
            string reason;
            if (GameNetworkAPI.IsLobbyEnvironment() ||
                !IsSoloEngineSession(out reason) ||
                !NativeInventoryContract.IsStrictSoloHost(out reason))
                return;
            ++_autoSaveAttempts;
            bool issued = QaSave("InteractiveBaseline.sav");
            bool verified = issued &&
                _coherentSaveGeneration == _sessionGeneration;
            QaNote("status=original_GUI_auto_baseline_save" +
                "|attempt=" + _autoSaveAttempts +
                "|max_attempts=8|original_method_returned=" + issued +
                "|exact_SaveData_receipt_verified=" + verified +
                "|native_goods_write_attempts=0");
            if (_autoSaveAttempts == 8 && !verified)
                QaNote("status=original_GUI_auto_baseline_unavailable" +
                    "|manual_original_menu_save_still_available=true" +
                    "|native_trade_not_authorized_without_save=true");
        }

        private void RunDisposableQaFrame()
        {
            if (_featureTicks < _qaNextFrame)
                return;
            _qaNextFrame = _featureTicks + 120;
            try
            {
                string why;
                if (!DisposableGameScope.IsEnabled(out why) || !_mapActive || _mapDisabled || _mapBootstrapPending)
                    return;
                // Private interactive GUI mode only. Never enable a goods
                // transaction here. The normal Save/Post and subsequent
                // native admission code must still verify the original file.
                TryInteractiveOriginalBaselineSave();
                if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1")
                    return;
                // Cold process: independently inspect the ORIGINAL saved
                // canonical owner. No NativeAuthorized, no consent input,
                // no new trade, and no player or inventory mutations.
                if (Environment.GetEnvironmentVariable(
                        "DIPLOMACY_PRIVATE_QA_DECLINE_COLD_VERIFY") == "1" &&
                    _qaDeclineColdReportedGeneration != _sessionGeneration &&
                    _canonicalSaved != null)
                {
                    string wanted = Environment.GetEnvironmentVariable(
                        "DIPLOMACY_PRIVATE_QA_DECLINE_COLD_ID");
                    ulong expected;
                    if (ulong.TryParse(wanted, NumberStyles.None,
                            CultureInfo.InvariantCulture, out expected) &&
                        expected >= 1001)
                    {
                        var model = CanonicalTradeCodec.Decode(_canonicalSaved);
                        var cold = model.HumanOffers.FirstOrDefault(x =>
                            x.Id == expected && x.From == 2 &&
                            x.To == _localPlayer &&
                            x.State == CanonicalHumanConsentState.Declined &&
                            x.DecisionSlot == _localPlayer &&
                            x.DecisionSequence > 0 && x.DecisionNonce > 0);
                        if (cold != null)
                        {
                            _qaDeclineColdReportedGeneration = _sessionGeneration;
                            QaNote("status=actual_original_cold_decline_readback_verified" +
                                "|id=" + cold.Id +
                                "|state=Declined|authentic_original_save=true" +
                                "|decision_slot=" + cold.DecisionSlot +
                                "|decision_sequence=" + cold.DecisionSequence +
                                "|native_goods_writes_observed=0" +
                                "|no_new_consent_submitted=true");
                        }
                    }
                }

                if (_qaGeneration != _sessionGeneration)
                {
                    string command = Path.Combine(QaRoot, "RUN_DISPOSABLE_NATIVE_QA.txt");
                    if (!File.Exists(command) || File.ReadAllText(command).Trim() != "RUN_ISOLATED_NATIVE_TEST")
                        return;
                    // Consume the explicit run request. A later process or loaded map must
                    // never repeat a trade merely because its predecessor left a marker.
                    File.Move(command, command + "." + DateTime.UtcNow.Ticks + ".consumed");
                    _qaGeneration = _sessionGeneration;
                    _qaStage = 0;
                    _qaAttempts = 0;
                    _qaOfferWaitAttempts = 0;
                    _qaChosenOriginalAiSlot = 0;
                    _qaChosenRequestedAmount = 5;
                    _qaDeclineOnly = false;
                    // This merely selects the ORIGINAL local player’s
                    // legitimate UI-style target/amount. It cannot create an
                    // alliance, alter native stock, override a personality,
                    // force a trade outcome or modify an original response.
                    // The same authoritative CanTradeWith / MayAutoAnswerTrade
                    // / NativeAuthorized fences run later for this selected AI.
                    string configuredSlot = Environment.GetEnvironmentVariable(
                        "DIPLOMACY_PRIVATE_QA_TRADE_AI_SLOT");
                    if (!string.IsNullOrEmpty(configuredSlot) &&
                        (!int.TryParse(configuredSlot, NumberStyles.None,
                            CultureInfo.InvariantCulture, out _qaChosenOriginalAiSlot) ||
                         _qaChosenOriginalAiSlot < 1 ||
                         _qaChosenOriginalAiSlot > 8 ||
                         _qaChosenOriginalAiSlot == _localPlayer))
                    {
                        QaNote("status=blocked|reason=invalid_private_original_ai_slot");
                        _qaStage = 90;
                        return;
                    }
                    string configuredQty = Environment.GetEnvironmentVariable(
                        "DIPLOMACY_PRIVATE_QA_TRADE_REQUESTED_QUANTITY");
                    if (!string.IsNullOrEmpty(configuredQty) &&
                        (!int.TryParse(configuredQty, NumberStyles.None,
                            CultureInfo.InvariantCulture, out _qaChosenRequestedAmount) ||
                         _qaChosenRequestedAmount < 1 ||
                         _qaChosenRequestedAmount > 10))
                    {
                        QaNote("status=blocked|reason=invalid_private_requested_quantity");
                        _qaStage = 90;
                        return;
                    }
                    string mode = Environment.GetEnvironmentVariable(
                        "DIPLOMACY_PRIVATE_QA_CONSENT_MODE");
                    if (!string.IsNullOrEmpty(mode))
                    {
                        if (mode != "DECLINE_ONLY")
                        {
                            QaNote("status=blocked|reason=unknown_private_QA_consent_mode");
                            _qaStage = 90;
                            return;
                        }
                        _qaDeclineOnly = true;
                    }
                    QaNote("status=private_terms_selected|native_AI_slot=" +
                        _qaChosenOriginalAiSlot +
                        "|requested_quantity=" + _qaChosenRequestedAmount +
                        "|forced_AI_decision=false|forced_native_write=false");
                    _qaRequest = 0;
                    _qaExpectedTrade = 0;
                    _qaObservedTrade = null;
                    QaNote("status=explicit_run_consumed|process=" + System.Diagnostics.Process.GetCurrentProcess().Id);
                }

                if (_qaStage >= 90)
                    return;
                if (_pendingPackage != null || _nativeTransferProbe != null || _pendingTeamTransition != null || _executingIntent != null)
                    return;
                if (!_saveBridgeInstalled)
                    InstallGameSaveBridge();
                if (!_saveBridgeInstalled)
                {
                    QaNote("status=blocked|reason=save_bridge_unavailable");
                    _qaStage = 90;
                    return;
                }

                if (_qaStage == 0)
                {
                    if (ReadSimulationTick() < 60)
                        return;
                    // A manual save through the original game UI is valid only when the
                    // installed save bridge independently verified and promoted its archive
                    // during this exact map generation. Do not force another native save.
                    if (_coherentSaveGeneration == _sessionGeneration)
                    {
                        QaNote("status=baseline_preverified_in_current_map_generation");
                        _qaStage = 1;
                        _qaAttempts = 0;
                        return;
                    }

                    _qaAttempts++;
                    bool saved = QaSave("NativeTradeBaseline.sav");
                    string sessionReason;
                    NativeInventoryContract.IsStrictSoloHost(out sessionReason);
                    QaNote("status=baseline_save_attempt|number=" + _qaAttempts + "|original_and_archive_verified=" + saved + "|preflight=" + Safe(_savePreflightReason) + "|session=" + Safe(sessionReason) + "|thread=" + Environment.CurrentManagedThreadId);
                    if (saved && _coherentSaveGeneration == _sessionGeneration)
                    {
                        _qaStage = 1;
                        _qaAttempts = 0;
                    }
                    else if (_qaAttempts >= 10)
                    {
                        // disposable test (PRIVATE disposable game only): the exact
                        // original SaveBridge can be temporarily busy during
                        // a graphical/headless game bootstrap. Do not turn
                        // 10 transient failures into an endless wait for a
                        // human UI save that cannot happen in -nographics.
                        // Still require the SAME original archive receipt
                        // before any offer/authorization/native goods write.
                        string privateScopeReason;
                        bool isolatedHeadless = Environment.GetEnvironmentVariable(
                            "DIPLOMACY_HEADLESS_TEST") == "1" &&
                            Environment.GetEnvironmentVariable(
                            "DIPLOMACY_NATIVE_TRADE_FOUR_CELL_PROBE") == "1" &&
                            DisposableGameScope.IsEnabled(out privateScopeReason);
                        bool strictlyTransientSave = !saved &&
                            (_savePreflightReason ==
                             "original_managed_save_prefix_not_called" ||
                             _savePreflightReason == "state_gate_busy" ||
                             _savePreflightReason == "engine_busy");
                        if (isolatedHeadless && strictlyTransientSave &&
                            _qaAttempts < 24)
                        {
                            if (_qaAttempts == 10 || _qaAttempts % 6 == 0)
                                QaNote("status=headless_original_baseline_save_bounded_retry" +
                                    "|attempt=" + _qaAttempts +
                                    "|max_attempts=24|original_receipt_still_required=true" +
                                    "|native_inventory_writes=0");
                        }
                        else if (isolatedHeadless)
                        {
                            // Fail CLOSED and finish this exact QA generation
                            // rather than looping indefinitely in manual mode.
                            _qaStage = 90;
                            QaNote("status=blocked" +
                                "|reason=original_baseline_save_unverified_after_bounded_headless_attempts" +
                                "|attempts=" + _qaAttempts +
                                "|retry_budget=24|original_save_never_forged=true" +
                                "|no_native_trade_approved=true");
                        }
                        else
                        {
                            // The ordinary explicit manual Save path is
                            // retained for non-headless private testing.
                            _qaStage = 4;
                            QaNote("status=awaiting_manual_original_game_save" +
                                "|reason=automatic_baseline_save_not_verified");
                        }
                    }

                    return;
                }

                if (_qaStage == 4)
                {
                    // A delayed, ordinary original-game UI save may complete after the
                    // direct engine call failed. Only the verified bridge receipt unlocks QA.
                    if (_coherentSaveGeneration == _sessionGeneration)
                    {
                        QaNote("status=manual_original_game_save_verified|next=native_authority");
                        _qaStage = 1;
                        _qaAttempts = 0;
                    }

                    return;
                }

                if (_qaStage == 1)
                {
                    if (!NativeAuthorized())
                    {
                        if (++_qaAttempts == 1 || _qaAttempts % 10 == 0)
                        {
                            string contractReason;
                            TryAuthorizeNativeInventory(out contractReason);
                            QaNote("status=waiting|reason=native_authority_after_baseline|attempt=" + _qaAttempts + "|detail=" + Safe(contractReason));
                        }

                        if (_qaAttempts >= 120)
                        {
                            _qaStage = 90;
                            QaNote("status=blocked|reason=native_authority_timeout");
                        }

                        return;
                    }

                    int target = 0;
                    foreach (int player in _roster.OrderBy(x => x))
                        if (player != _localPlayer && IsAlivePlayer(player) &&
                            (_qaChosenOriginalAiSlot == 0 ||
                             player == _qaChosenOriginalAiSlot))
                        {
                            string denied;
                            if (CanTradeWith(player, out denied))
                            {
                                target = player;
                                break;
                            }
                        }

                    if (target == 0)
                    {
                        if (++_qaAttempts == 1 || _qaAttempts % 10 == 0)
                            QaNote("status=waiting|reason=need_actual_allied_or_trade_pact_ai");
                        return;
                    }

                    if (CanonicalOwnsTradeLane)
                    {
                        var offered = PendingConsent(target, true);
                        if (offered != null)
                        {
                            CanonicalTradeAction reply = _qaDeclineOnly ?
                                CanonicalTradeAction.DeclineHuman :
                                CanonicalTradeAction.AcceptHuman;
                            HandleConsentUi(_qaDeclineOnly ? "Decline" : "Accept",
                                target, null);
                            // This is the existing PRODUCTION consent queue, but
                            // panel=null makes it private-driver input, not a
                            // physical human HUD click. No forced AI decision.
                            if (_canonicalInputs.Any(x => x.Consent != null &&
                                  x.Consent.Id == offered.Id &&
                                  x.ConsentAction == reply))
                            {
                                _qaRequest = offered.Id;
                                _qaExpectedTrade = offered.Id;
                                if (_qaDeclineOnly)
                                {
                                    QaNote("status=actual_ai_offer_decline_submitted_via_production_consent" +
                                        "|id=" + offered.Id + "|target=" + target +
                                        "|no_physical_human_click=true" +
                                        "|expected_native_writes=0");
                                    _qaStage = 5;
                                }
                                else
                                {
                                    QaNote("status=actual_ai_offer_answered_via_production_consent|id=" + offered.Id + "|target=" + target + "|give_good=" + offered.RequestedGood + "|give_amount=" + offered.RequestedAmount + "|receive_good=" + offered.OfferedGood + "|receive_amount=" + offered.OfferedAmount);
                                    _qaStage = 2;
                                }
                                return;
                            }
                        }

                        // Let the selected production controller create a genuinely beneficial
                        // initiative. An arbitrary legacy request can be correctly refused by AI.
                        ++_qaOfferWaitAttempts;
                        if (_qaOfferWaitAttempts == 1 || _qaOfferWaitAttempts % 10 == 0)
                            QaNote("status=waiting|reason=actual_production_ai_offer|attempt=" + _qaOfferWaitAttempts + "|target=" + target + "|canonical_ready=" + (_canonical != null) + "|canonical_stopped=" + _canonicalStopped);
                        if (_qaOfferWaitAttempts >= 120)
                        {
                            _qaStage = 90;
                            QaNote("status=blocked|reason=no_actual_production_ai_offer_in_bounded_window");
                        }

                        return;
                    }

                    ArchiveEnsure();
                    _qaRequest = _archive.NextId;
                    _qaExpectedTrade = _qaRequest;
                    EnqueueTrade((byte)_localPlayer, (byte)target, 0, 0,
                        _qaChosenRequestedAmount,
                        TradeOfferGenerosity.Grosszuegig, false,
                        TradeIntentAction.Propose, 0);
                    QaNote("status=production_delayed_request_enqueued|id=" +
                        _qaRequest + "|target=" + target +
                        "|requested_quantity=" + _qaChosenRequestedAmount +
                        "|immediate_native_writes=false|forced_AI_outcome=false");
                    _qaStage = 2;
                    return;
                }

                // State-only negative consent must terminate WITHOUT any
                // PreparedNative journal or four-cell Native commit. Save
                // the genuine original-game archive only after the canonical
                // stored offer is terminal Declined.
                if (_qaStage == 5)
                {
                    var declined = _canonical == null ? null :
                        _canonical.HumanOffersFor((byte)_localPlayer)
                          .FirstOrDefault(x => x.Id == _qaRequest);
                    if (declined == null)
                    {
                        QaNote("status=blocked|reason=decline_offer_missing_from_canonical_owner");
                        _qaStage = 90;
                        return;
                    }
                    if (declined.State == CanonicalHumanConsentState.Pending)
                        return;
                    if (declined.State != CanonicalHumanConsentState.Declined ||
                        declined.DecisionSlot != _localPlayer ||
                        declined.DecisionSequence == 0 ||
                        declined.DecisionNonce == 0 ||
                        _qaObservedTrade != null ||
                        _canonicalTransition != null ||
                        _pendingPackage != null)
                    {
                        QaNote("status=blocked|reason=negative_consent_wrong_terminal_or_native_transition" +
                            "|state=" + declined.State);
                        _qaStage = 90;
                        return;
                    }
                    _qaAttempts++;
                    bool saved = QaSave("AfterAiDecline.sav");
                    QaNote("status=actual_ai_decline_original_save_attempt" +
                        "|id=" + declined.Id +
                        "|state=Declined|original_and_archive_verified=" + saved +
                        "|attempt=" + _qaAttempts +
                        "|native_goods_writes_observed=0" +
                        "|preflight=" + Safe(_savePreflightReason));
                    if (saved && _coherentSaveGeneration == _sessionGeneration)
                    {
                        QaNote("status=actual_ai_decline_original_save_verified" +
                            "|id=" + declined.Id +
                            "|decision_sequence=" + declined.DecisionSequence +
                            "|decision_nonce=" + declined.DecisionNonce +
                            "|native_goods_writes_observed=0");
                        _qaStage = 100;
                    }
                    else if (_qaAttempts >= 10)
                    {
                        QaNote("status=blocked|reason=decline_original_save_unverified");
                        _qaStage = 90;
                    }
                    return;
                }

                if (_qaStage == 2)
                {
                    var intent = _mailbox == null ? null : _mailbox.Intents.FirstOrDefault(x => x.Id == _qaRequest);
                    var consent = _canonical == null ? null : _canonical.HumanOffersFor((byte)_localPlayer).FirstOrDefault(x => x.Id == _qaRequest);
                    if (intent == null && consent == null)
                    {
                        QaNote("status=blocked|reason=request_disappeared");
                        _qaStage = 90;
                        return;
                    }

                    if (intent != null && (intent.State == TradeIntentStatus.Waiting || intent.State == TradeIntentStatus.Processing))
                        return;
                    if (consent != null && (consent.State == CanonicalHumanConsentState.Pending || consent.State == CanonicalHumanConsentState.Accepted || consent.State == CanonicalHumanConsentState.PreparingNative))
                        return;
                    var receipt = _qaObservedTrade;
                    if (receipt == null || receipt.Generation != _sessionGeneration || receipt.Id != _qaExpectedTrade)
                    {
                        var counter = intent == null || intent.Action != TradeIntentAction.Propose || _archive == null ? null : _archive.Records.FirstOrDefault(x => x.RootId == intent.Id && x.State == TradeDisposition.PendingCounter && x.Requester == _localPlayer && x.Recipient == intent.Recipient);
                        if (counter != null)
                        {
                            _qaRequest = _archive.NextId;
                            _qaExpectedTrade = counter.Id;
                            EnqueueTrade(counter.Requester, counter.Recipient, counter.OfferedGood, counter.RequestedGood, counter.RequestedAmount, (TradeOfferGenerosity)counter.Generosity, false, TradeIntentAction.AcceptCounter, counter.Id);
                            QaNote("status=actual_counter_accepted_via_delayed_mailbox|request=" + _qaRequest + "|counter=" + counter.Id);
                            return;
                        }

                        QaNote("status=request_terminal_no_matching_native_commit|state=" + (intent == null ? consent.State.ToString() : intent.State.ToString()) + "|outcome=" + Safe(intent == null ? "human_consent_terminal" : intent.Outcome));
                        _qaStage = 90;
                        return;
                    }

                    QaNote("status=actual_four_cell_native_commit|id=" + receipt.Id + "|request=" + _qaRequest + "|tick=" + receipt.Tick + "|before=" + string.Join(",", receipt.Before) + "|after=" + string.Join(",", receipt.After) + "|players=" + string.Join(",", receipt.Players) + "|goods=" + string.Join(",", receipt.Goods));
                    _qaStage = 3;
                    _qaAttempts = 0;
                    return;
                }

                if (_qaStage == 3)
                {
                    bool saved = QaSave("AfterNativeTrade.sav");
                    _qaAttempts++;
                    QaNote("status=post_trade_save|original_and_archive_verified=" + saved + "|attempt=" + _qaAttempts + "|preflight=" + Safe(_savePreflightReason) + "|trade_id=" + _qaObservedTrade.Id + "|next_id=" + (_archive == null ? 0 : _archive.NextId));
                    // PrepareSave deliberately spans frames to drain the original engine.
                    // A first false result is waiting, never evidence of a failed native save.
                    // Retry saves only; the already committed trade is never submitted again.
                    if (saved)
                        _qaStage = 100;
                    else if (_qaAttempts >= 10)
                    {
                        _qaStage = 90;
                        QaNote("status=blocked|reason=post_trade_save_not_verified_after_bounded_wait");
                    }
                }
            }
            catch (Exception e)
            {
                QaNote("status=qa_halted|reason=" + Safe(e.ToString()));
                _qaStage = 90;
            }
        }
    }
}
#endif
