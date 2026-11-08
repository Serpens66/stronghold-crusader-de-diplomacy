// Private generated measurements only. No native mutation is performed here.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private static long _nativeWriteCalls;
        internal static bool PauseExactPrivateColdLoad(string loadedPath)
        {
            // This permits only pause control while the first real PlayState snapshot
            // is still absent. It never grants inventory or multiplayer authority.
            LegacyDiplomacySave saved;
            if (!ValidateExactColdQaSave(loadedPath, out saved))
                return false;
            var archiveApi = SHCDESE.API.GameMapArchiveManagerAPI.Instance;
            if (archiveApi == null || !string.Equals(archiveApi.GetCurrentFilePath(), loadedPath, StringComparison.OrdinalIgnoreCase))
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            using (lease)
            {
                var api = SHCDESE.API.GamePlayerManagerAPI.Instance;
                if (api == null || api.GetLocalPlayerId() != saved.LocalPlayerId || api.IsAIPlayer(saved.LocalPlayerId))
                    return false;
                api.SetLocalPaused(true);
                OriginalPrivateConnectionPause(true);
                return api.IsLocalPaused();
            }
        }

        internal static bool SetSoloTestPaused(bool paused, out bool previous)
        {
            previous = false;
            string reason;
            if (!IsSoloEngineSession(out reason))
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            using (lease)
            {
                if (!IsSoloEngineSession(out reason))
                    return false;
                var api = SHCDESE.API.GamePlayerManagerAPI.Instance;
                previous = api.IsLocalPaused();
                api.SetLocalPaused(paused);
                OriginalPrivateConnectionPause(paused);
                return api.IsLocalPaused() == paused;
            }
        }

        private static DiplomacyFeature QaOwner()
        {
            lock (OwnerGate)
            {
                return _activeOwner;
            }
        }

        internal static bool ReadQaState(out bool active, out bool disabled, out int stage)
        {
            active = false;
            disabled = false;
            stage = -1;
            var owner = QaOwner();
            if (owner == null)
                return false;
            if (!Monitor.TryEnter(owner._gate))
                return false;
            try
            {
                active = owner._mapActive && !owner._mapBootstrapPending;
                disabled = owner._mapDisabled;
                stage = owner._qaStage;
                return true;
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }

        private void WriteColdExpectation(string savePath)
        {
            RequireNativeDiplomacySection();
            var receipt = _qaObservedTrade;
            if (receipt == null || _archive == null)
                throw new InvalidDataException("No observed native trade for cold test");
            var record = _archive.Record(receipt.Id);
            if (record == null || record.State != TradeDisposition.Committed)
                throw new InvalidDataException("Committed archive record absent");
            string path = Path.Combine(QaRoot, "NativeTradeColdExpectation.bin");
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateTradeColdExpectationDomain);
                w.Write(Process.GetCurrentProcess().Id);
                w.Write(savePath);
                w.Write(DisposableGameScope.HashFile(savePath));
                w.Write(_mapFingerprint);
                w.Write(_rosterFingerprint);
                w.Write(receipt.Id);
                w.Write(_archive.NextId);
                w.Write(_archive.Records.Count);
                w.Write(_nativeWriteCalls);
                for (int i = 0; i < 4; i++)
                {
                    w.Write(receipt.Players[i]);
                    w.Write(receipt.Goods[i]);
                    w.Write(receipt.Before[i]);
                    w.Write(receipt.After[i]);
                    w.Write(GoodAmount(receipt.Players[i], receipt.Goods[i]));
                }

                w.Flush();
                File.WriteAllBytes(path, stream.ToArray());
            }

            QaNote("status=cold_expectation_written|trade_id=" + receipt.Id + "|save_sha256=" + DisposableGameScope.HashFile(savePath) + "|native_write_calls=" + _nativeWriteCalls);
        }

        internal static bool VerifyColdExpectation(out string reason)
        {
            reason = "owner_unavailable";
            var owner = QaOwner();
            if (owner == null)
                return false;
            if (!Monitor.TryEnter(owner._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                return owner.VerifyColdExpectationOwned(out reason);
            }
            finally
            {
                Monitor.Exit(owner._gate);
            }
        }

        private bool VerifyColdExpectationOwned(out string reason)
        {
            reason = "map_not_ready";
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _inventoryRestorePending)
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
            {
                reason = "engine_busy";
                return false;
            }

            using (lease)
            using (var r = new BinaryReader(File.OpenRead(Path.Combine(QaRoot, "NativeTradeColdExpectation.bin"))))
            {
                if (r.ReadString() != StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateTradeColdExpectationDomain)
                    throw new InvalidDataException("Cold expectation format");
                int previousPid = r.ReadInt32();
                if (previousPid == Process.GetCurrentProcess().Id)
                {
                    reason = "same_process";
                    return false;
                }

                string path = r.ReadString(), digest = r.ReadString();
                if (!string.Equals(Path.GetDirectoryName(path), Path.Combine(DisposableGameScope.ProfileRoot, "Saves"), StringComparison.OrdinalIgnoreCase) || DisposableGameScope.HashFile(path) != digest)
                {
                    reason = "saved_file_changed";
                    return false;
                }

                if (r.ReadString() != _mapFingerprint || r.ReadString() != _rosterFingerprint)
                {
                    reason = "scope_changed";
                    return false;
                }

                ulong id = r.ReadUInt64(), next = r.ReadUInt64();
                int count = r.ReadInt32();
                long writes = r.ReadInt64();
                if (_archive == null || _archive.NextId != next || _archive.Records.Count != count || _archive.Record(id) == null || _archive.Record(id).State != TradeDisposition.Committed)
                {
                    reason = "archive_or_next_id_changed";
                    return false;
                }

                var before = new int[4];
                var after = new int[4];
                var saved = new int[4];
                var actual = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    int player = r.ReadInt32(), good = r.ReadInt32();
                    before[i] = r.ReadInt32();
                    after[i] = r.ReadInt32();
                    saved[i] = r.ReadInt32();
                    actual[i] = GoodAmount(player, good);
                }

                if (r.BaseStream.Position != r.BaseStream.Length)
                    throw new InvalidDataException("Trailing cold expectation data");
                bool matching = saved.SequenceEqual(actual) && _nativeWriteCalls == 0 && writes == 4;
                HeadlessQaDriver.Note("status=" + (matching ? "cold_native_reload_verified" : "cold_native_reload_mismatch") + "|source_pid=" + previousPid + "|trade_id=" + id + "|next_id=" + next + "|before=" + string.Join(",", before) + "|after_commit=" + string.Join(",", after) + "|at_save=" + string.Join(",", saved) + "|reloaded=" + string.Join(",", actual) + "|previous_native_write_calls=" + writes + "|reload_native_write_calls=" + _nativeWriteCalls + "|save_sha256=" + digest);
                reason = matching ? "four_cells_and_archive_reloaded_without_replay" : "native_cells_or_write_count_mismatch";
                return matching;
            }
        }
    }
}
#endif
