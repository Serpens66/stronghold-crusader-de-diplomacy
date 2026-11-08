// explicit phase boundary in addition to the recursive original engine monitor.
// Runtime settlement is NOT per-trade disk durability. Only a verified completed save
// may publish a new save file. This core does not grant native/host authority.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class AtomicBoundary
    {
        private enum Phase
        {
            Idle,
            Mutation,
            Saving,
            Quarantine
        }

        private readonly object engine;
        private Phase phase;
        private long serial;
        private object current;
        internal AtomicBoundary(object originalEngineMonitor)
        {
            if (originalEngineMonitor == null)
                throw new ArgumentNullException("originalEngineMonitor");
            engine = originalEngineMonitor;
        }

        private void Held()
        {
            if (!Monitor.IsEntered(engine))
                throw new InvalidOperationException("Original engine monitor not held");
        }

        internal bool Quarantined
        {
            get
            {
                Held();
                return phase == Phase.Quarantine;
            }
        }

        internal bool MutationActive
        {
            get
            {
                Held();
                return phase == Phase.Mutation;
            }
        }

        internal bool SaveActive
        {
            get
            {
                Held();
                return phase == Phase.Saving;
            }
        }

        internal bool TryBeginMutation(out MutationScope scope)
        {
            Held();
            scope = null;
            if (phase != Phase.Idle || serial == long.MaxValue)
                return false;
            var candidate = new MutationScope(this, checked(serial + 1));
            serial++;
            current = candidate;
            phase = Phase.Mutation;
            scope = candidate;
            return true;
        }

        internal bool TryBeginSave(byte[] payload, out SaveScope scope)
        {
            Held();
            scope = null;
            if (phase == Phase.Saving)
            {
                ((SaveScope)current).Poison();
                return false;
            }

            if (phase != Phase.Idle || serial == long.MaxValue)
                return false;
            if (payload == null || payload.Length == 0 || payload.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Save payload size invalid");
            var candidate = new SaveScope(this, checked(serial + 1), (byte[])payload.Clone());
            serial++;
            current = candidate;
            phase = Phase.Saving;
            scope = candidate;
            return true;
        }

        internal abstract class Scope : IDisposable
        {
            protected readonly AtomicBoundary owner;
            private readonly int thread;
            private readonly long serial;
            protected bool disposed;
            protected Scope(AtomicBoundary owner, long serial)
            {
                this.owner = owner;
                this.serial = serial;
                thread = Environment.CurrentManagedThreadId;
            }

            protected void Verify()
            {
                owner.Held();
                if (disposed || thread != Environment.CurrentManagedThreadId || !Object.ReferenceEquals(owner.current, this) || owner.serial != serial)
                    throw new InvalidOperationException("Stale or cross-thread operation scope");
            }

            protected abstract bool SafeToLeave { get; }

            public void Dispose()
            {
                if (disposed)
                    return;
                Verify();
                bool safe = SafeToLeave;
                disposed = true;
                owner.current = null;
                owner.phase = safe ? Phase.Idle : Phase.Quarantine;
            }
        }

        internal sealed class MutationScope : Scope
        {
            private bool touched, settled;
            internal MutationScope(AtomicBoundary o, long s) : base(o, s)
            {
            }

            // A new write after an earlier settled leg revokes that settlement.
            // Only an explicit fresh MarkSettled after the LAST native write can
            // authorize leaving the original engine inventory section.
            internal void BeforeNativeWrite()
            {
                Verify();
                touched = true;
                settled = false;
            }

            internal void MarkSettled()
            {
                Verify();
                settled = true;
            }

            protected override bool SafeToLeave
            {
                get
                {
                    return !touched || settled;
                }
            }
        }

        internal sealed class SaveScope : Scope
        {
            private readonly byte[] payload;
            private int publicationState, issued; // 0=open, 1=poisoned, 2=sealed
            internal SaveScope(AtomicBoundary o, long s, byte[] p) : base(o, s)
            {
                payload = p;
            }

            // Poison is cross-thread lock-free. A poisoned nested save ALWAYS beats a
            // later attempted seal. Once sealed, new nested saves remain refused, but
            // they cannot retroactively revoke an already linearized publication.
            internal void Poison()
            {
                Interlocked.CompareExchange(ref publicationState, 1, 0);
            }

            internal byte[] CopyForCallback()
            {
                try
                {
                    Verify();
                    if (Volatile.Read(ref publicationState) != 0)
                        throw new InvalidOperationException("Save callback after poison or publication seal");
                    var copy = (byte[])payload.Clone();
                    Interlocked.Increment(ref issued);
                    return copy;
                }
                catch
                {
                    Poison();
                    throw;
                }
            }

            internal bool CallbackWasIssued
            {
                get
                {
                    Verify();
                    return Volatile.Read(ref issued) > 0;
                }
            }

            internal bool IsValid
            {
                get
                {
                    Verify();
                    return Volatile.Read(ref publicationState) != 1;
                }
            }

            // Called exactly once on the original engine monitor immediately before
            // File.Replace/File.Move. A concurrent Poison and Seal have one atomic
            // order, so invalidating callbacks cannot pass the publication boundary.
            internal bool TrySealForPublication()
            {
                Verify();
                if (Volatile.Read(ref issued) == 0)
                    return false;
                return Interlocked.CompareExchange(ref publicationState, 2, 0) == 0;
            }

            internal bool Matches(byte[] candidate)
            {
                Verify();
                if (candidate == null || candidate.Length != payload.Length)
                    return false;
                int diff = 0;
                for (int i = 0; i < payload.Length; i++)
                    diff |= payload[i] ^ candidate[i];
                return diff == 0;
            }

            protected override bool SafeToLeave
            {
                get
                {
                    return true;
                }
            } // A failed SAVE did not mutate the game.
        }
    }

    // File transaction: native writes go only to our newly created temporary file.
    // Existing saves are promoted with File.Replace only after original success,
    // exact archive-payload verification and Flush(true). Failed verification never
    // replaces the user's old save. No symlink/reparse traversal is accepted.
    internal sealed class AtomicSaveFile : IDisposable
    {
        internal readonly string Destination, TemporaryPath;
        private readonly string backup;
        private bool disposed, committed;
        // complete the file transaction at most ONCE, even after refusal.
        private int completionAttempted;
        internal string SavedSha256 { get; private set; }
        internal long SavedBytes { get; private set; }

        internal bool Committed
        {
            get
            {
                return committed;
            }
        }

        internal AtomicSaveFile(string allowedRoot, string destination)
        {
            if (string.IsNullOrWhiteSpace(allowedRoot) || string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Missing save root/path");
            string root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Destination = Path.GetFullPath(destination);
            if (!Destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root) || Path.GetFileName(Destination).Length == 0)
                throw new InvalidOperationException("Save outside verified root");
            string directory = Path.GetDirectoryName(Destination);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException("Native save directory must already exist");
            for (string p = directory; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Reparse directory cannot own a native save");
            // Check ancestors above allowedRoot too; a junction higher in the
            // profile path must not redirect the owned temporary file elsewhere.
            }

            if (File.Exists(Destination) && (File.GetAttributes(Destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Reparse save destination refused");
            string token = Guid.NewGuid().ToString("N");
            string extension = Path.GetExtension(Destination);
            TemporaryPath = Path.Combine(directory, ".new-" + token + extension);
            backup = Path.Combine(directory, ".old-" + token + extension);
            using (var f = new FileStream(TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                f.Flush(true);
        }

        // Legacy offline callers exercise the same physical-file invariants; the
        // actual game bridge always passes its explicit publication seal callback.
        internal bool Complete(bool originalSaveSucceeded, Func<string, bool> verifyArchive, Func<bool> verifyRuntime, out string failure)
        {
            return Complete(originalSaveSucceeded, verifyArchive, verifyRuntime, () => true, out failure);
        }

        internal bool Complete(bool originalSaveSucceeded, Func<string, bool> verifyArchive, Func<bool> verifyRuntime, Func<bool> sealForPublication, out string failure)
        {
            failure = "save_not_completed";
            if (disposed || committed || Interlocked.CompareExchange(ref completionAttempted, 1, 0) != 0)
                throw new InvalidOperationException("Save file token was already used");
            if (!originalSaveSucceeded || verifyArchive == null || verifyRuntime == null || sealForPublication == null)
                return false;
            string operation = "initial_file_check";
            try
            {
                if (!File.Exists(TemporaryPath))
                {
                    failure = "native_save_missing";
                    return false;
                }

                var info = new FileInfo(TemporaryPath);
                if (info.Length < 1 || info.Length > 128000000L)
                {
                    failure = "native_save_size_invalid";
                    return false;
                }

                if (!verifyRuntime())
                {
                    failure = "save_runtime_changed";
                    return false;
                }

                // Flush the exact engine-produced temporary file while we own it.
                // Then keep a READ share guard over one file object during archive
                // verification and both digest passes. This disallows a same-sized
                // overwrite inside verifyArchive from becoming an unverified promoted
                // save; even a verifier that returns true after mutating bytes is denied.
                operation = "exclusive_flush";
                using (var flush = OpenExclusiveForFlush())
                {
                    if (flush.Length != info.Length)
                    {
                        failure = "native_save_length_changed";
                        return false;
                    }

                    flush.Flush(true);
                }

                operation = "read_guard";
                using (var observed = new FileStream(TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (observed.Length != info.Length)
                    {
                        failure = "native_save_length_changed";
                        return false;
                    }

                    byte[] before, after;
                    using (var h = SHA256.Create())
                        before = h.ComputeHash(observed);
                    operation = "archive_verification";
                    if (!verifyArchive(TemporaryPath))
                    {
                        failure = "native_mod_archive_mismatch";
                        return false;
                    }

                    observed.Position = 0;
                    using (var h = SHA256.Create())
                        after = h.ComputeHash(observed);
                    int digestDifference = 0;
                    for (int i = 0; i < before.Length; i++)
                        digestDifference |= before[i] ^ after[i];
                    if (observed.Length != info.Length || digestDifference != 0)
                    {
                        failure = "save_bytes_changed_during_archive_verification";
                        return false;
                    }

                    if (!verifyRuntime())
                    {
                        failure = "save_runtime_changed_after_verification";
                        return false;
                    }

                    SavedBytes = observed.Length;
                    SavedSha256 = BitConverter.ToString(after).Replace("-", "").ToLowerInvariant();
                }

                // Windows path/handle replacement AFTER the read handle is closed
                // still requires original-engine integration and crash/fault tests.
                // This bounded guard prevents verifier-time changes, not all TOCTOU.
                if (!verifyRuntime())
                {
                    failure = "save_runtime_changed_before_publication";
                    return false;
                }

                // Last check: atomically seal the ORIGINAL engine-owned save scope.
                // Cross-thread poison before this point prevents all file replacement.
                // Poison after this point can only reject a different nested save.
                if (!sealForPublication())
                {
                    failure = "save_publication_seal_refused";
                    return false;
                }

                operation = "atomic_publication";
                bool replace = File.Exists(Destination);
                for (int retry = 0;; retry++)
                {
                    try
                    {
                        if (replace)
                            File.Replace(TemporaryPath, Destination, backup);
                        else
                            File.Move(TemporaryPath, Destination);
                        break;
                    }
                    catch (IOException busy)
                    {
                        // A scanner/read handle may briefly deny DELETE after verification.
                        // Only Windows sharing/lock conflicts qualify. Keep the exact sealed
                        // scope, same file and original monitor; never create a second save.
                        if (!SharingConflict(busy) || retry >= 39)
                            throw;
                        Thread.Sleep(25);
                        if (!verifyRuntime())
                        {
                            failure = "save_runtime_changed_during_publication_wait";
                            return false;
                        }
                    }
                }

                // Linearization point: cleanup and diagnostics must not turn success into
                // a failure report after the new complete file replaced the old one.
                committed = true;
                failure = null;
                try
                {
                    if (File.Exists(backup))
                        File.Delete(backup);
                }
                catch
                {
                } // owned old-save copy only

                return true;
            }
            catch (Exception e)
            {
                failure = "save_file_transaction_" + operation + "_" + e.GetType().Name + "_win32_" + (e.HResult & 0xffff);
                return false;
            }
        }

        private static bool SharingConflict(IOException error)
        {
            int code = error.HResult & 0xffff;
            return code == 32 || code == 33;
        }

        private FileStream OpenExclusiveForFlush()
        {
            for (int retry = 0;; retry++)
            {
                try
                {
                    return new FileStream(TemporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException busy)
                {
                    if (!SharingConflict(busy) || retry >= 19)
                        throw;
                    Thread.Sleep(25);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                if (File.Exists(TemporaryPath))
                    File.Delete(TemporaryPath);
            }
            catch
            {
            }
        // Keep the previous save as an explicit rollback artifact. It is not a
        // second journal and can never be loaded as authority automatically.
        }
    }
}
