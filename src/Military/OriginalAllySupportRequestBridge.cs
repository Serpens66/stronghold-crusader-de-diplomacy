// Private, OFF by default. A MOD-authored original ally SUPPORT REQUEST,
// never a direct tribe command and never a human/UI/original-CPU impersonation.
// No registration, hook, native call, replay or new planner occurs in this file.
// An installed exact original sim-lane owner + Chore113 priority contract is
// independently required before the dormant native transport may ever activate.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal enum OriginalSupportRequestPhase : byte
    {
        Waiting = 0,
        StagedReviewOnly = 1,
        UnarmedOriginalBoundary = 2,
        RetryableCurrentOwnerMismatch = 3,
        DispatchBeganUnconfirmed = 4,
        InvocationOutcomeUnknownNoRetry = 5,
        DuplicateOrCompleted = 6,
        OriginalTupleUnattributed = 7,
        NativeCausalResponseNotProven = 8
    }

    // Fixed by the original Windows-x64 Chore113 kind0 consumer ABI.
    // No OriginalPlayerId, native Chore slot, group, host or manual HUD inputs.
    internal struct OriginalSupportDefendArguments
    {
        internal readonly int AttackTarget, ConsultedDonorAI, DefendedRequesterAI, Opcode;
        internal OriginalSupportDefendArguments(byte consulted, byte defended)
        {
            AttackTarget = 0;
            ConsultedDonorAI = consulted;
            DefendedRequesterAI = defended;
            Opcode = 11;
        }
    }

    internal interface IOriginalSupportDefendConsumer
    {
        // A VOID return is not evaluator acceptance or native ACK.
        void RequestDefend(int attackTarget, int donorAI, int defendedAI, int opcode);
    }

    // One immutable projection of an EXISTING ModSupportIntentLifecycle nonce.
    // This object grants NO transport authority or ownership of an original AI.
    internal sealed class OriginalSupportRequestTicket
    {
        internal const string RealAuthor = ModSupportIntent.ModAuthor;
        internal readonly ulong Nonce, ReliefEventId;
        internal readonly long Generation, MapEpoch, CancellationSerial;
        internal readonly long ObservedTick, ExpiresAfterTick;
        internal readonly string MapSha256;
        internal readonly byte DonorAI, DefendedAI;
        internal readonly OriginalSupportDefendArguments Arguments;

        internal OriginalSupportRequestTicket(ModSupportIntent source)
        {
            Nonce = source.Nonce;
            ReliefEventId = source.ReliefEventId;
            Generation = source.Generation;
            MapEpoch = source.MapEpoch;
            CancellationSerial = source.CancellationSerial;
            ObservedTick = source.IssuedTick;
            ExpiresAfterTick = source.ExpiresAfterTick;
            MapSha256 = source.MapIdentitySha256;
            DonorAI = source.ConsultedDonorAI;
            DefendedAI = source.RequesterDefendedAI;
            Arguments = new OriginalSupportDefendArguments(DonorAI, DefendedAI);
        }
    }

    // Strict LIVE re-read for a future separately installed exact callback.
    // Unlike a supplied "host=true" fact it reads original live registers,
    // current native local slot, peer roster, reciprocal native alliance,
    // original tick, and official typed donor ally mode while holding the
    // REAL original threadLock. This cannot authorize a sender by itself:
    // verified native SDK memory compatibility is presently fail-closed,
    // and current map/save/Chore priority must also be attested by Root.
    internal static class OriginalSupportLiveSoloAudit
    {
        internal static unsafe bool TryVerifyUnderOriginalMonitor(
            OriginalSupportRequestTicket ticket, long originalTick)
        {
            try
            {
                string diagnostic;
                if (ticket == null ||
                    !DiplomacyFeature.IsNativeDiplomacySectionHeld ||
                    !SerpsRawraCompat.AttestedLoadedNativeCompatibility(out diagnostic))
                    return false;

                GameData current = GameData.Instance;
                EngineInterface.PlayState state =
                    current == null ? null : current.lastGameState;
                var players = GamePlayerManagerAPI.Instance;
                var clock = GameTimeManagerAPI.Instance;
                if (current == null || state == null || players == null ||
                    clock == null || MainViewModel.Instance == null ||
                    Director.instance == null || current.SkirmishGameType != 0 ||
                    current.coopTrailID != 0 || state.player_register == null ||
                    state.player_register.Length != 9 ||
                    state.computer_register == null ||
                    state.computer_register.Length != 9 ||
                    state.lord_alive == null || state.lord_alive.Length != 9 ||
                    clock.GetElapsedMapTicks() != originalTick)
                    return false;

                int local = players.GetLocalPlayerId();
                bool[] humans = new bool[9], managedAis = new bool[9],
                    nativeAis = new bool[9];
                for (int id = 1; id <= 8; ++id)
                {
                    humans[id] = state.is_valid_player(id);
                    managedAis[id] = state.is_skirmish_player(id);
                    if (humans[id] || managedAis[id])
                        nativeAis[id] = players.IsAIPlayer(id);
                }
                bool networked = GameNetworkAPI.IsNetworkedEnvironment();
                var peerList = networked ? GameNetworkAPI.GetPlayers() : null;
                SessionMember[] peers = peerList == null ? null :
                    peerList.Select(p => p == null ? null :
                        new SessionMember {
                            Slot = p.playerID, Self = p.isSelf,
                            RealIdentity = p.steamID > 1000UL
                        }).ToArray();
                string rosterReason;
                if (!SoloSessionPolicy.Allows(local, current.playerID,
                        humans, managedAis, nativeAis, peers, networked,
                        out rosterReason))
                    return false;

                int donor = ticket.DonorAI, defended = ticket.DefendedAI;
                if (donor < 1 || donor > 8 || defended < 1 ||
                    defended > 8 || donor == defended ||
                    donor == local || defended == local ||
                    humans[donor] || humans[defended] ||
                    !managedAis[donor] || !nativeAis[donor] ||
                    !managedAis[defended] || !nativeAis[defended] ||
                    state.lord_alive[donor] == 0 ||
                    state.lord_alive[defended] == 0 ||
                    !players.IsPlayerAlliedTo(donor, defended) ||
                    !players.IsPlayerAlliedTo(defended, donor))
                    return false;

                // OFFICIAL typed SDK original GamePlayerResources access only;
                // no guessed RVA, no native field write and no global CPU queue.
                GamePlayerResources* donorResources;
                if (!players.TryGetPlayerResourcesById(donor, out donorResources) ||
                    donorResources == null ||
                    donorResources->r_AIAllyOrderMode != 0)
                    return false;
                return true;
            }
            catch { return false; }
        }
    }

    // Not a user-submitted bool or caller-supplied "host" assertion.
    // Production permits only a verified exact POST-DLL_RunTick callback which
    // holds the actual original EngineInterface.threadLock on its own thread.
    // Independent exact managed-detour registration is NOT attested here.
    internal sealed class OriginalRequestSimulationOwner
    {
        internal static readonly bool ExactPostTickOwnerRegistered = false;
        private readonly object _originalMonitor;
        private readonly int _threadId;
        private readonly long _generation, _epoch, _cancellation, _tick;
        private readonly string _map;
        private readonly ulong _nonce;

        private OriginalRequestSimulationOwner(object monitor, int threadId,
            OriginalSupportRequestTicket ticket, long tick)
        {
            _originalMonitor = monitor;
            _threadId = threadId;
            _generation = ticket.Generation;
            _epoch = ticket.MapEpoch;
            _cancellation = ticket.CancellationSerial;
            _tick = tick;
            _map = ticket.MapSha256;
            _nonce = ticket.Nonce;
        }

        // Internal owner certificate is minted ONLY by the real exact managed
        // callback after independent registration proof. A monitor held by
        // an arbitrary caller is NOT a certified original simulation lane.
        // No registration API, Thread.Suspend, monitor spoof or installer here.
        private static bool TryCaptureFromExactInstalledPostTick(
            OriginalSupportRequestTicket ticket, long verifiedOriginalTick,
            out OriginalRequestSimulationOwner owner)
        {
            owner = null;
            if (!ExactPostTickOwnerRegistered || ticket == null ||
                verifiedOriginalTick < ticket.ObservedTick ||
                verifiedOriginalTick > ticket.ExpiresAfterTick)
                return false;
            try
            {
                Type engine = typeof(MainViewModel).Assembly.GetType("EngineInterface", false);
                if (engine == null ||
                    engine.Module.ModuleVersionId.ToString() !=
                    "29ade4ef-f587-422f-a1eb-f81dff066e0a")
                    return false;
                FieldInfo field = engine.GetField("threadLock",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null || field.FieldType != typeof(object))
                    return false;
                object monitor = field.GetValue(null);
                if (monitor == null || !Monitor.IsEntered(monitor) ||
                    !OriginalSupportLiveSoloAudit.TryVerifyUnderOriginalMonitor(
                        ticket, verifiedOriginalTick))
                    return false;
                owner = new OriginalRequestSimulationOwner(monitor,
                    Thread.CurrentThread.ManagedThreadId, ticket, verifiedOriginalTick);
                return true;
            }
            catch { return false; }
        }

#if ORIGINAL_REQUEST_PURE_FAKE_TESTS
        // Exclusively compiled into the tiny isolated synthetic C# harness.
        // This branch must NEVER be compiled into an integrated plugin.
        internal static OriginalRequestSimulationOwner SyntheticForFakeOnly(
            object ownedMonitor, OriginalSupportRequestTicket ticket, long tick)
        {
            if (ownedMonitor == null || !Monitor.IsEntered(ownedMonitor))
                throw new InvalidOperationException("test monitor not entered");
            return new OriginalRequestSimulationOwner(ownedMonitor,
                Thread.CurrentThread.ManagedThreadId, ticket, tick);
        }
#endif

        internal bool MatchesOriginalOwnerAndScope(OriginalSupportRequestTicket ticket)
        {
            return ticket != null && _originalMonitor != null &&
                Monitor.IsEntered(_originalMonitor) &&
                _threadId == Thread.CurrentThread.ManagedThreadId &&
                _nonce == ticket.Nonce && _generation == ticket.Generation &&
                _epoch == ticket.MapEpoch && _cancellation == ticket.CancellationSerial &&
                _tick >= ticket.ObservedTick && _tick <= ticket.ExpiresAfterTick &&
                String.Equals(_map, ticket.MapSha256, StringComparison.Ordinal);
        }

        internal long OriginalTick { get { return _tick; } }
    }

    // Exact typed native binding from this project's existing original image
    // attestation pattern. Not loaded or invoked by this delivery.
    // Original consumer is void (int32 target,int32 donor,int32 requester,int32 opcode).
    internal sealed class PinnedOriginalDefendConsumer : IOriginalSupportDefendConsumer
    {
        internal const int OriginalConsumerRva = 0xD7580;
        internal const string NativePeSha256 =
            "fbcb93195fc7efca9bdac5204852efdd76f9818f59a6711750d77c9cef2831e2";
        private const int OriginalConsumerLength = 587;
        private const string OriginalConsumerSha256 =
            "0624a805c7ff5378b9728657b3a9905bce8a1accfcea8230b43841717f14f589";
        internal static bool NativeBindingAuthorized { get { return DiplomacyFeature.PrivateMilitarySupportRequested; } }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ExactVoidFourInt32(int target, int donor,
            int requester, int opcode);
        private readonly ExactVoidFourInt32 _entry;
        private readonly IntPtr _image;

        private PinnedOriginalDefendConsumer(ExactVoidFourInt32 entry, IntPtr image)
        {
            _entry = entry;
            _image = image;
        }

        internal static bool TryBindLoadedOriginal(out PinnedOriginalDefendConsumer consumer,
            out string reason)
        {
            consumer = null;
            reason = "private_original_native_bridge_not_authorized";
            if (!NativeBindingAuthorized || !DisposableGameScope.IsEnabled(out reason))
                return false;
            try
            {
                if (!Environment.Is64BitProcess ||
                    Environment.OSVersion.Platform != PlatformID.Win32NT)
                    return false;
                string diagnostic;
                // Original main executable, exact ONE module, canonical loaded
                // native path and original SHA: not a caller-supplied filename.
                if (!SerpsRawraCompat.AttestedLoadedNativeEngine(out diagnostic))
                    return false;
                Type managed = typeof(MainViewModel).Assembly.GetType(
                    "EngineInterface", false);
                if (managed == null || managed.Module.ModuleVersionId.ToString() !=
                    "29ade4ef-f587-422f-a1eb-f81dff066e0a" ||
                    typeof(GamePlayerManagerAPI).Module.ModuleVersionId.ToString() !=
                    "aab7af1d-dabf-4f55-a7a6-dd67c8f06853")
                    return false;
                using (Process process = Process.GetCurrentProcess())
                {
                    ProcessModule found = null;
                    int count = 0;
                    foreach (ProcessModule module in process.Modules)
                    {
                        if (!String.Equals(module.ModuleName, "CrusaderDE.dll",
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                        found = module;
                        count++;
                    }
                    if (count != 1 || found == null || found.BaseAddress == IntPtr.Zero)
                        return false;
                    IntPtr entry = IntPtr.Add(found.BaseAddress, OriginalConsumerRva);
                    if (found.ModuleMemorySize < OriginalConsumerRva + OriginalConsumerLength ||
                        !OriginalNativeImageReadOnly.MatchesMappedBytes(found.BaseAddress,
                            OriginalConsumerRva, OriginalConsumerLength, OriginalConsumerSha256) ||
                        !OriginalNativeImageReadOnly.MatchesMappedBytes(found.BaseAddress,
                            0x3D020, 564, "b5d2adba59e12aecc399ddfcd5a11aa89d1016b146ab03c10756ea8e80466fe0") ||
                        !OriginalNativeImageReadOnly.MatchesMappedBytes(found.BaseAddress,
                            0x2C8040, 88, "7a50b536831ab59afa82ceceb289acd94063bd167fea6e46e6c33b0134b21bc7"))
                    {
                        reason = "original_ally_consumer_memory_mismatch";
                        return false;
                    }
                    consumer = new PinnedOriginalDefendConsumer(
                        (ExactVoidFourInt32)Marshal.GetDelegateForFunctionPointer(
                            entry, typeof(ExactVoidFourInt32)), found.BaseAddress);
                    reason = "verified_loaded_image_and_original_four_int32_void_entry";
                    return true; // Binding is NOT a dispatch or original ACK.
                }
            }
            catch (Exception e)
            {
                reason = "original_native_binding_refused:" + e.GetType().Name;
                return false;
            }
        }

        internal unsafe bool TryReadOriginalRescueThreshold(int profile, out int threshold)
        {
            threshold = 0;
            const int aicTableRva = 0x404C950;
            if (!DiplomacyFeature.IsNativeDiplomacySectionHeld ||
                profile < 0 || profile >= Enum.GetValues(typeof(Enums.AILords)).Length ||
                sizeof(InternalAIC) != 0x5E4)
                return false;
            IntPtr entry = IntPtr.Add(_image, aicTableRva + profile * sizeof(InternalAIC));
            if (!OriginalNativeImageReadOnly.Readable(entry, sizeof(InternalAIC)))
                return false;
            threshold = ((InternalAIC*)entry)->siege_troops_before_will_come_to_rescue;
            return threshold >= 0;
        }

        public void RequestDefend(int target, int donorAI, int defendedAI, int opcode)
        {
            if (!NativeBindingAuthorized || !DiplomacyFeature.IsNativeDiplomacySectionHeld ||
                _entry == null || target != 0 ||
                donorAI < 1 || donorAI > 8 || defendedAI < 1 || defendedAI > 8 ||
                donorAI == defendedAI || opcode != 11)
                throw new InvalidOperationException("original_native_request_not_authorized");
            _entry(target, donorAI, defendedAI, opcode);
            // VOID native consumer; it is not an ACK, acceptance or troop movement.
        }
    }

    internal sealed class OriginalSupportRequestBeginReceipt
    {
        internal readonly OriginalSupportRequestTicket Ticket;
        internal readonly long OriginalTick;
        internal readonly bool NativeCallStarted, NativeCallThrew;
        internal readonly OriginalSupportHookReceipt SynchronousEvaluatorPost;
        internal bool NativeAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }

        internal OriginalSupportRequestBeginReceipt(OriginalSupportRequestTicket ticket,
            long tick, bool threw, OriginalSupportHookReceipt boundedPost)
        {
            Ticket = ticket; OriginalTick = tick;
            NativeCallStarted = true; NativeCallThrew = threw;
            SynchronousEvaluatorPost = boundedPost;
        }
    }

    // No journal and no planner; uses the SAME ordered existing lifecycle.
    // Only the actual external owner can attempt one stamped request.
    internal sealed class OriginalAllySupportRequestBridge
    {
        internal static readonly bool LiveOriginalNativeDispatchEnabled = false;
        private readonly ModSupportIntentLifecycle _lifecycle;
        private OriginalSupportRequestTicket _staged;
        private OriginalAllyHumanPriorityFrame _humanFirstPriority;
        private OriginalSupportRequestBeginReceipt _begin;
        // 0 unset, 1 staged, 2 invocation entered, 3 ended/unknown.
        private int _attemptState;

        internal OriginalAllySupportRequestBridge(ModSupportIntentLifecycle lifecycle)
        {
            if (lifecycle == null)
                throw new ArgumentNullException("lifecycle");
            _lifecycle = lifecycle;
        }

        // Called ONLY on the managed MOD owner lane under its existing gate,
        // never from the original sim callback while it holds threadLock.
        // Detached review facts do not authorize a native call; independent
        // actual original monitor + installed callback proof is still needed.
        internal OriginalSupportRequestPhase TryStageExistingIntent(
            ulong nonce, long now, OriginalAllyRequestReviewFacts facts,
            out OriginalSupportRequestTicket ticket)
        {
            ticket = null;
            if (nonce == 0 || facts == null || Interlocked.CompareExchange(
                    ref _attemptState, 0, 0) != 0)
                return OriginalSupportRequestPhase.Waiting;
            ModSupportIntent[] ordered = _lifecycle.OrderedPending(now);
            if (ordered.Length == 0 || ordered[0] == null ||
                ordered[0].Nonce != nonce)
                return OriginalSupportRequestPhase.Waiting;
            ModSupportIntent candidate = ordered[0];
            if (candidate.Kind != AiMilitaryCooperationKind.DefendAlly ||
                candidate.ConsultedDonorAI == candidate.RequesterDefendedAI ||
                candidate.Nonce == 0 || candidate.ReliefEventId == 0)
                return OriginalSupportRequestPhase.Waiting;
            if (_lifecycle.RequestOriginalEvaluatorPreflight(nonce, now, facts) !=
                ModSupportPreflight.RequestIndependentNativePreflight)
                return OriginalSupportRequestPhase.Waiting;

            OriginalSupportRequestTicket proposed =
                new OriginalSupportRequestTicket(candidate);
            if (Interlocked.CompareExchange(ref _staged, proposed, null) != null)
                return OriginalSupportRequestPhase.Waiting;
            Interlocked.CompareExchange(ref _attemptState, 1, 0);
            ticket = proposed;
            return OriginalSupportRequestPhase.StagedReviewOnly;
        }

        // Stronger REAL MOD-owner route: under the existing MOD owner gate
        // plus the original nonblocking native monitor lease, derive a permit
        // only from its ACTUAL per-donor human pending/inflight registers and
        // typed donor native ally mode. No caller-provided "priority=true".
        // Existing lifecycle and source-only staging are reused, not cloned.
        internal OriginalSupportRequestPhase TryStageWithActualHumanPriority(
            DiplomacyFeature realOwner, ulong nonce, long originalTick,
            OriginalAllyRequestReviewFacts current, out OriginalSupportRequestTicket ticket)
        {
            ticket = null;
            if (realOwner == null)
                return OriginalSupportRequestPhase.Waiting;
            OriginalSupportRequestPhase phase =
                TryStageExistingIntent(nonce, originalTick, current, out ticket);
            if (phase != OriginalSupportRequestPhase.StagedReviewOnly)
                return phase;
            OriginalAllyHumanPriorityFrame human;
            if (!realOwner.TryIssueOriginalAllyPriorityFrame(
                    ticket, originalTick, out human))
            {
                TryAbandonOwnUnbegunReview(nonce);
                ticket = null;
                return OriginalSupportRequestPhase.Waiting;
            }
            Volatile.Write(ref _humanFirstPriority, human);
            return phase;
        }

        // Own MOD preparation only. A verified busy/unknown original state
        // must not cancel any ORIGINAL command or permanently disable a map.
        // An already-begun or outcome-unknown invocation is NEVER reset.
        internal bool TryAbandonOwnUnbegunReview(ulong nonce)
        {
            OriginalSupportRequestTicket current = Volatile.Read(ref _staged);
            if (current == null || nonce == 0 || current.Nonce != nonce ||
                Interlocked.CompareExchange(ref _attemptState, 0, 1) != 1)
                return false;
            Interlocked.CompareExchange(ref _staged, null, current);
            Interlocked.Exchange(ref _humanFirstPriority, null);
            return true;
        }

        // The only production entry for dispatch. Exact verified post-tick
        // installation, native-ABI acceptance and a same-tick ORIGINAL Chore113
        // priority barrier are missing, so the production switch stays FALSE.
        internal OriginalSupportRequestPhase TryBeginFromOriginalSimulation(
            OriginalRequestSimulationOwner exactOwner,
            IOriginalSupportDefendConsumer typedConsumer,
            out OriginalSupportRequestBeginReceipt receipt)
        {
            receipt = null;
            if (!LiveOriginalNativeDispatchEnabled ||
                !PinnedOriginalDefendConsumer.NativeBindingAuthorized)
                return OriginalSupportRequestPhase.UnarmedOriginalBoundary;
            // An externally supplied flag is NOT an owner or priority ticket.
            OriginalSupportRequestTicket ticket = Volatile.Read(ref _staged);
            OriginalAllyHumanPriorityFrame frame =
                Volatile.Read(ref _humanFirstPriority);
            if (ticket == null || frame == null || exactOwner == null ||
                !exactOwner.MatchesOriginalOwnerAndScope(ticket) ||
                !frame.Owner.OriginalHumanPriorityStillCurrentOnSimulationLane(
                    frame, ticket, exactOwner.OriginalTick) ||
                !OriginalSupportLiveSoloAudit.TryVerifyUnderOriginalMonitor(
                    ticket, exactOwner.OriginalTick))
                return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
            // SAME capture gate as the real human HUD prefix, zero timeout.
            // A human registered BEFORE the native claim wins; clicks AFTER
            // this linearization remain queued in the original MOD pipeline.
            OriginalSupportNativeClaim claim;
            if (!frame.Owner.TryAcquireOriginalSupportNativeClaim(
                    frame, ticket, exactOwner.OriginalTick, out claim))
                return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
            try
            {
                if (!frame.Owner.OriginalSupportNativeClaimStillCurrent(
                        claim, ticket, exactOwner.OriginalTick))
                    return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
                // Do NOT hold _humanCaptureGate or enter _gate over the
                // synchronous original evaluator/native void call.
                return InvokeOnceAfterCertifiedOwner(
                    exactOwner, typedConsumer, out receipt);
            }
            finally
            {
                frame.Owner.ReleaseOriginalSupportNativeClaim(claim);
            }
        }

#if ORIGINAL_REQUEST_PURE_FAKE_TESTS
        // Fake-transport-only, isolated C# tests. The real DLL is never loaded,
        // and no production build may define ORIGINAL_REQUEST_PURE_FAKE_TESTS.
        internal OriginalSupportRequestPhase InvokeFakeForPureTests(
            OriginalRequestSimulationOwner fakeOwner,
            IOriginalSupportDefendConsumer fakeOnly,
            out OriginalSupportRequestBeginReceipt receipt)
        {
            if (fakeOnly is PinnedOriginalDefendConsumer)
            {
                receipt = null;
                return OriginalSupportRequestPhase.UnarmedOriginalBoundary;
            }
            return InvokeOnceAfterCertifiedOwner(fakeOwner, fakeOnly, out receipt);
        }
#endif

        private OriginalSupportRequestPhase InvokeOnceAfterCertifiedOwner(
            OriginalRequestSimulationOwner owner, IOriginalSupportDefendConsumer consumer,
            out OriginalSupportRequestBeginReceipt receipt)
        {
            receipt = null;
            OriginalSupportRequestTicket ticket = Volatile.Read(ref _staged);
            if (ticket == null || Interlocked.CompareExchange(
                    ref _attemptState, 0, 0) != 1)
                return OriginalSupportRequestPhase.DuplicateOrCompleted;
            if (owner == null || !owner.MatchesOriginalOwnerAndScope(ticket) ||
                consumer == null)
                return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
            if (Interlocked.CompareExchange(ref _attemptState, 2, 1) != 1)
                return OriginalSupportRequestPhase.DuplicateOrCompleted;
            // Exactly one ThreadStatic call-span, created outside callback.
            // Every callback observation is primitive, nonblocking and bounded.
            OriginalSupportHookSpan span =
                OriginalAllySynchronousEvaluatorCapture.Begin(ticket);
            if (span == null)
            {
                Interlocked.CompareExchange(ref _attemptState, 1, 2);
                return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
            }
            bool threw = true;
            try
            {
                OriginalSupportDefendArguments call = ticket.Arguments;
                // The ORIGINAL native evaluator retains its own busy, strength,
                // refusal and troop selection. No Post value forces acceptance.
                consumer.RequestDefend(call.AttackTarget, call.ConsultedDonorAI,
                    call.DefendedRequesterAI, call.Opcode);
                threw = false;
            }
            catch
            {
                // Native void may already have applied effects: never retry.
            }
            finally
            {
                OriginalSupportHookReceipt post =
                    OriginalAllySynchronousEvaluatorCapture.End(
                        span, owner.OriginalTick);
                var begun = new OriginalSupportRequestBeginReceipt(
                    ticket, owner.OriginalTick, threw, post);
                Volatile.Write(ref _begin, begun);
                Interlocked.Exchange(ref _attemptState, 3);
                receipt = begun;
            }
            return threw
                ? OriginalSupportRequestPhase.InvocationOutcomeUnknownNoRetry
                : OriginalSupportRequestPhase.DispatchBeganUnconfirmed;
        }

        // MOD-owner journal reconciliation starts only on a LATER original
        // tick. Caller must own its existing MOD gate, not original threadLock.
        // Neither a missing post nor a mutable Post=true is a native ACK.
        // Cancelled/changed/expired scopes quarantine a begun request.
        internal bool TryReconcileBeginOnModOwnerLane(long nowTick)
        {
            if (DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return false; // no inverted monitor/_gate acquisition
            OriginalSupportRequestBeginReceipt begun = Volatile.Read(ref _begin);
            if (begun == null || !begun.NativeCallStarted ||
                nowTick <= begun.OriginalTick)
                return false;
            OriginalAllyHumanPriorityFrame priority =
                Volatile.Read(ref _humanFirstPriority);
            if (priority != null &&
                !priority.Owner.IsOriginalSupportModOwnerGateHeld())
                return false;
            OriginalSupportRequestTicket t = begun.Ticket;
            return _lifecycle.NoteExternalOriginalSupportBeginFollowingTick(
                t.Nonce, begun.OriginalTick, nowTick, t.Generation,
                t.MapEpoch, t.CancellationSerial, t.MapSha256);
        }

        // A later original native state tuple alone carries NO MOD nonce or
        // original decision correlation ID. Classify it but never emit ACK,
        // movement success or favor credit, even when mode=2/requester matches.
        internal OriginalSupportRequestPhase ObserveLaterOriginalTuple(
            long generation, long epoch, long tick, string mapSha,
            int donorMode, int nativeRequester)
        {
            OriginalSupportRequestBeginReceipt begun = Volatile.Read(ref _begin);
            if (begun == null || tick <= begun.OriginalTick)
                return OriginalSupportRequestPhase.Waiting;
            OriginalSupportRequestTicket t = begun.Ticket;
            if (generation != t.Generation || epoch != t.MapEpoch ||
                !String.Equals(mapSha, t.MapSha256, StringComparison.Ordinal) ||
                tick > t.ExpiresAfterTick)
                return OriginalSupportRequestPhase.RetryableCurrentOwnerMismatch;
            if (donorMode == 2 && nativeRequester == t.DefendedAI)
                return OriginalSupportRequestPhase.OriginalTupleUnattributed;
            return OriginalSupportRequestPhase.NativeCausalResponseNotProven;
        }

        // Immutable actual invocation-begin observation survives a later
        // lifecycle Tick/Expire or world rollover; it never becomes ACK.
        internal OriginalSupportRequestBeginReceipt LastOriginalBeginObservation
        {
            get { return Volatile.Read(ref _begin); }
        }

        internal bool HasNativeAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }
        internal bool OriginalCPURequested { get { return false; } }
    }
}
#endif
