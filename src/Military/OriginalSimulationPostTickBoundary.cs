// Private, unarmed original managed simulation-thread observation boundary.
// No startup registration, no native transport, no order/ACK/success grant.
// Root may separately consider an exact Harmony transpiler only with proven
// safe installation/quiescence and original session authorization.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;

namespace StrongholdEuropeDE.Runtime
{
    // This class deliberately has NO Harmony.Patch, hook installation, native
    // import, GameAction, SDK order, raw tick callback or background worker.
    internal static class OriginalSimulationPostTickBoundary
    {
        internal const string ModAuthor = "DiplomacyMod";
        // This stamp contains NO consulted-donor or requester identity.
        // Those remain separately authenticated mod support intent facts; a stamp
        // never impersonates either AI or the original local human.
        internal const bool NativeOrderAuthority = false;
        internal const bool OriginalCPURequested = false;
        internal const bool NativeAck = false;
        internal const bool MilitarySuccess = false;
        internal const int FavorCredit = 0;
        internal const bool InstalledByThisClass = false;

        private const string ManagedSha256 =
            "bc8b6a395f01d48557db413600c8dd8d1fdfd3abdf97bfbbb68a3c56b04fd789";
        private const string OriginalRunIlSha256 =
            "6fea4a9adab7704908229e83f789be9953e96cf4499e516392e73bab1e6be162";
        private const int RunToken = 0x060000FD;
        private const int DllRunTickToken = 0x06000099;
        private const string OriginalMvid =
            "29ade4ef-f587-422f-a1eb-f81dff066e0a";

        // A pure observation scope is NOT a native dispatch capability.
        // New scopes invalidate previous generation/epoch/cancellation counts.
        private sealed class Scope
        {
            internal readonly object Owner;
            internal readonly object OwnerGate;
            internal readonly long Generation, MapEpoch, CancellationSerial;
            internal readonly string MapSha256;
            internal long CompletedOriginalCalls;

            internal Scope(object owner, object gate, long generation,
                long epoch, long cancellation, string mapSha)
            {
                Owner = owner;
                OwnerGate = gate;
                Generation = generation;
                MapEpoch = epoch;
                CancellationSerial = cancellation;
                MapSha256 = mapSha;
            }
        }

        // Process-lifetime high-water only, never a second active owner.
        // No Owner or OwnerGate reference survives Detach in this tombstone.
        // One newest stamp suffices because generation/epoch/cancellation must
        // advance lexicographically; memory use is constant over any map count.
        private sealed class RevokedScopeStamp
        {
            internal readonly long Generation, MapEpoch, CancellationSerial;
            internal readonly string MapSha256;

            internal RevokedScopeStamp(Scope scope)
            {
                Generation = scope.Generation;
                MapEpoch = scope.MapEpoch;
                CancellationSerial = scope.CancellationSerial;
                MapSha256 = scope.MapSha256;
            }
        }

        private static Scope _current;
        // Serializes bind/detach across different owner gates. Both locks are
        // TryEnter(0); the simulation callback takes neither lock. Mutations
        // of _current and _revokedScope form one ordered owner transition.
        private static readonly object _scopeTransitionGate = new object();
        private static RevokedScopeStamp _revokedScope;
        // Set only by Harmony's PRIVATE patch-time IL validator. Never means
        // installed hook or native authority; no registration is provided.
        private static int _transpilerContractChecked;
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
        // 0=never installed, 1=enabled, 2=permanently disabled.
        // An uncertain partial Patch is never eligible for a passive owner.
        private static int _observerInstallState;

        internal static bool TryEnableAfterInitialManagedInstall()
        {
            return Volatile.Read(ref _transpilerContractChecked) == 1 &&
                Interlocked.CompareExchange(ref _observerInstallState, 1, 0) == 0;
        }

        internal static void DisableForProcess()
        {
            Interlocked.Exchange(ref _observerInstallState, 2);
        }
#endif

        private static bool ValidMapSha(string digest)
        {
            return digest != null && digest.Length == 64 &&
                digest.All(c => c >= '0' && c <= '9' ||
                                 c >= 'a' && c <= 'f');
        }

        private static bool ExactMethodBody(MethodBase method)
        {
            MethodInfo target = method as MethodInfo;
            if (target == null || !target.IsStatic ||
                target.MetadataToken != RunToken || target.Name != "run" ||
                target.DeclaringType == null ||
                target.DeclaringType.FullName != "EngineInterface" ||
                target.ReturnType != typeof(int))
                return false;
            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 1 ||
                parameters[0].ParameterType != typeof(bool) ||
                target.Module.ModuleVersionId.ToString() != OriginalMvid)
                return false;

            MethodBody body = target.GetMethodBody();
            if (body == null || body.LocalVariables.Count <= 5 ||
                body.ExceptionHandlingClauses.Count != 1)
                return false;
            ExceptionHandlingClause clause = body.ExceptionHandlingClauses[0];
            if (clause.Flags != ExceptionHandlingClauseOptions.Finally ||
                clause.TryOffset != 0x001A ||
                clause.TryLength != 0x0273 ||
                clause.HandlerOffset != 0x028D ||
                clause.HandlerLength != 0x000A)
                return false;

            byte[] il = body.GetILAsByteArray();
            if (il == null || il.Length != 0x029C ||
                il[0x001D] != 0x28 || il[0x001E] != 0x6E ||
                il[0x001F] != 0x00 || il[0x0020] != 0x00 ||
                il[0x0021] != 0x0A ||
                il[0x0222] != 0x28 || il[0x0223] != 0x99 ||
                il[0x0224] != 0x00 || il[0x0225] != 0x00 ||
                il[0x0226] != 0x06 ||
                il[0x0227] != 0x13 || il[0x0228] != 0x05 ||
                il[0x0229] != 0x14 ||
                il[0x0291] != 0x28 || il[0x0292] != 0x6F ||
                il[0x0293] != 0x00 || il[0x0294] != 0x00 ||
                il[0x0295] != 0x0A)
                return false;

            using (SHA256 digest = SHA256.Create())
            {
                string actual = BitConverter.ToString(
                    digest.ComputeHash(il)).Replace("-", "").ToLowerInvariant();
                return String.Equals(actual, OriginalRunIlSha256,
                    StringComparison.Ordinal);
            }
        }

        // Metadata-only preparation; caller may inspect this method, but this
        // class never activates it. A verified future method CALL can reach a
        // late Harmony detour: Director.runSimTickTest loops IL_0157 -> IL_0048.
        // Safe live installation while a frame is actively executing is NOT
        // established here; Root must independently prove quiescence first.
        internal static bool ExactOriginalMethodContract(MethodInfo candidate)
        {
            try { return ExactMethodBody(candidate); }
            catch { return false; }
        }

        private static bool IsLocalFive(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Stloc_S)
                return false;
            LocalBuilder builder = instruction.operand as LocalBuilder;
            if (builder != null)
                return builder.LocalIndex == 5;
            if (instruction.operand is byte)
                return (byte)instruction.operand == 5;
            if (instruction.operand is int)
                return (int)instruction.operand == 5;
            if (instruction.operand is short)
                return (short)instruction.operand == 5;
            return false;
        }

        private static bool IsOriginalNativeTick(CodeInstruction instruction)
        {
            MethodInfo native = instruction.operand as MethodInfo;
            return instruction.opcode == OpCodes.Call && native != null &&
                native.MetadataToken == DllRunTickToken &&
                native.Module.ModuleVersionId.ToString() == OriginalMvid &&
                native.DeclaringType != null &&
                native.DeclaringType.FullName == "EngineInterface" &&
                native.Name == "DLL_RunTick" &&
                native.ReturnType == typeof(int);
        }

        // Unregistered by default. This is the ONLY allowed IL delta:
        //   IL_0222 call DLL_RunTick (int32)
        //   IL_0227 stloc.s 5
        //   [call void AfterOriginalDllRunTick()]  <- already inside try
        //   IL_0229 ldnull
        // It adds no stack operands, moves no labels or EH blocks, and
        // cannot intercept, skip, reorder or replace original native work.
        internal static IEnumerable<CodeInstruction> TranspileUnarmed(
            IEnumerable<CodeInstruction> source, MethodBase original)
        {
            if (source == null || !ExactMethodBody(original))
                throw new InvalidOperationException("original_run_CIL_not_attested");
            var instructions = source.ToList();
            int matchedIndex = -1, matches = 0;
            for (int i = 0; i + 2 < instructions.Count; ++i)
            {
                if (!IsOriginalNativeTick(instructions[i]))
                    continue;
                ++matches;
                if (!IsLocalFive(instructions[i + 1]) ||
                    instructions[i + 2].opcode != OpCodes.Ldnull)
                    throw new InvalidOperationException(
                        "original_posttick_IL_boundary_mismatch");
                matchedIndex = i;
            }
            if (matches != 1 || matchedIndex < 0)
                throw new InvalidOperationException("original_tick_call_not_unique");

            CodeInstruction following = instructions[matchedIndex + 2];
            // No branch label or EH transition may point into this seam.
            // Labels, handlers and all original CodeInstructions are untouched.
            if (following.labels == null || following.labels.Count != 0 ||
                following.blocks == null || following.blocks.Count != 0)
                throw new InvalidOperationException("posttick_label_or_EH_conflict");
            var stampMethod = typeof(OriginalSimulationPostTickBoundary)
                .GetMethod("AfterOriginalDllRunTick",
                    BindingFlags.NonPublic | BindingFlags.Static);
            if (stampMethod == null || stampMethod.ReturnType != typeof(void) ||
                stampMethod.GetParameters().Length != 0)
                throw new InvalidOperationException("managed_stamp_not_attested");

            instructions.Insert(matchedIndex + 2,
                new CodeInstruction(OpCodes.Call, stampMethod));
            Volatile.Write(ref _transpilerContractChecked, 1);
            return instructions;
        }

        // Simulation lane: primitive stamp ONLY, never lock, allocate, log,
        // read native state, invoke a writer or trust a wrapper result.
        // Every managed exception is contained before original finally.
        private static void AfterOriginalDllRunTick()
        {
            try
            {
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                if (Volatile.Read(ref _observerInstallState) != 1)
                    return;
#endif
                Scope current = Volatile.Read(ref _current);
                if (current != null)
                    Interlocked.Increment(ref current.CompletedOriginalCalls);
            }
            catch { }
        }

        private static bool StrictlyNewerThanRevoked(Scope proposed,
            RevokedScopeStamp revoked)
        {
            if (revoked == null)
                return true;
            if (proposed.Generation != revoked.Generation)
                return proposed.Generation > revoked.Generation;
            if (proposed.MapEpoch != revoked.MapEpoch)
                return proposed.MapEpoch > revoked.MapEpoch;
            // Changing a map digest without advancing generation or epoch
            // cannot launder a stale scope with a larger cancellation serial.
            return String.Equals(proposed.MapSha256, revoked.MapSha256,
                       StringComparison.Ordinal) &&
                   proposed.CancellationSerial > revoked.CancellationSerial;
        }

        // Optional future owner binding, NOT any authorization.
        // Call ONLY from the current Unity owner; never from the simulation
        // lane. Acquire owner gate nonblocking; it is not threadLock.
        internal static bool TryBindPassiveOwner(object owner, object ownerGate,
            long generation, long mapEpoch, long cancellation,
            string mapIdentitySha256, string originalManagedSha256)
        {
            if (owner == null || ownerGate == null || generation < 1 ||
                mapEpoch < 1 || cancellation < 0 ||
                !ValidMapSha(mapIdentitySha256) ||
                originalManagedSha256 != ManagedSha256 ||
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                Volatile.Read(ref _observerInstallState) != 1 ||
#endif
                Volatile.Read(ref _transpilerContractChecked) != 1)
                return false;

            // Construct before taking the owner gate to avoid allocations
            // while it is held; original sim lane never enters this method.
            var proposed = new Scope(owner, ownerGate, generation, mapEpoch,
                cancellation, mapIdentitySha256);
            bool entered = false, transitionEntered = false;
            try
            {
                Monitor.TryEnter(ownerGate, 0, ref entered);
                if (!entered)
                    return false;
                // Cross-owner transitions are serialized without waiting;
                // the simulation callback never acquires either gate.
                Monitor.TryEnter(_scopeTransitionGate, 0,
                    ref transitionEntered);
                if (!transitionEntered ||
                    !StrictlyNewerThanRevoked(proposed, _revokedScope))
                    return false;
                Scope prior = Volatile.Read(ref _current);
                if (prior != null)
                {
                    if (!Object.ReferenceEquals(prior.Owner, owner) ||
                        !Object.ReferenceEquals(prior.OwnerGate, ownerGate) ||
                        generation < prior.Generation ||
                        generation == prior.Generation && mapEpoch < prior.MapEpoch ||
                        generation == prior.Generation && mapEpoch == prior.MapEpoch &&
                            cancellation < prior.CancellationSerial)
                        return false;
                    if (generation == prior.Generation &&
                        mapEpoch == prior.MapEpoch)
                    {
                        if (!String.Equals(prior.MapSha256, mapIdentitySha256,
                                StringComparison.Ordinal))
                            return false;
                        // An identical registration must not erase valid
                        // post-tick stamps or resurrect a cancelled scope.
                        if (cancellation == prior.CancellationSerial)
                            return true;
                    }
                }
                return Object.ReferenceEquals(
                    Interlocked.CompareExchange(ref _current, proposed, prior), prior);
            }
            catch { return false; }
            finally
            {
                if (transitionEntered)
                    Monitor.Exit(_scopeTransitionGate);
                if (entered)
                    Monitor.Exit(ownerGate);
            }
        }

        // Unity/owner lane only. Never wait for the owner gate under the
        // native engine monitor. An observation is NOT a native permit.
        internal static bool TryObserveOnly(object owner, object ownerGate,
            long generation, long epoch, long cancellation,
            string mapIdentitySha256, long lastSeen, out long observed)
        {
            observed = 0;
            if (owner == null || ownerGate == null || generation < 1 ||
                epoch < 1 || cancellation < 0 || lastSeen < 0 ||
                !ValidMapSha(mapIdentitySha256))
                return false;
            bool entered = false;
            try
            {
                Monitor.TryEnter(ownerGate, 0, ref entered);
                if (!entered)
                    return false;
                Scope scope = Volatile.Read(ref _current);
                if (scope == null ||
                    !Object.ReferenceEquals(scope.Owner, owner) ||
                    !Object.ReferenceEquals(scope.OwnerGate, ownerGate) ||
                    scope.Generation != generation || scope.MapEpoch != epoch ||
                    scope.CancellationSerial != cancellation ||
                    !String.Equals(scope.MapSha256, mapIdentitySha256,
                        StringComparison.Ordinal))
                    return false;
                long value = Interlocked.Read(ref scope.CompletedOriginalCalls);
                if (value <= lastSeen || value <= 0)
                    return false;
                if (!Object.ReferenceEquals(Volatile.Read(ref _current), scope))
                    return false;
                observed = value;
                return true;
            }
            catch { return false; }
            finally
            {
                if (entered)
                    Monitor.Exit(ownerGate);
            }
        }

        // Local observation cleanup only. Never cancel an Original order.
        // Retain just the highest revoked immutable identity across Detach,
        // owner changes, in-process Cold/map reloads and later rebinds.
        internal static bool TryDetachPassiveOwner(object owner, object ownerGate)
        {
            if (owner == null || ownerGate == null)
                return false;
            bool entered = false, transitionEntered = false;
            try
            {
                Monitor.TryEnter(ownerGate, 0, ref entered);
                if (!entered)
                    return false;
                Monitor.TryEnter(_scopeTransitionGate, 0,
                    ref transitionEntered);
                if (!transitionEntered)
                    return false;
                Scope prior = Volatile.Read(ref _current);
                if (prior == null ||
                    !Object.ReferenceEquals(prior.Owner, owner) ||
                    !Object.ReferenceEquals(prior.OwnerGate, ownerGate) ||
                    !StrictlyNewerThanRevoked(prior, _revokedScope))
                    return false;
                // Allocate before the CAS; a failed detach cannot lose its
                // previously installed watermark or mark an active scope dead.
                var revoked = new RevokedScopeStamp(prior);
                if (!Object.ReferenceEquals(
                        Interlocked.CompareExchange(ref _current, null, prior),
                        prior))
                    return false;
                // The transition gate excludes every concurrent bind until
                // after this publication. No owner/gate reference is retained.
                Volatile.Write(ref _revokedScope, revoked);
                return true;
            }
            catch { return false; }
            finally
            {
                if (transitionEntered)
                    Monitor.Exit(_scopeTransitionGate);
                if (entered)
                    Monitor.Exit(ownerGate);
            }
        }
    }
}
#endif
