// Exact local disposable game only. Reuses the ONE  original save owner.
// The certificate is not a game save, a multiplayer authority or a QA bypass.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using SHCDESE.API;
using SHCDESE.API.Components.Archive;

namespace StrongholdEuropeDE.Runtime
{
    internal static class NativeAidDurabilityProbe
    {
        private const string Magic = StrongholdEuropeDE.Runtime.CompatibilityContracts.PrivateAidReceiptDomain;
        private const int MaximumEnvelope = 65536;
        private static string Store
        {
            get
            {
                return Path.Combine(DisposableGameScope.ProfileRoot, "NativeAidProof");
            }
        }

        private static string Full(string p)
        {
            return Path.GetFullPath(p);
        }

        private static string Digest(byte[] bytes)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static bool Exact(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool SavePath(string p)
        {
            bool valid = !string.IsNullOrEmpty(p) && p.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(Full(p)), Full(Path.Combine(DisposableGameScope.ProfileRoot, "Saves")), StringComparison.OrdinalIgnoreCase) && File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0;
            if (!valid)
                return false;
            string profile = Full(DisposableGameScope.ProfileRoot);
            for (string parent = Path.GetDirectoryName(Full(p)); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    return false;
                if (string.Equals(Full(parent), profile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void CheckStore()
        {
            string profile = Full(DisposableGameScope.ProfileRoot);
            for (string p = Store; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            {
                if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse private receipt path");
                if (string.Equals(Full(p), profile, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            throw new InvalidDataException("Receipt store outside private profile");
        }

        private static byte[] Entropy()
        {
            return Encoding.UTF8.GetBytes(Magic + "|" + Full(DisposableGameScope.ProfileRoot).ToUpperInvariant() + "|" + DisposableGameScope.GameManagedSha + "|" + DisposableGameScope.RawraSha);
        }

        private static byte[] Key(bool create)
        {
            CheckStore();
            string file = Path.Combine(Store, "local-key.dpapi");
            if (!File.Exists(file))
            {
                if (!create)
                    throw new FileNotFoundException("Private original aid key absent");
                Directory.CreateDirectory(Store);
                CheckStore();
                byte[] secret = new byte[32], protectedKey;
                try
                {
                    using (var rng = RandomNumberGenerator.Create())
                        rng.GetBytes(secret);
                    protectedKey = ProtectedData.Protect(secret, Entropy(), DataProtectionScope.CurrentUser);
                }
                finally
                {
                    Array.Clear(secret, 0, secret.Length);
                }

                using (var f = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    f.Write(protectedKey, 0, protectedKey.Length);
                    f.Flush(true);
                }
            }

            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 4096)
                throw new InvalidDataException("Invalid private aid key");
            byte[] key = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy(), DataProtectionScope.CurrentUser);
            if (key.Length != 32)
                throw new InvalidDataException("Invalid private aid key length");
            return key;
        }

        // Pure envelope helpers. They issue no receipt capability or file authority.
        private static byte[] SealEnvelope(byte[] body, byte[] key)
        {
            if (body == null || body.Length == 0 || body.Length > MaximumEnvelope - 36 || key == null || key.Length != 32)
                throw new InvalidDataException("Invalid aid envelope input");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(body.Length);
                w.Write(body);
                using (var h = new HMACSHA256(key))
                    w.Write(h.ComputeHash(body));
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] AuthenticateEnvelope(byte[] envelope, byte[] key)
        {
            if (envelope == null || envelope.Length < 37 || envelope.Length > MaximumEnvelope || key == null || key.Length != 32)
                throw new InvalidDataException("Invalid aid envelope size");
            using (var ms = new MemoryStream(envelope, false))
            using (var r = new BinaryReader(ms))
            {
                int n = r.ReadInt32();
                if (n <= 0 || n != envelope.Length - 36)
                    throw new InvalidDataException("Invalid aid envelope framing");
                byte[] body = r.ReadBytes(n), mac = r.ReadBytes(32);
                using (var h = new HMACSHA256(key))
                    if (!Exact(mac, h.ComputeHash(body)))
                        throw new InvalidDataException("Unauthenticated original aid certificate");
                return body;
            }
        }

        // This digest is only a directory index, not the security proof:
        // the HMAC-SHA256 certificate BODY still authenticates the FULL
        // original save path, SHA-256, private profile, game/extender
        // hashes and COMPLETE AidLedger. 32 hex digits (128-bit locator)
        // avoid Win32 MAX_PATH when a correctly staged QA root is long.
        // Even an index collision is fail-closed at AuthenticateEnvelope.
        private static string CertificatePath(string save)
        {
            string fullDigest = Digest(Encoding.UTF8.GetBytes(Full(save).ToUpperInvariant()));
            return Path.Combine(Store, fullDigest.Substring(0, 32) + ".receipt");
        }

        private static byte[] Body(string path, string saveSha, AidLedger memory)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(DisposableGameScope.GameManagedSha);
                w.Write(DisposableGameScope.RawraSha);
                w.Write(Full(DisposableGameScope.ProfileRoot).ToUpperInvariant());
                w.Write(Full(path).ToUpperInvariant());
                w.Write(saveSha);
                w.Write(Digest(AidPromiseLedgerCodec.Encode(memory)));
                w.Write(memory.Map);
                w.Write(memory.Roster);
                w.Write(memory.Day);
                w.Flush();
                return ms.ToArray();
            }
        }

        // Nonserializable, reference-bound capability. Only TryRead below constructs
        // it after original process scope, complete save SHA and secret MAC checks.
        internal sealed class Receipt
        {
            private readonly AidLedger bound;
            private readonly string ledger;
            private Receipt(AidLedger value)
            {
                bound = value;
                ledger = Digest(AidPromiseLedgerCodec.Encode(value));
            }

            internal bool AppliesTo(AidLedger value)
            {
                return Object.ReferenceEquals(value, bound) && ledger == Digest(AidPromiseLedgerCodec.Encode(value));
            }

            // Factory itself performs ALL checks. There is no "verified=true" input
            // and no same-assembly constructor that can skip MAC/file authentication.
            internal static bool TryIssue(string path, AidLedger memory, out Receipt receipt, out string reason)
            {
                receipt = null;
                byte[] key = null;
                reason = "private_aid_certificate_absent";
                try
                {
                    if (!DisposableGameScope.IsEnabled(out reason) || !SavePath(path) || memory == null)
                        return false;
                    CheckStore();
                    string certificate = CertificatePath(path);
                    if (!File.Exists(certificate))
                    {
                        reason = "private_aid_certificate_absent";
                        return false;
                    }

                    if ((File.GetAttributes(certificate) & FileAttributes.ReparsePoint) != 0 || new FileInfo(certificate).Length > MaximumEnvelope)
                        throw new InvalidDataException("Invalid certificate file");
                    key = Key(false);
                    byte[] actual = AuthenticateEnvelope(File.ReadAllBytes(certificate), key);
                    byte[] expected = Body(path, DisposableGameScope.HashFile(path), memory);
                    if (!Exact(actual, expected))
                    {
                        reason = "private_aid_save_or_ledger_scope_changed";
                        return false;
                    }

                    receipt = new Receipt(memory);
                    reason = "exact_original_saved_aid_history_authenticated";
                    return true;
                }
                catch (Exception e)
                {
                    reason = "private_aid_certificate_" + e.GetType().Name;
                    return false;
                }
                finally
                {
                    if (key != null)
                        Array.Clear(key, 0, key.Length);
                }
            }
        }

        internal static bool TrySeal(string path, string committedSha, AidLedger memory, out string reason)
        {
            byte[] key = null;
            string temp = null;
            reason = "private_aid_certificate_not_written";
            try
            {
                if (!DisposableGameScope.IsEnabled(out reason) || !SavePath(path) || memory == null || memory.HasUnattestedHistoricalNativeAid || !memory.Promises.Any(p => p.Status == AidPledgeStatus.Delivered) || DisposableGameScope.HashFile(path) != committedSha)
                    return false;
                key = Key(true);
                byte[] envelope = SealEnvelope(Body(path, committedSha, memory), key);
                string destination = CertificatePath(path);
                temp = Path.Combine(Store, ".aid-receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    f.Write(envelope, 0, envelope.Length);
                    f.Flush(true);
                }

                if (File.Exists(destination))
                    File.Replace(temp, destination, null);
                else
                    File.Move(temp, destination);
                temp = null;
                if (!Exact(File.ReadAllBytes(destination), envelope))
                    throw new IOException("Certificate publication mismatch");
                reason = "exact_original_save_and_complete_aid_history_sealed";
                return true;
            }
            catch (Exception e)
            {
                reason = "private_aid_certificate_" + e.GetType().Name;
                return false;
            }
            finally
            {
                if (key != null)
                    Array.Clear(key, 0, key.Length);
                if (temp != null)
                    try
                    {
                        File.Delete(temp);
                    }
                    catch
                    {
                    }
            }
        }

        internal static bool TryRead(string path, AidLedger memory, out Receipt receipt, out string reason)
        {
            return Receipt.TryIssue(path, memory, out receipt, out reason);
        }
    }

    internal sealed partial class AidLedger
    {
        internal bool RestoreOriginalHistory(NativeAidDurabilityProbe.Receipt receipt)
        {
            if (receipt == null || !receipt.AppliesTo(this))
                return false;
            _originalNativeHistoryAttested = true;
            return true;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private string _loadedAidSaveSha, _loadedAidPrimarySha;
        // Actual original LoadData callback records the file AND primary bytes used
        // for this load. Later archive-context changes cannot substitute another save.
        private void NoteOriginalAidLoad(string path, byte[] primary)
        {
            _loadedAidSaveSha = null;
            _loadedAidPrimarySha = null;
            try
            {
                string why;
                if (primary == null || !DisposableGameScope.IsEnabled(out why) || string.IsNullOrEmpty(path) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.Combine(DisposableGameScope.ProfileRoot, "Saves"), StringComparison.OrdinalIgnoreCase))
                    return;
                _loadedAidSaveSha = DisposableGameScope.HashFile(path);
                using (var h = SHA256.Create())
                    _loadedAidPrimarySha = BitConverter.ToString(h.ComputeHash(primary)).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                _loadedAidSaveSha = null;
                _loadedAidPrimarySha = null;
            }
        }

        private void SealOriginalAidHistory(SaveCall c)
        {
            try
            {
                RequireNativeDiplomacySection();
                string reason;
                if (c == null || !Object.ReferenceEquals(c.Owner, this) || !Object.ReferenceEquals(_activeSave, c) || c.MpConsentOriginalSave || !_privateSaveScope || c.Released || !c.StateGateHeld || !Monitor.IsEntered(_gate) || c.ThreadId != Environment.CurrentManagedThreadId || c.Generation != _sessionGeneration || c.File == null || !c.File.Committed || _unattestedSavedAid || _nativeTransferProbe != null || _pendingPackage != null || _nativeMutation != null || _aidMemory == null || _aidMemory.HasUnattestedHistoricalNativeAid || !_aidMemory.Promises.Any(p => p.Status == AidPledgeStatus.Delivered) || !NativeInventoryContract.IsStrictSoloHost(out reason) || !VerifySavedArchive(c, c.OriginalPath))
                    return;
                var saved = AidPromiseLedgerCodec.SnapshotForSave(_aidMemory, _mapFingerprint, _rosterFingerprint, Math.Max(1, _lastProcessedDay));
                MapArchive archive;
                if (!MapArchive.TryLoad(c.OriginalPath, out archive) || archive == null)
                    return;
                using (archive)
                {
                    var actual = DiplomacyStateCodec.Deserialize(archive.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true), null);
                    if (actual == null || actual.AidMemory == null || !ExistingSaveReceiptGate.ExactBytes(AidPromiseLedgerCodec.Encode(actual.AidMemory), AidPromiseLedgerCodec.Encode(saved)))
                        return;
                }

                bool written = NativeAidDurabilityProbe.TrySeal(c.OriginalPath, c.File.SavedSha256, saved, out reason);
                Log("aid_durability", "status=" + (written ? "original_save_history_receipt_sealed" : "original_save_history_receipt_unavailable") + "|reason=" + Safe(reason));
            }
            catch (Exception e)
            {
                try
                {
                    Log("aid_durability", "status=original_save_history_receipt_unavailable|reason=" + Safe(e.GetType().Name));
                }
                catch
                {
                }
            }
        }

        private bool TryRestoreOriginalAidHistory()
        {
            if (_aidMemory == null || !_aidMemory.HasUnattestedHistoricalNativeAid)
                return true;
            if (_unattestedSavedAid || !_mapActive || _mapDisabled || _mapBootstrapPending || _nativeTransferProbe != null || _pendingPackage != null)
                return false;
            try
            {
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return false;
                using (lease)
                {
                    string reason;
                    if (!NativeInventoryContract.IsStrictSoloHost(out reason))
                        return false;
                    var archive = GameMapArchiveManagerAPI.Instance;
                    if (archive == null || !archive.IsCurrentFileSaveFile() || string.IsNullOrEmpty(archive.GetCurrentFilePath()) || string.IsNullOrEmpty(_loadedSavePath) || string.IsNullOrEmpty(_loadedAidSaveSha) || string.IsNullOrEmpty(_loadedAidPrimarySha) || !string.Equals(Path.GetFullPath(archive.GetCurrentFilePath()), Path.GetFullPath(_loadedSavePath), StringComparison.OrdinalIgnoreCase) || DisposableGameScope.HashFile(_loadedSavePath) != _loadedAidSaveSha)
                        return false;
                    MapArchive loaded;
                    if (!MapArchive.TryLoad(_loadedSavePath, out loaded) || loaded == null)
                        return false;
                    using (loaded)
                    {
                        byte[] primary = loaded.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true);
                        if (primary == null)
                            return false;
                        using (var h = SHA256.Create())
                            if (BitConverter.ToString(h.ComputeHash(primary)).Replace("-", "").ToLowerInvariant() != _loadedAidPrimarySha)
                                return false;
                        var state = DiplomacyStateCodec.Deserialize(primary, null);
                        if (state == null || state.AidMemory == null || !ExistingSaveReceiptGate.ExactBytes(AidPromiseLedgerCodec.Encode(state.AidMemory), AidPromiseLedgerCodec.Encode(_aidMemory)))
                            return false;
                    }

                    NativeAidDurabilityProbe.Receipt receipt;
                    if (!NativeAidDurabilityProbe.TryRead(_loadedSavePath, _aidMemory, out receipt, out reason))
                    {
                        try
                        {
                            Log("aid_durability", "status=historical_aid_waiting_for_original_receipt|reason=" + Safe(reason));
                        }
                        catch
                        {
                        }

                        return false;
                    }

                    bool restored = _aidMemory.RestoreOriginalHistory(receipt);
                    if (restored)
                        try
                        {
                            Log("aid_durability", "status=exact_original_saved_aid_history_reattested|day=" + _aidMemory.Day);
                        }
                        catch
                        {
                        }

                    return restored;
                }
            }
            catch (Exception e)
            {
                try
                {
                    Log("aid_durability", "status=historical_aid_receipt_waiting|reason=" + Safe(e.GetType().Name));
                }
                catch
                {
                }

                return false;
            }
        }
    }
}
#endif
