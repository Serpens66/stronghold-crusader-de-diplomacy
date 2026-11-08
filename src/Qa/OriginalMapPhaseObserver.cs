// PRIVATE observation only. No change to original arguments, results, games,
// saved identities, dispatch rights, original locks, source paths or AI policy.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using CrusaderDE;
using HarmonyLib;
using R3;
using SHCDESE.API;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.MapLoader;

namespace StrongholdEuropeDE.Runtime
{
    // Private-only exact original observer target binder. It is intentionally
    // independent of the game, Harmony and native APIs so it can be tested
    // from the real production source against synthetic CLR method bodies.
    internal static class OriginalMapPhaseMethodBinding
    {
        internal static bool TryResolve(
            Type owner, string ownerName, string methodName, Type argumentType,
            Guid expectedMvid, string expectedCanonicalDigest,
            out MethodInfo target, out string reason, out int observedToken,
            out string semanticIl)
        {
            target = null;
            observedToken = 0;
            reason = "metadata_unavailable";
            semanticIl = "not_checked";
            try
            {
                if (owner == null || !string.Equals(owner.FullName, ownerName, StringComparison.Ordinal))
                {
                    reason = "owner_mismatch";
                    return false;
                }

                // The enclosing original assembly identity is independently
                // pinned by the existing startup/game-image guard. Require it
                // here too; neither a filename nor another same-named class
                // in a foreign assembly can bind observer callbacks.
                if (!string.Equals(owner.Assembly.GetName().Name,
                    "Assembly-CSharp", StringComparison.Ordinal))
                {
                    reason = "assembly_identity_mismatch";
                    return false;
                }

                const BindingFlags flags = BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly;
                MethodInfo exact = null;
                bool sawName = false;
                string mismatch = "signature_mismatch";
                foreach (MethodInfo candidate in owner.GetMethods(flags))
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) ||
                        candidate.DeclaringType != owner)
                        continue;
                    sawName = true;
                    if (observedToken == 0)
                        observedToken = candidate.MetadataToken;
                    if (candidate.IsStatic)
                    {
                        mismatch = "staticness_mismatch";
                        continue;
                    }
                    if (candidate.ReturnType != typeof(void))
                    {
                        mismatch = "return_type_mismatch";
                        continue;
                    }
                    ParameterInfo[] args = candidate.GetParameters();
                    if (args.Length != 1)
                    {
                        mismatch = "arity_mismatch";
                        continue;
                    }
                    if (args[0].ParameterType.IsByRef || args[0].IsOut)
                    {
                        mismatch = "by_value_argument_required";
                        continue;
                    }
                    if (args[0].ParameterType != argumentType)
                    {
                        mismatch = "argument_type_mismatch";
                        continue;
                    }
                    if (exact != null)
                    {
                        reason = "ambiguous_exact_signature";
                        return false;
                    }
                    exact = candidate;
                }

                if (exact == null)
                {
                    reason = sawName ? mismatch : "member_missing";
                    return false;
                }
                observedToken = exact.MetadataToken;
                if (owner.Module.ModuleVersionId != expectedMvid ||
                    exact.Module.ModuleVersionId != expectedMvid ||
                    exact.Module != owner.Module)
                {
                    reason = "module_mvid_mismatch";
                    return false;
                }

                // The digest includes SELF:owner/name/arguments/return/static,
                // resolved members, constants, branches, locals and handlers.
                // Never compare raw metadata tokens as semantic authority.
                string current = CanonicalManagedIL.Digest(exact);
                if (!string.Equals(current, expectedCanonicalDigest,
                    StringComparison.Ordinal))
                {
                    reason = "semantic_il_mismatch";
                    semanticIl = "mismatch";
                    return false;
                }
                semanticIl = "matched";
                reason = "exact_signature_mvid_semantic_il";
                target = exact;
                return true;
            }
            catch (Exception)
            {
                // Never log untrusted original IL strings, paths, usernames,
                // game exception text, or unstable exception messages.
                reason = "metadata_or_il_unavailable";
                semanticIl = "unavailable";
                target = null;
                return false;
            }
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // Byte-pinned original Assembly-CSharp method set. A different
        // original MVID is not license to hook unknown managed game code.
        private static readonly Guid OriginalMapPhaseGameMvid =
            new Guid("29ade4ef-f587-422f-a1eb-f81dff066e0a");
        // Physical original metadata tokens are diagnostic reference values,
        // not binding predicates; a loader may reorder method tables.
        private const int OriginalInitGameInfoToken = 100663659;
        private const int OriginalNewMapLoadedToken = 100664235;
        // Derived by the untouched CanonicalManagedIL.Digest from the
        // byte-pinned original Assembly-CSharp.dll (not a runtime guess).
        private const string OriginalInitGameInfoCanonicalDigest =
            "ef3738425de997a0f1b8f2f0046ec341141e5d48fdf68b7f126a1788959aae29";
        private const string OriginalNewMapLoadedCanonicalDigest =
            "0d447cdfa44ab02c9b439ddd8f6657741f745d1a4437fd87c3733874763c3d77";
        private const int OriginalPhaseLogAllowance = 64;

        private Harmony _originalMapPhaseHarmony;
        private IDisposable _originalPhaseMapLoad, _originalPhaseSaveLoad;
        private IDisposable _originalPhaseStartMap, _originalPhaseUnloadMap;
        private int _originalPhaseEnabled, _originalPhaseBudget;
        // Observation-only correlation. Never used by map/session policy.
        private long _originalPhaseObservationSequence;

        // Original native load-result fields are already supplied BY VALUE.
        // This is diagnostic memory only; never copied to a saved token.
        private sealed class OriginalMapLoadReturnSample
        {
            internal readonly int MapSize, GameType, MultiplayerMap, MultiplayerKothMap;
            internal readonly int SkirmishGameType, CoopTrailId;
            internal readonly int[] KeepPositions;
            // Exact eight original LoadMapReturnData Byte fields, not roles.
            internal readonly byte[] StartKeepOrderBytes;
            internal OriginalMapLoadReturnSample(int mapSize, int gameType,
                int multiplayerMap, int multiplayerKothMap, int skirmishGameType,
                int coopTrailId, int[] keepPositions, byte[] startKeepOrderBytes)
            {
                MapSize = mapSize;
                GameType = gameType;
                MultiplayerMap = multiplayerMap;
                MultiplayerKothMap = multiplayerKothMap;
                SkirmishGameType = skirmishGameType;
                CoopTrailId = coopTrailId;
                KeepPositions = keepPositions;
                StartKeepOrderBytes = startKeepOrderBytes;
            }
        }

        private void NoteOriginalMapPhaseTargetBinding(string label,
            bool accepted, string reason, string semanticIl,
            int expectedToken, int observedToken)
        {
            // Fixed anonymous labels/reasons from the binder only. Tokens
            // refer to CLR metadata, never user identifiers or save keys.
            var text = new StringBuilder(220);
            text.Append("status=observer_target_binding|member=").Append(label);
            text.Append("|result=").Append(accepted ? "matched" : "refused");
            text.Append("|reason=").Append(reason);
            text.Append("|semantic_il=").Append(semanticIl);
            text.Append("|expected_token=0x").Append(expectedToken.ToString("X8", CultureInfo.InvariantCulture));
            text.Append("|observed_token=0x").Append(observedToken.ToString("X8", CultureInfo.InvariantCulture));
            text.Append("|token_reordered=").Append(
                observedToken != 0 && expectedToken != observedToken ? "True" : "False");
            text.Append("|authority=private_observation_only");
            Log("original_map_phase", text.ToString());
        }

        // Only after the existing exact startup compatibility guard and
        // original monitor resolution. Failure is not a gameplay error.
        private void InstallPrivateOriginalMapPhaseObserver()
        {
            try
            {
                string scopeReason;
                if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" ||
                    Environment.GetEnvironmentVariable("DIPLOMACY_ORIGINAL_MAP_PHASE_OBSERVER") != "1" ||
                    !DisposableGameScope.IsEnabled(out scopeReason))
                    return;

                MethodInfo init, loaded;
                string initReason, loadedReason, initIl, loadedIl;
                int initObservedToken, loadedObservedToken;
                bool initMatched = OriginalMapPhaseMethodBinding.TryResolve(
                    typeof(GameData), "GameData", "InitGameInfo",
                    typeof(EngineInterface.LoadMapReturnData),
                    OriginalMapPhaseGameMvid, OriginalInitGameInfoCanonicalDigest,
                    out init, out initReason, out initObservedToken, out initIl);
                bool loadedMatched = OriginalMapPhaseMethodBinding.TryResolve(
                    typeof(GameMap), "GameMap", "newMapLoaded",
                    typeof(int), OriginalMapPhaseGameMvid,
                    OriginalNewMapLoadedCanonicalDigest,
                    out loaded, out loadedReason, out loadedObservedToken,
                    out loadedIl);
                NoteOriginalMapPhaseTargetBinding("init_game_info",
                    initMatched, initReason, initIl,
                    OriginalInitGameInfoToken, initObservedToken);
                NoteOriginalMapPhaseTargetBinding("new_map_loaded",
                    loadedMatched, loadedReason, loadedIl,
                    OriginalNewMapLoadedToken, loadedObservedToken);
                if (!initMatched || !loadedMatched)
                {
                    Log("original_map_phase",
                        "status=observer_absent|reason=exact_original_phase_binding_refused");
                    return;
                }

                const BindingFlags callbackFlags = BindingFlags.Static |
                    BindingFlags.NonPublic;
                MethodInfo prefix = typeof(DiplomacyFeature).GetMethod(
                    "OriginalInitGameInfoObservePrefix", callbackFlags);
                MethodInfo postfix = typeof(DiplomacyFeature).GetMethod(
                    "OriginalInitGameInfoObservePostfix", callbackFlags);
                MethodInfo loadedPostfix = typeof(DiplomacyFeature).GetMethod(
                    "OriginalNewMapLoadedObservePostfix", callbackFlags);
                if (prefix == null || postfix == null || loadedPostfix == null)
                    return;

                _originalPhaseBudget = OriginalPhaseLogAllowance;
                // Dedicated Harmony ownership, not the regular diplomacy
                // hook ID. UnpatchSelf cannot remove normal owner hooks.
                _originalMapPhaseHarmony = new Harmony(
                    _harmony.Id + ".private.original-map-phase-observer");
                _originalMapPhaseHarmony.Patch(init,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
                _originalMapPhaseHarmony.Patch(loaded,
                    postfix: new HarmonyMethod(loadedPostfix));

                // Separate typed original event subscriptions. Observation
                // makes no claim about relative callback ordering.
                _originalPhaseMapLoad = ObservableSubscribeExtensions.Subscribe(
                    MapLoaderR3EventHooks.OnLoadMap.Observable, OriginalObserveMapLoad);
                _originalPhaseSaveLoad = ObservableSubscribeExtensions.Subscribe(
                    MapLoaderR3EventHooks.OnLoadSave.Observable, OriginalObserveSaveLoad);
                _originalPhaseStartMap = ObservableSubscribeExtensions.Subscribe(
                    MapLoaderR3EventHooks.OnStartMap.Observable, OriginalObserveStartMap);
                _originalPhaseUnloadMap = ObservableSubscribeExtensions.Subscribe(
                    MapLoaderR3EventHooks.OnUnloadMap.Observable, OriginalObserveUnloadMap);
                Volatile.Write(ref _originalPhaseEnabled, 1);
                Log("original_map_phase", "status=observer_installed|authority=read_only|member_set=pinned");
            }
            catch (Exception)
            {
                // Optional setup failures must undo owned resources without
                // changing map startup, original hooks or dispatch.
                StopPrivateOriginalMapPhaseObserver();
            }
        }

        private void StopPrivateOriginalMapPhaseObserver()
        {
            try
            {
                Volatile.Write(ref _originalPhaseEnabled, 0);
                DisposePrivatePhaseSubscription(ref _originalPhaseUnloadMap);
                DisposePrivatePhaseSubscription(ref _originalPhaseStartMap);
                DisposePrivatePhaseSubscription(ref _originalPhaseSaveLoad);
                DisposePrivatePhaseSubscription(ref _originalPhaseMapLoad);
                Harmony harmony = _originalMapPhaseHarmony;
                _originalMapPhaseHarmony = null;
                if (harmony != null)
                {
                    try { harmony.UnpatchSelf(); }
                    catch (Exception) { /* Callbacks remain inert. */ }
                }
            }
            catch (Exception) { }
        }

        private static void DisposePrivatePhaseSubscription(ref IDisposable subscription)
        {
            IDisposable old = subscription;
            subscription = null;
            if (old != null)
            {
                try { old.Dispose(); }
                catch (Exception) { }
            }
        }

        private static DiplomacyFeature ActiveOriginalPhaseOwner()
        {
            DiplomacyFeature owner = Volatile.Read(ref _activeOwner);
            return owner != null && Volatile.Read(ref owner._originalPhaseEnabled) == 1
                ? owner : null;
        }

        private bool ConsumeOriginalPhaseTraceBudget()
        {
            return Interlocked.Decrement(ref _originalPhaseBudget) >= 0;
        }

        // Harmony-specific __0 and __state are observational parameters.
        // Void prefix: NEVER returns false, changes __0, __instance or result.
        // The original argument is a pinned by-value VALUE TYPE.
        private static void OriginalInitGameInfoObservePrefix(
            EngineInterface.LoadMapReturnData __0,
            out OriginalMapLoadReturnSample __state)
        {
            __state = null;
            try
            {
                DiplomacyFeature owner = ActiveOriginalPhaseOwner();
                if (owner == null)
                    return;
                int[] positions = new int[]
                {
                    __0.keep_positions0x, __0.keep_positions0y,
                    __0.keep_positions1x, __0.keep_positions1y,
                    __0.keep_positions2x, __0.keep_positions2y,
                    __0.keep_positions3x, __0.keep_positions3y,
                    __0.keep_positions4x, __0.keep_positions4y,
                    __0.keep_positions5x, __0.keep_positions5y,
                    __0.keep_positions6x, __0.keep_positions6y,
                    __0.keep_positions7x, __0.keep_positions7y,
                };
                // Preserve every original by-value Byte once, in native
                // index order. No sentinel/base/slot semantics inferred.
                byte[] originalStartKeepOrder = new byte[]
                {
                    __0.start_keep_location_order0,
                    __0.start_keep_location_order1,
                    __0.start_keep_location_order2,
                    __0.start_keep_location_order3,
                    __0.start_keep_location_order4,
                    __0.start_keep_location_order5,
                    __0.start_keep_location_order6,
                    __0.start_keep_location_order7,
                };
                __state = new OriginalMapLoadReturnSample(__0.mapSize,
                    __0.game_type, __0.multiplayerMap, __0.multiplayerKOTHMap,
                    __0.skirmishGameType, __0.coopTrailID, positions,
                    originalStartKeepOrder);
                owner.TraceOriginalReturnSample("init_game_info_argument_pre", __state);
            }
            catch (Exception) { }
        }

        private static void OriginalInitGameInfoObservePostfix(
            OriginalMapLoadReturnSample __state)
        {
            try
            {
                DiplomacyFeature owner = ActiveOriginalPhaseOwner();
                if (owner == null)
                    return;
                if (__state != null)
                    owner.TraceOriginalReturnSample("init_game_info_argument_post", __state);
                owner.TraceOriginalLivePhase("init_game_info_postfix");
            }
            catch (Exception) { }
        }

        private static void OriginalNewMapLoadedObservePostfix()
        {
            try
            {
                DiplomacyFeature owner = ActiveOriginalPhaseOwner();
                if (owner != null)
                    owner.TraceOriginalLivePhase("new_map_loaded_postfix");
            }
            catch (Exception) { }
        }

        // e.Phase is from an already supplied typed original callback,
        // not from a new native query.
        private void OriginalObserveMapLoad(MapLoadEventArgs e)
        {
            try { if (e != null) TraceOriginalLivePhase("r3_map_load_" + e.Phase); }
            catch (Exception) { }
        }
        private void OriginalObserveSaveLoad(LoadSaveGameEventArgs e)
        {
            try { if (e != null) TraceOriginalLivePhase("r3_save_load_" + e.Phase); }
            catch (Exception) { }
        }
        private void OriginalObserveStartMap(MapStartEventArgs e)
        {
            try { if (e != null) TraceOriginalLivePhase("r3_start_map_" + e.Phase); }
            catch (Exception) { }
        }
        private void OriginalObserveUnloadMap(MapUnloadEventArgs e)
        {
            try { if (e != null) TraceOriginalLivePhase("r3_unload_map_" + e.Phase); }
            catch (Exception) { }
        }

        private void TraceOriginalReturnSample(string phase,
            OriginalMapLoadReturnSample snapshot)
        {
            try
            {
                if (snapshot == null || Volatile.Read(ref _originalPhaseEnabled) != 1 ||
                    !ConsumeOriginalPhaseTraceBudget())
                    return;
                // Anonymous original return-data values only. No saved model
                // coordinates, player/Steam IDs or source paths.
                var text = new StringBuilder(256);
                text.Append("status=original_phase|phase=").Append(phase);
                AppendOriginalPhaseStamps(text);
                text.Append("|origin=native_load_result_by_value");
                text.Append("|map_size=").Append(snapshot.MapSize.ToString(CultureInfo.InvariantCulture));
                text.Append("|game_type=").Append(snapshot.GameType.ToString(CultureInfo.InvariantCulture));
                text.Append("|multiplayer_map=").Append(snapshot.MultiplayerMap.ToString(CultureInfo.InvariantCulture));
                text.Append("|multiplayer_koth_map=").Append(snapshot.MultiplayerKothMap.ToString(CultureInfo.InvariantCulture));
                text.Append("|skirmish_game_type=").Append(snapshot.SkirmishGameType.ToString(CultureInfo.InvariantCulture));
                text.Append("|coop_trail_id=").Append(snapshot.CoopTrailId.ToString(CultureInfo.InvariantCulture));
                text.Append("|keep_positions=");
                AppendOriginalPhaseCoordinates(text, snapshot.KeepPositions);
                text.Append("|original_start_keep_order_bytes=");
                AppendOriginalPhaseStartKeepBytes(text, snapshot.StartKeepOrderBytes);
                Log("original_map_phase", text.ToString());
            }
            catch (Exception) { }
        }

        // NEVER acquire or wait for a lock. The original
        // EngineInterface.threadLock must ALREADY be held on this thread.
        private void TraceOriginalLivePhase(string phase)
        {
            try
            {
                if (Volatile.Read(ref _originalPhaseEnabled) != 1 ||
                    !ConsumeOriginalPhaseTraceBudget())
                    return;
                // Managed epoch stamps are observational only; they are not
                // synchronization or authority and may straddle a transition.
                var text = new StringBuilder(320);
                text.Append("status=original_phase|phase=").Append(phase);
                AppendOriginalPhaseStamps(text);
                if (!IsNativeDiplomacySectionHeld)
                {
                    text.Append("|live=skipped_original_monitor_not_held");
                    text.Append("|raw_start_keep_order_ints=not_read_no_original_monitor");
                    Log("original_map_phase", text.ToString());
                    return;
                }
                // This is the only branch permitted to read original live
                // GameData or GameMap: the existing original monitor is
                // already held by the callback thread. Never acquire it.
                GameData game = GameData.Instance;
                if (game == null)
                {
                    text.Append("|live=unavailable_under_original_monitor");
                    text.Append("|raw_start_keep_order_ints=unavailable_game_data");
                    Log("original_map_phase", text.ToString());
                    return;
                }
                // Do not treat unexpected lengths as eight slots, and do
                // not interpret any order element as a player or sentinel.
                int[] nativeOrder = game.start_keep_location_order;
                int[] originalOrderCopy = null;
                string orderUnavailable = null;
                if (nativeOrder == null)
                    orderUnavailable = "unavailable_null";
                else if (nativeOrder.Length != 8)
                    orderUnavailable = "unavailable_length";
                else
                {
                    originalOrderCopy = new int[8];
                    for (int i = 0; i < 8; i++)
                        originalOrderCopy[i] = nativeOrder[i];
                }
                if (game.Keep_Locations == null ||
                    game.Keep_Locations.GetLength(0) != 8 ||
                    game.Keep_Locations.GetLength(1) != 2)
                {
                    text.Append("|live=unavailable_under_original_monitor");
                    text.Append("|keep_locations=unavailable_shape");
                    text.Append("|raw_start_keep_order_ints=");
                    AppendOriginalPhaseLiveOrder(text, originalOrderCopy, orderUnavailable);
                    Log("original_map_phase", text.ToString());
                    return;
                }
                int mapType = (int)game.mapType;
                int mapSize = GameMap.tilemapSize;
                int skirmish = game.SkirmishGameType;
                int coop = game.coopTrailID;
                var positions = new int[16];
                for (int i = 0; i < 8; i++)
                {
                    positions[2 * i] = game.Keep_Locations[i, 0];
                    positions[2 * i + 1] = game.Keep_Locations[i, 1];
                }
                text.Append("|live=original_monitor_held");
                text.Append("|map_type=").Append(mapType.ToString(CultureInfo.InvariantCulture));
                text.Append("|tilemap_size=").Append(mapSize.ToString(CultureInfo.InvariantCulture));
                text.Append("|skirmish_game_type=").Append(skirmish.ToString(CultureInfo.InvariantCulture));
                text.Append("|coop_trail_id=").Append(coop.ToString(CultureInfo.InvariantCulture));
                text.Append("|keep_locations=");
                AppendOriginalPhaseCoordinates(text, positions);
                text.Append("|raw_start_keep_order_ints=");
                AppendOriginalPhaseLiveOrder(text, originalOrderCopy, orderUnavailable);
                Log("original_map_phase", text.ToString());
            }
            catch (Exception) { }
        }

        // These stamps label what the observer saw, not proof that
        // original callbacks were atomic or that a generation was accepted.
        // All fields are already managed feature state; never query native.
        private void AppendOriginalPhaseStamps(StringBuilder text)
        {
            long serial = Interlocked.Increment(ref _originalPhaseObservationSequence);
            long epoch = Volatile.Read(ref _mapEpoch);
            long generation = Volatile.Read(ref _sessionGeneration);
            long bootstrapEpoch = Volatile.Read(ref _mapBootstrapEpoch);
            long featureTick = Volatile.Read(ref _featureTicks);
            text.Append("|observer_serial=").Append(serial.ToString(CultureInfo.InvariantCulture));
            text.Append("|map_epoch=").Append(epoch.ToString(CultureInfo.InvariantCulture));
            text.Append("|session_generation=").Append(generation.ToString(CultureInfo.InvariantCulture));
            text.Append("|bootstrap_epoch=").Append(bootstrapEpoch.ToString(CultureInfo.InvariantCulture));
            text.Append("|feature_tick=").Append(featureTick.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendOriginalPhaseStartKeepBytes(
            StringBuilder text, byte[] originalBytes)
        {
            if (originalBytes == null || originalBytes.Length != 8)
                throw new InvalidOperationException("private original return-order bytes unavailable");
            for (int i = 0; i < 8; i++)
            {
                if (i != 0)
                    text.Append(',');
                // Preserve exact original unsigned byte values 0..255.
                text.Append(originalBytes[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendOriginalPhaseLiveOrder(
            StringBuilder text, int[] originalInts, string unavailable)
        {
            if (originalInts == null || originalInts.Length != 8)
            {
                text.Append(unavailable ?? "unavailable_shape");
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                if (i != 0)
                    text.Append(',');
                // The original GameData array is Int32[8], not reinterpreted
                // as eight bytes, players, sentinels or an assignment base.
                text.Append(originalInts[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendOriginalPhaseCoordinates(StringBuilder text,
            int[] coordinates)
        {
            if (coordinates == null || coordinates.Length != 16)
                throw new InvalidOperationException("private original map phase coordinates unavailable");
            for (int i = 0; i < 8; i++)
            {
                if (i != 0)
                    text.Append(';');
                text.Append(coordinates[2 * i].ToString(CultureInfo.InvariantCulture));
                text.Append(',').Append(coordinates[2 * i + 1].ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
#endif
