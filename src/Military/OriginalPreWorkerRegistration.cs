// Disabled unless three independent private build gates are explicitly selected.
// No new Harmony owner, no timer, no native command and no second run-method patch.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalPreWorkerRegistration
    {
        // 0 = not attempted, 1 = patch may be partially applied, 2 = applied.
        // Once nonzero, no unsafe retry or live teardown rewrite is permitted.
        private static int _attempt;
        private static string _lastFinding = "not_checked";

        internal static bool MustRetainRunPatch
        {
            get { return Volatile.Read(ref _attempt) != 0; }
        }

        internal static string LastFinding
        {
            get { return Volatile.Read(ref _lastFinding); }
        }

        private static bool HashMatches(string path, string expected)
        {
            if (String.IsNullOrEmpty(path))
                return false;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                {
                    return String.Equals(BitConverter.ToString(
                        sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(),
                        expected, StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        private static bool ExactBody(MethodBase method, int token, string ilSha)
        {
            if (method == null || method.MetadataToken != token)
                return false;
            MethodBody body = method.GetMethodBody();
            if (body == null)
                return false;
            using (SHA256 sha = SHA256.Create())
                return String.Equals(BitConverter.ToString(
                    sha.ComputeHash(body.GetILAsByteArray())).Replace("-", "")
                    .ToLowerInvariant(), ilSha, StringComparison.Ordinal);
        }

        private static bool ExactField(Type t, string name,
            BindingFlags flags, int token, Type fieldType, out FieldInfo field)
        {
            field = t.GetField(name, flags | BindingFlags.DeclaredOnly);
            return field != null && field.MetadataToken == token &&
                field.FieldType == fieldType;
        }

        private static bool ActualUnityMainThread()
        {
            // Unity's installed InitializeSynchronizationContext constructs
            // the context from Thread.CurrentThread.ManagedThreadId.
            // Merely being inside a method named Awake is not authority.
            Assembly core = typeof(Application).Assembly;
            if (core.ManifestModule.ModuleVersionId.ToString() !=
                "806021f9-55ec-43bb-97cd-1adca41e00da" ||
                !HashMatches(core.Location,
                    "e468e19789d608b41e2c7fe4f367c7082fece7d1883f568e75870f6c5384cc86"))
                return false;

            Type type = core.GetType("UnityEngine.UnitySynchronizationContext", false);
            SynchronizationContext current = SynchronizationContext.Current;
            FieldInfo mainId;
            MethodInfo initializer = type == null ? null : type.GetMethod(
                "InitializeSynchronizationContext",
                BindingFlags.NonPublic | BindingFlags.Static |
                BindingFlags.DeclaredOnly);
            if (type == null || current == null ||
                current.GetType() != type ||
                !ExactBody(initializer, 0x060017F2,
                    "35837ed1e3c04fc7424321507f099c3c88d01b6d82b24540c35dff9a6cfbdf7e") ||
                !ExactField(type, "m_MainThreadID",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    0x04000831, typeof(int), out mainId))
                return false;

            object assigned = mainId.GetValue(current);
            return assigned is int &&
                (int)assigned == Thread.CurrentThread.ManagedThreadId &&
                (int)assigned > 0;
        }

        private static bool ExactDetourRuntime()
        {
            Assembly harmony = typeof(Harmony).Assembly;
            if (harmony.GetName().Version.ToString() != "2.9.0.0" ||
                harmony.ManifestModule.ModuleVersionId.ToString() !=
                    "474744d6-5d8e-460f-a08c-d5fd82b5d65f" ||
                !HashMatches(harmony.Location,
                    "1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031"))
                return false;

            // Harmony's earlier existing patches have already caused the
            // matching MonoMod runtime to load. Never load it speculatively.
            Assembly monoMod = null;
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                if (loaded.GetName().Name == "MonoMod.RuntimeDetour")
                {
                    if (monoMod != null)
                        return false;
                    monoMod = loaded;
                }
            return monoMod != null &&
                monoMod.GetName().Version.ToString() == "22.1.29.1" &&
                monoMod.ManifestModule.ModuleVersionId.ToString() ==
                    "4e2760c7-517c-4ea7-9c63-3d67e84b319f" &&
                HashMatches(monoMod.Location,
                    "40e49bb314391cd7bddc2644f8553eeba92c194b940836b103df16955c464e0c");
        }

        private static bool ExactDirectorSourceAndFields(
            out FieldInfo instance, out FieldInfo simThread,
            out FieldInfo fixedTimer, out FieldInfo simRunning)
        {
            instance = null;
            simThread = null;
            fixedTimer = null;
            simRunning = null;
            Type director = typeof(Director);
            BindingFlags declared = BindingFlags.DeclaredOnly;
            BindingFlags privateInstance = BindingFlags.Instance |
                BindingFlags.NonPublic | declared;
            if (director.Module.ModuleVersionId.ToString() !=
                    "29ade4ef-f587-422f-a1eb-f81dff066e0a" ||
                !HashMatches(director.Assembly.Location,
                    "bc8b6a395f01d48557db413600c8dd8d1fdfd3abdf97bfbbb68a3c56b04fd789") ||
                !ExactBody(director.TypeInitializer, 0x06000070,
                    "0149992c43fbd10d27f488fa1292d750e91a1ea4094b477086ca12020bb9fc7d") ||
                !ExactBody(director.GetMethod("Awake", privateInstance),
                    0x06000035,
                    "05ab67b54fc82e92f351dbfbda68a1d5417855d4ab0ee5ca24fdbb36f881473d") ||
                !ExactBody(director.GetMethod("OnDestroy", privateInstance),
                    0x0600004B,
                    "b4372282cfed34d680966285739459cccc3cc50ffd4cb566aec15017566ef9cb") ||
                !ExactField(director, "instance",
                    BindingFlags.Public | BindingFlags.Static,
                    0x04000027, director, out instance) ||
                !ExactField(director, "simThread",
                    privateInstance, 0x04000042,
                    typeof(Thread), out simThread) ||
                !ExactField(director, "fixedTimer",
                    privateInstance, 0x04000050,
                    typeof(System.Threading.Timer), out fixedTimer) ||
                !ExactField(director, "simThreadRunning",
                    privateInstance, 0x04000040,
                    typeof(bool), out simRunning))
                return false;
            return true;
        }

        private static bool TryProveInitialWindow(MethodInfo target,
            out string finding)
        {
            finding = "source_or_owner_unverified";
            try
            {
                if (Volatile.Read(ref _attempt) != 0 ||
                    target == null ||
                    !OriginalSimulationPostTickBoundary
                         .ExactOriginalMethodContract(target) ||
                    !ActualUnityMainThread() || !ExactDetourRuntime())
                    return false;

                FieldInfo instance, simThread, fixedTimer, simRunning;
                if (!ExactDirectorSourceAndFields(out instance,
                        out simThread, out fixedTimer, out simRunning))
                    return false;

                // The ORIGINAL Director .cctor initializes this field null;
                // only Director.Awake writes it afterwards, at IL_0001.
                // Awake creates the timer at IL_0065 and starts simThread at
                // IL_00AA. Use raw reference, never Unity Object == null.
                object director = instance.GetValue(null);
                if (!System.Object.ReferenceEquals(director, null))
                {
                    Thread worker = simThread.GetValue(director) as Thread;
                    object timer = fixedTimer.GetValue(director);
                    bool running = (bool)simRunning.GetValue(director);
                    finding = worker == null ?
                        (timer == null ? "director_exists_no_thread_yet" :
                            "director_timer_created") :
                        (worker.IsAlive ? "director_worker_alive" :
                            "director_worker_exists_not_alive");
                    if (running)
                        finding += "_running_flag";
                    // Even an apparently unstarted/dead worker is not a
                    // first-ever absent worker; NEVER admit this branch.
                    return false;
                }

                // Reject competing Harmony owners. A later extra Patch would
                // refresh a native detour; this must be the first run hook.
                if (Harmony.GetPatchInfo(target) != null)
                {
                    finding = "run_already_has_harmony_patch";
                    return false;
                }
                finding = "unity_main_original_singleton_never_started";
                return true;
            }
            catch
            {
                finding = "metadata_or_owner_probe_failed";
                return false;
            }
        }

        internal static bool IsInstalled
        {
            get { return Volatile.Read(ref _attempt) == 2; }
        }

        internal static void RegisterFirstRunPatch(Harmony owner, MethodInfo run,
            HarmonyMethod ordinaryPrefix, HarmonyMethod ordinaryPostfix,
            HarmonyMethod ordinaryFinalizer, HarmonyMethod combinedPrefix,
            HarmonyMethod combinedFinalizer)
        {
            if (owner == null || run == null ||
                ordinaryPrefix == null || ordinaryPostfix == null ||
                ordinaryFinalizer == null || combinedPrefix == null ||
                combinedFinalizer == null)
                throw new ArgumentNullException("original_run_patch");

            string finding;
            if (!TryProveInitialWindow(run, out finding))
            {
                Volatile.Write(ref _lastFinding, finding);
                // Preserve the existing unrelated human-order hook unchanged.
                // A refusal never attempts or registers the private observer.
                owner.Patch(run, prefix: ordinaryPrefix,
                    postfix: ordinaryPostfix, finalizer: ordinaryFinalizer);
                return;
            }

            // Do this BEFORE Harmony Patch: an exception may leave a native
            // entry partially replaced. Do not retry or live-Unpatch it.
            if (Interlocked.CompareExchange(ref _attempt, 1, 0) != 0)
                throw new InvalidOperationException("run_initial_registration_reentered");
            try
            {
                MethodInfo transpiler = typeof(OriginalSimulationPostTickBoundary)
                    .GetMethod("TranspileUnarmed",
                        BindingFlags.NonPublic | BindingFlags.Static);
                if (transpiler == null ||
                    !typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(
                        transpiler.ReturnType) ||
                    transpiler.GetParameters().Length != 2)
                    throw new InvalidOperationException("postnative_transpiler_contract_missing");

                // ONE existing initial run Patch. Existing prefix/finalizer
                // are retained; there is no second late detour.
                owner.Patch(run, prefix: combinedPrefix,
                    postfix: ordinaryPostfix, finalizer: combinedFinalizer,
                    transpiler: new HarmonyMethod(transpiler), ilmanipulator: null);

                if (!OriginalSimulationPostTickBoundary
                         .TryEnableAfterInitialManagedInstall())
                    throw new InvalidOperationException("postnative_managed_install_unverified");

                Volatile.Write(ref _attempt, 2);
                Volatile.Write(ref _lastFinding, finding);
            }
            catch
            {
                OriginalSimulationPostTickBoundary.DisableForProcess();
                Volatile.Write(ref _lastFinding, "registration_failed_no_retry");
                throw;
            }
        }

        internal static void DisableAndDetach(object owner, object ownerGate)
        {
            if (!MustRetainRunPatch)
                return;
            OriginalSimulationPostTickBoundary.DisableForProcess();
            if (owner != null && ownerGate != null)
                OriginalSimulationPostTickBoundary.TryDetachPassiveOwner(
                    owner, ownerGate);
        }
    }
}
#endif
