// Private QA timing control only. Original load/save/goods code is preserved.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using CrusaderDE;
using HarmonyLib;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private static bool ReportExactColdQaDenied(string safeReason)
        {
            // No original engine, save, inventory, network, archive or lobby
            // permissions are changed. The private QA driver records which
            // pre-existing guard denied the cold attempt, WITHOUT any user
            // identifiers, paths, player names or Steam IDs.
            HeadlessQaDriver.Note("status=exact_cold_preflight_denied|reason=" +
                safeReason);
            return false;
        }

        internal static bool ValidateExactColdQaSave(string path, out LegacyDiplomacySave saved)
        {
            saved = null;
            if (HeadlessQaDriver.IsAiMilitaryColdMode)
                return ValidateExactColdAidSave(path, out saved);
            string why;
            if (!DisposableGameScope.IsEnabled(out why))
                return ReportExactColdQaDenied("disposable_test_scope_invalid");
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(path), Path.Combine(DisposableGameScope.ProfileRoot, "Saves"), StringComparison.OrdinalIgnoreCase))
                return ReportExactColdQaDenied("save_not_in_exact_test_profile");
            string expectation = Path.Combine(Path.GetDirectoryName(DisposableGameScope.ProfileRoot), "qa", "NativeTradeColdExpectation.bin");
            if (!File.Exists(expectation))
                return ReportExactColdQaDenied("expectation_file_absent");
            // The existing warm QA wrote exactly one immutable four-cell
            // receipt, with an original Save SHA. Read the WHOLE receipt
            // before allowing any legacy-only cold-load attempt. These
            // values are observations, not permission to write Game goods.
            string expectedMap, expectedRoster;
            ulong expectedTradeId, expectedNextId;
            int expectedRecords;
            long expectedNativeWrites;
            var expectedPlayers = new int[4];
            var expectedGoods = new int[4];
            var expectedBefore = new int[4];
            var expectedAfter = new int[4];
            using (var r = new BinaryReader(File.OpenRead(expectation)))
            {
                if (r.ReadString() != StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateTradeColdExpectationDomain)
                    return ReportExactColdQaDenied("expectation_domain_mismatch");
                if (r.ReadInt32() == Process.GetCurrentProcess().Id)
                    return ReportExactColdQaDenied("same_process_cold_forbidden");
                if (!string.Equals(r.ReadString(), path, StringComparison.OrdinalIgnoreCase))
                    return ReportExactColdQaDenied("expectation_target_path_mismatch");
                if (r.ReadString() != DisposableGameScope.HashFile(path))
                    return ReportExactColdQaDenied("expectation_save_hash_mismatch");
                expectedMap = r.ReadString();
                expectedRoster = r.ReadString();
                expectedTradeId = r.ReadUInt64();
                expectedNextId = r.ReadUInt64();
                expectedRecords = r.ReadInt32();
                expectedNativeWrites = r.ReadInt64();
                if (string.IsNullOrEmpty(expectedMap) ||
                    string.IsNullOrEmpty(expectedRoster) ||
                    expectedTradeId < TradeHistoryArchive.MinimumTradeId ||
                    expectedRecords <= 0 || expectedNativeWrites != 4)
                    return ReportExactColdQaDenied("warm_receipt_identity_invalid");
                for (int i = 0; i < 4; ++i)
                {
                    expectedPlayers[i] = r.ReadInt32();
                    expectedGoods[i] = r.ReadInt32();
                    expectedBefore[i] = r.ReadInt32();
                    expectedAfter[i] = r.ReadInt32();
                    int atSave = r.ReadInt32();
                    if (expectedPlayers[i] < 1 || expectedPlayers[i] > 8 ||
                        expectedGoods[i] <= 0 ||
                        expectedBefore[i] < 0 || expectedAfter[i] < 0 ||
                        expectedAfter[i] != atSave)
                        return ReportExactColdQaDenied("warm_receipt_cell_unverified");
                }
                if (r.BaseStream.Position != r.BaseStream.Length ||
                    expectedGoods[0] != expectedGoods[1] ||
                    expectedGoods[2] != expectedGoods[3] ||
                    expectedGoods[0] == expectedGoods[2] ||
                    expectedPlayers[0] != expectedPlayers[3] ||
                    expectedPlayers[1] != expectedPlayers[2] ||
                    expectedPlayers[0] == expectedPlayers[1] ||
                    (long)expectedBefore[0] + expectedBefore[1] !=
                        (long)expectedAfter[0] + expectedAfter[1] ||
                    (long)expectedBefore[2] + expectedBefore[3] !=
                        (long)expectedAfter[2] + expectedAfter[3])
                    return ReportExactColdQaDenied("warm_receipt_four_cell_conservation_failed");
            }

            SHCDESE.API.Components.Archive.MapArchive archive;
            if (!SHCDESE.API.Components.Archive.MapArchive.TryLoad(path, out archive) || archive == null)
                return ReportExactColdQaDenied("original_archive_cannot_load");
            using (archive)
                saved = DiplomacyStateCodec.Deserialize(archive.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true), null);
            if (saved == null)
                return ReportExactColdQaDenied("mod_archive_payload_missing");
            if (saved.PendingPackage != null || saved.PendingTransfer != null)
                return ReportExactColdQaDenied("unsettled_package_or_transfer_in_save");
            // The first native cell belongs to the trade requester, NOT
            // necessarily the local human. Real original AI -> human offers
            // have the AI at expectedPlayers[0], local human at [1].
            // This private test must prove that orientation from the exact
            // completed archived record, never from an untrusted UI flag.
            if (saved.LocalPlayerId < 1 || saved.LocalPlayerId > 8 ||
                (saved.LocalPlayerId != expectedPlayers[0] &&
                 saved.LocalPlayerId != expectedPlayers[1]))
                return ReportExactColdQaDenied("invalid_native_local_player_id");
            bool originalAiRequester = saved.LocalPlayerId == expectedPlayers[1];
            if (originalAiRequester)
            {
                var archiveForAi = saved.TradeArchive;
                TradeRecord originalAiRecord = archiveForAi == null
                    ? null : archiveForAi.Record(expectedTradeId);
                if (archiveForAi == null || saved.TradeMailbox == null ||
                    saved.RequestHistory == null ||
                    archiveForAi.PendingNativeId != 0 ||
                    archiveForAi.NextId != expectedNextId ||
                    archiveForAi.Records.Count != expectedRecords ||
                    !string.Equals(archiveForAi.MapFingerprint, expectedMap,
                        StringComparison.Ordinal) ||
                    !string.Equals(archiveForAi.RosterFingerprint, expectedRoster,
                        StringComparison.Ordinal) ||
                    originalAiRecord == null ||
                    originalAiRecord.State != TradeDisposition.Committed ||
                    originalAiRecord.AiToAi ||
                    originalAiRecord.Requester != expectedPlayers[0] ||
                    originalAiRecord.Recipient != expectedPlayers[1] ||
                    originalAiRecord.OfferedGood != expectedGoods[0] ||
                    originalAiRecord.RequestedGood != expectedGoods[2] ||
                    originalAiRecord.OfferedAmount != expectedBefore[0] - expectedAfter[0] ||
                    originalAiRecord.RequestedAmount != expectedBefore[2] - expectedAfter[2])
                    return ReportExactColdQaDenied("original_ai_requester_archive_or_terms_unverified");
                // Archive validation remains the unchanged original validator.
                archiveForAi.Validate(saved.MapFingerprint,
                    saved.RosterFingerprint, saved.LastProcessedDay);
                HeadlessQaDriver.Note(
                    "status=exact_ai_requester_cold_archived_terms_attested" +
                    "|trade_id=" + expectedTradeId +
                    "|local_human_is_recipient=true|native_writes_authorized=false");
            }
            if (!string.Equals(saved.MapFingerprint, expectedMap,
                    StringComparison.Ordinal) ||
                !string.Equals(saved.RosterFingerprint, expectedRoster,
                    StringComparison.Ordinal))
                return ReportExactColdQaDenied("saved_map_roster_differs_from_hashed_receipt");

            if (saved.CanonicalTradeState != null)
            {
                // The canonical owner retains its strict immutable human
                // mask check; no compatibility lane can bypass it.
                if (CanonicalTradeCodec.Decode(saved.CanonicalTradeState).HumanMask !=
                    (byte)(1 << (saved.LocalPlayerId - 1)))
                    return ReportExactColdQaDenied("canonical_trade_human_mask_not_single");
            }
            else
            {
                // Schema23 CAN LEGITIMATELY save a null CanonicalTradeState:
                // CanonicalTradeStatePersistence.Write writes length 0, and
                // RestoreCanonicalState restores null while the legacy trade
                // archive and mailbox remain the SINGLE authoritative owner.
                // Allow this PRIVATE ONLY for the EXACT original committed
                // warm trade, not for arbitrary legacy saves or real MP.
                if (SHCDESE.API.GameNetworkAPI.IsLobbyEnvironment())
                    return ReportExactColdQaDenied("legacy_cold_real_MP_lobby_forbidden");
                if (saved.TradeArchive == null || saved.TradeMailbox == null ||
                    saved.RequestHistory == null ||
                    saved.TradeArchive.PendingNativeId != 0 ||
                    saved.TradeArchive.NextId != expectedNextId ||
                    saved.TradeArchive.Records.Count != expectedRecords)
                    return ReportExactColdQaDenied("legacy_trade_ledger_missing_or_unsettled");
                saved.TradeArchive.Validate(saved.MapFingerprint,
                    saved.RosterFingerprint, saved.LastProcessedDay);
                saved.TradeMailbox.Validate(saved.MapFingerprint,
                    saved.RosterFingerprint, saved.LastProcessedDay,
                    saved.TradeArchive);
                TradeRecord confirmed = saved.TradeArchive.Record(expectedTradeId);
                if (confirmed == null ||
                    confirmed.State != TradeDisposition.Committed ||
                    confirmed.AiToAi ||
                    confirmed.Requester != expectedPlayers[0] ||
                    confirmed.Recipient != expectedPlayers[1] ||
                    confirmed.OfferedGood != expectedGoods[0] ||
                    confirmed.RequestedGood != expectedGoods[2])
                    return ReportExactColdQaDenied("legacy_archived_trade_not_exact_committed_receipt");
            }

            var peers = SHCDESE.API.GameNetworkAPI.GetPlayers();
            if (peers != null && peers.Any(p => p != null &&
                p.steamID > 1000 && !p.isSelf))
                return ReportExactColdQaDenied("network_other_member_identity_before_original_load");
            if (saved.CanonicalTradeState == null)
                HeadlessQaDriver.Note("status=exact_legacy_cold_preflight_permitted" +
                    "|trade_id=" + expectedTradeId +
                    "|no_native_goods_write_authorized=true");
            return true;
        }

        internal static void OriginalPrivateConnectionPause(bool paused)
        {
            RequireNativeDiplomacySection();
            string why;
            if (Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1" || !DisposableGameScope.IsEnabled(out why))
                throw new InvalidOperationException("Exact private pause scope absent");
            var type = typeof(MainViewModel).Assembly.GetType("EngineInterface", true);
            var method = type.GetMethod("ConnectionPauseEngine", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException("Pinned original connection pause absent");
            method.Invoke(null, new object[] { paused });
        }

        internal static IDisposable BeginExactPrivateColdLoad(string path)
        {
            LegacyDiplomacySave saved;
            if (!ValidateExactColdQaSave(path, out saved))
                throw new InvalidDataException("Cold load is not the exact verified single-human private save");
            // Original LoadSaveFile takes this very monitor itself. Extend it through
            // its managed postfix so no original worker can simulate before pause.
            IDisposable lease;
            var timer = Stopwatch.StartNew();
            while (!TryEnterNativeInventorySection(out lease))
            {
                if (timer.ElapsedMilliseconds >= 2000)
                    throw new InvalidOperationException("Original cold-load monitor unavailable");
                Thread.Sleep(1);
            }

            return lease;
        }
    }

    internal static partial class HeadlessQaDriver
    {
        private static Harmony coldLoadHarmony;
        private static bool nativeTradePauseOwned, coldConnectionPauseOwned;
        private static void InstallAtomicColdLoadPause()
        {
            if (coldLoadHarmony != null)
                return;
            var engine = typeof(MainViewModel).Assembly.GetType("EngineInterface", true);
            var target = engine.GetMethod("LoadSaveFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (target == null || target.ReturnType.Name != "LoadMapReturnData")
                throw new MissingMethodException("Pinned original private load boundary absent");
            var type = typeof(HeadlessQaDriver);
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            coldLoadHarmony = new Harmony("StrongholdEuropeDE.PrivateHeadlessExactColdLoad");
            coldLoadHarmony.Patch(target, prefix: new HarmonyMethod(type.GetMethod("AtomicColdLoadPrefix", flags)), postfix: new HarmonyMethod(type.GetMethod("AtomicColdLoadPostfix", flags)), finalizer: new HarmonyMethod(type.GetMethod("AtomicColdLoadFinalizer", flags)));
        }

        private static void AtomicColdLoadPrefix(string __0, out IDisposable __state)
        {
            __state = null;
            if (!AnyColdAction)
                return;
            string expected = Path.Combine(DisposableGameScope.ProfileRoot, "Saves", filename);
            if (!string.Equals(Path.GetFullPath(__0), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected private cold target");
            __state = DiplomacyFeature.BeginExactPrivateColdLoad(__0);
        }

        private static void AtomicColdLoadPostfix(IDisposable __state)
        {
            if (__state == null)
                return;
            DiplomacyFeature.OriginalPrivateConnectionPause(true);
            coldConnectionPauseOwned = true;
            Note("status=original_cold_load_connection_paused_under_same_monitor");
        }

        private static Exception AtomicColdLoadFinalizer(Exception __exception, IDisposable __state)
        {
            if (__state != null)
                __state.Dispose();
            return __exception;
        }

        internal static void FreezeObservedNativeTrade()
        {
            if (action != "LOAD_TRADE" || stage != 3 || Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1")
                return;
            // Observation must never throw into production booking/compensation.
            try
            {
                bool previous;
                if (DiplomacyFeature.SetSoloTestPaused(true, out previous))
                {
                    nativeTradePauseOwned = true;
                    Note("status=verified_native_trade_original_simulation_paused");
                }
                else
                    Note("status=trade_pause_not_acquired|reason=original_engine_busy");
            }
            catch (Exception e)
            {
                Note("status=trade_pause_not_acquired|reason=" + e.GetType().Name);
            }
        }
    }
}
#endif
