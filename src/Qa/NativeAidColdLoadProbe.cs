// Private proof data is separate from the original game save and grants no
// inventory authority.  alone validates actual cold history provenance.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using SHCDESE.API.Components.Archive;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class NativeAidColdLoadExpectation
    {
        internal int Pid, Donor, Recipient, Good, Quantity, DonorBefore, RecipientBefore, DonorAfter, RecipientAfter, DonorSaved, RecipientSaved;
        internal long Writes;
        internal ulong Id;
        internal string Path, Sha, Map, Roster, Module;
        internal byte[] Ledger;
    }

    internal sealed partial class DiplomacyFeature
    {
        private static string ExpectationPath
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(DisposableGameScope.ProfileRoot), "qa", "NativeAidColdExpectation.bin");
            }
        }

        // Publish a complete private expectation atomically.
        // A crash between the warm original-save commit and its QA metadata
        // can NEVER expose a partial "passed" envelope to a cold process.
        private static void PublishOriginalAidExpectation(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 100000)
                throw new InvalidDataException("Invalid bounded original aid expectation");
            string target = ExpectationPath;
            string directory = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Private aid expectation directory unavailable");
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                if (File.Exists(target))
                {
                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Reparse private expectation target");
                    File.Replace(temp, target, null);
                }
                else
                    File.Move(temp, target);
                temp = null;
                if (!ExistingSaveReceiptGate.ExactBytes(File.ReadAllBytes(target), bytes))
                    throw new InvalidDataException("Original aid expectation publication mismatch");
            }
            finally
            {
                if (temp != null && File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static NativeAidColdLoadExpectation ReadColdExpectation()
        {
            if (new FileInfo(ExpectationPath).Length > 100000)
                throw new InvalidDataException("Oversized aid expectation");
            using (var f = File.OpenRead(ExpectationPath))
            using (var r = new BinaryReader(f))
            {
                if (r.ReadString() != StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateAidColdExpectationDomain)
                    throw new InvalidDataException("Aid expectation format");
                var e = new NativeAidColdLoadExpectation
                {
                    Pid = r.ReadInt32(),
                    Path = r.ReadString(),
                    Sha = r.ReadString(),
                    Map = r.ReadString(),
                    Roster = r.ReadString(),
                    Module = r.ReadString(),
                    Id = r.ReadUInt64()
                };
                e.Donor = r.ReadInt32();
                e.Recipient = r.ReadInt32();
                e.Good = r.ReadInt32();
                e.Quantity = r.ReadInt32();
                e.DonorBefore = r.ReadInt32();
                e.RecipientBefore = r.ReadInt32();
                e.DonorAfter = r.ReadInt32();
                e.RecipientAfter = r.ReadInt32();
                e.DonorSaved = r.ReadInt32();
                e.RecipientSaved = r.ReadInt32();
                e.Writes = r.ReadInt64();
                int n = r.ReadInt32();
                if (n < 1 || n > 65536)
                    throw new InvalidDataException("Aid expectation ledger size");
                e.Ledger = r.ReadBytes(n);
                if (e.Ledger.Length != n || f.Position != f.Length || e.Pid <= 0 || e.Donor < 1 || e.Donor > 8 || e.Recipient < 1 || e.Recipient > 8 || e.Donor == e.Recipient || e.Good < 1 || e.Good > 24 || e.Quantity < 3 || e.Quantity > 40 || e.Id < 5001 || e.DonorAfter != e.DonorBefore - e.Quantity || e.RecipientAfter != e.RecipientBefore + e.Quantity || e.Writes < 2)
                    throw new InvalidDataException("Invalid observed aid expectation");
                return e;
            }
        }

        internal static bool ValidateExactColdAidSave(string path, out LegacyDiplomacySave saved)
        {
            saved = null;
            string why;
            if (!ExplicitQaScope || !DisposableGameScope.IsEnabled(out why) || !File.Exists(ExpectationPath))
                return false;
            var e = ReadColdExpectation();
            path = Path.GetFullPath(path);
            if (e.Pid == Process.GetCurrentProcess().Id || e.Module != typeof(DiplomacyFeature).Assembly.ManifestModule.ModuleVersionId.ToString("D") || !string.Equals(Path.GetDirectoryName(path), Path.Combine(DisposableGameScope.ProfileRoot, "Saves"), StringComparison.OrdinalIgnoreCase) || !string.Equals(path, e.Path, StringComparison.OrdinalIgnoreCase) || DisposableGameScope.HashFile(path) != e.Sha)
                return false;
            MapArchive archive;
            if (!MapArchive.TryLoad(path, out archive) || archive == null)
                return false;
            using (archive)
                saved = DiplomacyStateCodec.Deserialize(archive.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true), null);
            if (saved == null || saved.AidMemory == null || saved.MapFingerprint != e.Map || saved.RosterFingerprint != e.Roster || saved.PendingPackage != null || saved.PendingTransfer != null || saved.CanonicalTradeState == null || saved.LocalPlayerId < 1 || saved.LocalPlayerId > 8 || !ExistingSaveReceiptGate.ExactBytes(e.Ledger, AidPromiseLedgerCodec.Encode(saved.AidMemory)) || CanonicalTradeCodec.Decode(saved.CanonicalTradeState).HumanMask != (byte)(1 << (saved.LocalPlayerId - 1)))
                return false;
            // The sealed ledger must contain THIS exact delivered event.
            // A different old delivered ID cannot satisfy the expectation,
            // even when the rest of the archive has a valid internal hash.
            if (saved.AidMemory.Promises.Count(p =>
                p.Id == e.Id && p.Status == AidPledgeStatus.Delivered &&
                p.Donor == e.Donor && p.Recipient == e.Recipient &&
                p.Good == e.Good && p.Quantity == e.Quantity) != 1)
                return false;
            var peers = SHCDESE.API.GameNetworkAPI.GetPlayers();
            return peers == null || !peers.Any(p => p != null && p.steamID > 1000 && !p.isSelf);
        }

        internal static bool SaveOriginalQa(string filename, out string reason)
        {
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                bool saved = o.QaSave(filename);
                reason = o._savePreflightReason;
                return saved;
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }

        internal static bool WriteAidColdExpectation(string filename, out string reason)
        {
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "engine_busy";
                    return false;
                }

                using (lease)
                {
                    var a = o._observedNativeAid;
                    // A process-local observation and a completed
                    // journal are both required. A saved, self-consistent ledger
                    // or old QA observation cannot mint a new warm success.
                    int originalWrites;
                    bool sameGift = a != null && o._aidMemory != null &&
                        a.SourcePid == Process.GetCurrentProcess().Id &&
                        a.Generation == o._sessionGeneration &&
                        a.Map == o._mapFingerprint && a.Roster == o._rosterFingerprint &&
                        a.Id >= 5001 && a.Day > 0 && a.Day <= o._lastProcessedDay &&
                        a.Donor != a.Recipient && a.Donor != o._localPlayer &&
                        a.Recipient != o._localPlayer &&
                        a.DonorAfter == a.DonorBefore - a.Quantity &&
                        a.RecipientAfter == a.RecipientBefore + a.Quantity &&
                        o._aidMemory.OriginalNativeGiftObservedThisProcess &&
                        o._aidMemory.Promises.Count(p => p.Id == a.Id &&
                            p.Status == AidPledgeStatus.Delivered &&
                            p.Donor == a.Donor && p.Recipient == a.Recipient &&
                            p.Good == a.Good && p.Quantity == a.Quantity &&
                            p.IssuedDay < a.Day && p.DueDay >= a.Day) == 1 &&
                        o._aidWriteGeneration == o._sessionGeneration &&
                        o._aidWriteAttempts.TryGetValue(a.Id, out originalWrites) &&
                        originalWrites == 2;
                    if (!sameGift || o._coherentSaveGeneration != o._sessionGeneration ||
                        o._aidMemory.HasUnattestedHistoricalNativeAid)
                    {
                        reason = "actual_committed_aid_and_original_save_required";
                        return false;
                    }

                    string path = Path.Combine(DisposableGameScope.ProfileRoot, "Saves", filename);
                    MapArchive archive;
                    if (!MapArchive.TryLoad(path, out archive) || archive == null)
                    {
                        reason = "original_archive_absent";
                        return false;
                    }

                    byte[] ledger = AidPromiseLedgerCodec.Encode(o._aidMemory);
                    using (archive)
                    {
                        var saved = DiplomacyStateCodec.Deserialize(archive.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true), null);
                        if (saved == null || saved.AidMemory == null || !ExistingSaveReceiptGate.ExactBytes(ledger, AidPromiseLedgerCodec.Encode(saved.AidMemory)))
                        {
                            reason = "original_saved_aid_ledger_changed";
                            return false;
                        }
                    }

                    // Do not publish a warm-success cold
                    // expectation unless the original SaveData owner has
                    // already sealed this EXACT .sav AND full aid ledger with
                    // its independent private HMAC capability. A normal
                    // completed save without that receipt is not durable aid.
                    NativeAidDurabilityProbe.Receipt durableReceipt;
                    if (!NativeAidDurabilityProbe.TryRead(path, o._aidMemory,
                        out durableReceipt, out reason) || durableReceipt == null ||
                        !durableReceipt.AppliesTo(o._aidMemory))
                    {
                        reason = "original_save_history_receipt_not_authenticated";
                        return false;
                    }

                    using (var ms = new MemoryStream())
                    using (var w = new BinaryWriter(ms))
                    {
                        w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateAidColdExpectationDomain);
                        w.Write(Process.GetCurrentProcess().Id);
                        w.Write(path);
                        w.Write(DisposableGameScope.HashFile(path));
                        w.Write(o._mapFingerprint);
                        w.Write(o._rosterFingerprint);
                        w.Write(typeof(DiplomacyFeature).Assembly.ManifestModule.ModuleVersionId.ToString("D"));
                        w.Write(a.Id);
                        w.Write(a.Donor);
                        w.Write(a.Recipient);
                        w.Write(a.Good);
                        w.Write(a.Quantity);
                        w.Write(a.DonorBefore);
                        w.Write(a.RecipientBefore);
                        w.Write(a.DonorAfter);
                        w.Write(a.RecipientAfter);
                        w.Write(o.GoodAmount(a.Donor, a.Good));
                        w.Write(o.GoodAmount(a.Recipient, a.Good));
                        w.Write(Interlocked.Read(ref _nativeWriteCalls));
                        w.Write(ledger.Length);
                        w.Write(ledger);
                        w.Flush();
                        PublishOriginalAidExpectation(ms.ToArray());
                    }

                    reason = "actual_native_aid_original_save_expectation_written";
                    return true;
                }
            }
            catch (Exception e)
            {
                reason = "expectation_" + e.GetType().Name;
                return false;
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }

        internal static bool VerifyColdAid(bool exactInitial, out string reason)
        {
            reason = "private_qa_scope_absent";
            if (!ExplicitQaScope)
                return false;
            var o = QaOwner();
            if (o == null)
            {
                reason = "owner_unavailable";
                return false;
            }

            if (!Monitor.TryEnter(o._gate))
            {
                reason = "state_busy";
                return false;
            }

            try
            {
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "engine_busy";
                    return false;
                }

                using (lease)
                {
                    var e = ReadColdExpectation();
                    LegacyDiplomacySave saved;
                    if (!ValidateExactColdAidSave(e.Path, out saved))
                    {
                        reason = "exact_original_cold_save_mismatch";
                        return false;
                    }

                    if (!o._mapActive || o._mapDisabled || o._mapBootstrapPending || o._inventoryRestorePending)
                    {
                        reason = "map_not_ready";
                        return false;
                    }

                    if (o._mapFingerprint != e.Map || o._rosterFingerprint != e.Roster || o._aidMemory == null)
                    {
                        reason = "cold_scope_or_ledger_changed";
                        return false;
                    }

                    if (o._aidMemory.HasUnattestedHistoricalNativeAid && !o.TryRestoreOriginalAidHistory())
                    {
                        reason = "awaiting_original_durable_aid_receipt";
                        return false;
                    }

                    var expected = AidPromiseLedgerCodec.Decode(e.Ledger, e.Map, e.Roster, Math.Max(1, o._lastProcessedDay));
                    var actual = o._aidMemory;
                    var oldIds = expected.Promises.Where(p => p.Status == AidPledgeStatus.Delivered).Select(p => p.Id).ToArray();
                    var fresh = actual.Promises.Where(p => p.Status == AidPledgeStatus.Delivered && !oldIds.Contains(p.Id)).ToArray();
                    bool ledgers = oldIds.All(id => actual.Promises.Count(q => q.Id == id && q.Status == AidPledgeStatus.Delivered && expected.Promises.Any(p => p.Id == id && p.Donor == q.Donor && p.Recipient == q.Recipient && p.Good == q.Good && p.Quantity == q.Quantity && p.IssuedDay == q.IssuedDay && p.DueDay == q.DueDay)) == 1);
                    if (exactInitial)
                        ledgers = ledgers && fresh.Length == 0 && ExistingSaveReceiptGate.ExactBytes(e.Ledger, AidPromiseLedgerCodec.Encode(actual));
                    else
                    {
                        // A DIFFERENT saved pending pledge may legitimately settle after load.
                        // Do not suppress it or call that replay of the already delivered gift.
                        var observed = o._observedNativeAid;
                        ledgers = ledgers && fresh.Length <= 1 && fresh.All(p => expected.Promises.Any(q => q.Id == p.Id && q.Status == AidPledgeStatus.Pending && q.Donor == p.Donor && q.Recipient == p.Recipient && q.Good == p.Good && q.Quantity == p.Quantity && q.IssuedDay < actual.Day && q.DueDay >= actual.Day) && observed != null && observed.SourcePid == Process.GetCurrentProcess().Id && observed.Generation == o._sessionGeneration && observed.Id == p.Id);
                        var favors = expected.Favors.ToDictionary(f => f.A * 256 + f.B, f => new[] { f.Balance, f.Completed, f.LastDeliveryDay });
                        foreach (var p in fresh.OrderBy(p => p.Id))
                        {
                            int key = Math.Min(p.Donor, p.Recipient) * 256 + Math.Max(p.Donor, p.Recipient);
                            int[] favor;
                            if (!favors.TryGetValue(key, out favor))
                            {
                                favor = new[]
                                {
                                    0,
                                    0,
                                    -100
                                };
                                favors.Add(key, favor);
                            }

                            favor[0] = (int)Math.Max(-400L, Math.Min(400L, (long)favor[0] + (p.Donor < p.Recipient ? p.Quantity : -p.Quantity)));
                            favor[1]++;
                            favor[2] = actual.Day;
                        }

                        ledgers = ledgers && actual.Favors.Count == favors.Count && actual.Favors.All(f => favors.ContainsKey(f.A * 256 + f.B) && favors[f.A * 256 + f.B][0] == f.Balance && favors[f.A * 256 + f.B][1] == f.Completed && favors[f.A * 256 + f.B][2] == f.LastDeliveryDay);
                    }

                    int donor = o.GoodAmount(e.Donor, e.Good), recipient = o.GoodAmount(e.Recipient, e.Good);
                    bool savedCellsMatch = donor == e.DonorSaved && recipient == e.RecipientSaved;
                    long nativeWriteCalls = Interlocked.Read(ref _nativeWriteCalls);
                    int replayAttempts = 0;
                    if (o._aidWriteGeneration == o._sessionGeneration)
                        o._aidWriteAttempts.TryGetValue(e.Id, out replayAttempts);
                    bool giftAttempts = fresh.All(p => o._aidWriteGeneration == o._sessionGeneration && o._aidWriteAttempts.ContainsKey(p.Id) && o._aidWriteAttempts[p.Id] == 2);
                    // Only the paused original load must preserve the exact
                    // physical cells. Once the simulation resumes, its economy
                    // can change them independently of any Diplomacy write.
                    bool matching = ledgers && giftAttempts && replayAttempts == 0 &&
                        nativeWriteCalls == 2L * fresh.Length &&
                        !actual.HasUnattestedHistoricalNativeAid &&
                        actual.OriginalNativeGiftObservedThisProcess == (fresh.Length > 0) &&
                        donor >= 0 && recipient >= 0 && (!exactInitial || savedCellsMatch);
                    HeadlessQaDriver.Note("status=" + (matching ? "cold_ai_aid_verified" : "cold_ai_aid_mismatch") +
                        "|phase=" + (exactInitial ? "initial" : "production_pump_no_replay") +
                        "|source_pid=" + e.Pid + "|id=" + e.Id +
                        "|saved=" + e.DonorSaved + "," + e.RecipientSaved +
                        "|actual=" + donor + "," + recipient + "|saved_cells_match=" + savedCellsMatch +
                        "|ledger_matched=" + ledgers + "|cold_native_write_calls=" + nativeWriteCalls +
                        "|original_gift_native_replay_attempts=" + replayAttempts +
                        "|distinct_saved_pending_gifts_delivered=" + fresh.Length +
                        "|historical_receipt_authenticated=" + (!actual.HasUnattestedHistoricalNativeAid) +
                        "|native_gift_observed_this_process=" + actual.OriginalNativeGiftObservedThisProcess +
                        "|save_sha256=" + e.Sha);
                    reason = matching
                        ? (exactInitial ? "saved_native_cells_and_authenticated_history_restored"
                                        : "authenticated_history_and_native_write_audit_without_replay")
                        : "cold_cells_ledger_or_provenance_mismatch";
                    return matching;
                }
            }
            catch (Exception ex)
            {
                reason = "cold_aid_" + ex.GetType().Name;
                return false;
            }
            finally
            {
                Monitor.Exit(o._gate);
            }
        }
    }
}
#endif
