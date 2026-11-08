// world observation ExtendedState: ORIGINAL GetElapsedMapTicks clock, pinned IL,
// bounded same-session monotonic local witness and causal read-only frames.
// No callbacks, Unity frame count, ReadSimulationTick or new Chore owner.
// Only the selected  post-native managed original monitor may call this.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SHCDESE.API;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalMapClockCursor
    {
        internal const long RejectNearSignedIntOverflow = (long)int.MaxValue - 4096;
        internal readonly Bytes OriginalSignedSessionId;
        internal readonly Bytes SignedScope;
        internal readonly long OriginalMapGeneration, OriginalNetworkGeneration;
        internal readonly ulong OriginalHostSteamId, OriginalLocalSteamId;
        private long _lastNativeTick;
        private bool _invalid;
        internal OriginalMapClockCursor(TrustedContext context, Bytes pinnedSession, long originalMapGeneration)
        {
            if (context == null || context.Generation < 1 || context.HostSteamId <= 1000 || context.LocalSteamId <= 1000 || context.Scope == null || context.Scope.Length != 128 || pinnedSession == null || pinnedSession.Length != 32 || originalMapGeneration < 1)
                throw new InvalidDataException("Unpinned original MP clock");
            OriginalSignedSessionId = new Bytes(pinnedSession.Copy());
            SignedScope = new Bytes(context.Scope.Copy());
            OriginalMapGeneration = originalMapGeneration;
            OriginalNetworkGeneration = context.Generation;
            OriginalHostSteamId = context.HostSteamId;
            OriginalLocalSteamId = context.LocalSteamId;
        }

        internal bool TryObserve(int originalElapsedTicks, TrustedContext currentContext, Bytes currentSession, long currentMapGeneration, out long observed, out string reason)
        {
            observed = 0;
            reason = "original_map_tick_unverified";
            if (_invalid)
            {
                reason = "original_map_tick_cursor_revoked";
                return false;
            }

            if (currentContext == null || currentSession == null || currentMapGeneration != OriginalMapGeneration || currentContext.Generation != OriginalNetworkGeneration || currentContext.HostSteamId != OriginalHostSteamId || currentContext.LocalSteamId != OriginalLocalSteamId || !currentContext.Scope.Same(SignedScope) || !currentSession.Same(OriginalSignedSessionId))
            {
                _invalid = true;
                reason = "original_map_or_signed_session_changed";
                return false;
            }

            // The native UInt64* can be UNBOUND at the map-start. In that
            // case GetElapsedMapTicks returns 0: WAIT for real binding,
            // never revoke the map or invent a fake tick 1.
            if (originalElapsedTicks == 0)
            {
                reason = "original_map_clock_unbound_waiting";
                return false;
            }

            // Negative signed UInt64 truncation/near-overflow, or a REAL
            // previously positive clock rewind, invalidate this session.
            if (originalElapsedTicks < 0 || (long)originalElapsedTicks >= RejectNearSignedIntOverflow || (long)originalElapsedTicks < _lastNativeTick)
            {
                _invalid = true;
                reason = "original_map_tick_overflow_or_rewind";
                return false;
            }

            _lastNativeTick = originalElapsedTicks;
            observed = originalElapsedTicks;
            reason = "official_original_elapsed_map_tick_observed";
            return true;
        }

        internal void Revoke()
        {
            _invalid = true;
        }
    }

    // The Engine's ONE existing ChoreSession owns the consumed INPUT
    // cursor. The Controller's ONE Schema19 owner owns the published BASE
    // cursor. During activation, these MUST be allowed to DIFFER:
    // consumed1 (Activation), published0, savedEpoch0, collectorEpoch1.
    // This type cannot authenticate its inputs and is created ONLY from
    // their genuinely verified managed callbacks under the ORIGINAL lock.
    internal sealed class PublishedSignedChoreFrontier
    {
        internal readonly Bytes OriginalSessionId, Original128Scope;
        internal readonly ulong SavedInputEpoch, EffectiveSignedInputEpoch;
        internal readonly ulong VerifiedGlobalSignedSequence;
        internal readonly ulong CanonicallyPublishedGlobalSignedSequence;
        internal readonly long OriginalMapGeneration, ActualNativeMapTick;
        internal readonly int ActualNativeDay;
        private readonly byte[] _ownerReadbackSha256;
        internal PublishedSignedChoreFrontier(Bytes verifiedOriginalSessionId, Bytes original128Scope, ulong actuallySavedCanonicalInputEpoch, ulong effectiveAlreadyVerifiedCollectorInputEpoch, ulong actuallyConsumedInputChoreHighwater, ulong confirmedPublishedBaseSignedHighwater, long originalMapGeneration, long observedActualNativeMapTick, int observedNativeDay, byte[] exactBeforeCanonicalOwnerReadback)
        {
            if (verifiedOriginalSessionId == null || verifiedOriginalSessionId.Length != 32 || original128Scope == null || original128Scope.Length != 128 || actuallySavedCanonicalInputEpoch >= ulong.MaxValue - 2 || effectiveAlreadyVerifiedCollectorInputEpoch < 1 || effectiveAlreadyVerifiedCollectorInputEpoch >= ulong.MaxValue - 1 || // The collector's effective signed activation may be one
 // ahead of current saved InputEpoch, NEVER a guessed epoch.
            (effectiveAlreadyVerifiedCollectorInputEpoch != actuallySavedCanonicalInputEpoch && effectiveAlreadyVerifiedCollectorInputEpoch != actuallySavedCanonicalInputEpoch + 1) || actuallyConsumedInputChoreHighwater < 1 || actuallyConsumedInputChoreHighwater >= ulong.MaxValue - 2 || confirmedPublishedBaseSignedHighwater > actuallyConsumedInputChoreHighwater || originalMapGeneration < 1 || observedActualNativeMapTick < 1 || observedNativeDay < 1 || exactBeforeCanonicalOwnerReadback == null || exactBeforeCanonicalOwnerReadback.Length < 16 || exactBeforeCanonicalOwnerReadback.Length > 262144)
                throw new InvalidDataException("Original consumed vs saved base signed input frontier unproven");
            OriginalSessionId = new Bytes(verifiedOriginalSessionId.Copy());
            Original128Scope = new Bytes(original128Scope.Copy());
            SavedInputEpoch = actuallySavedCanonicalInputEpoch;
            EffectiveSignedInputEpoch = effectiveAlreadyVerifiedCollectorInputEpoch;
            VerifiedGlobalSignedSequence = actuallyConsumedInputChoreHighwater;
            CanonicallyPublishedGlobalSignedSequence = confirmedPublishedBaseSignedHighwater;
            OriginalMapGeneration = originalMapGeneration;
            ActualNativeMapTick = observedActualNativeMapTick;
            ActualNativeDay = observedNativeDay;
            // SHA only the REAL original BEFORE CanonicalSubrecord bytes;
            // never synthesize "the new Epoch" in the saved owner.
            using (var hash = System.Security.Cryptography.SHA256.Create())
                _ownerReadbackSha256 = hash.ComputeHash((byte[])exactBeforeCanonicalOwnerReadback.Clone());
        }

        internal byte[] OwnerReadbackSha256()
        {
            return (byte[])_ownerReadbackSha256.Clone();
        }
    }

    internal sealed class CapturedConsentWorldFrame
    {
        internal readonly OriginalMultiplayerConsentFrame OriginalObservation;
        internal readonly long ActualOriginalMapTick;
        internal readonly int ActualNativeDay;
        internal readonly ulong SavedCanonicalInputEpoch;
        internal readonly ulong EffectiveSignedInputEpoch;
        internal readonly ulong ConsumedSignedInputFrontier;
        internal readonly ulong PublishedBaseSignedFrontier;
        internal readonly ulong CaptureOrdinal;
        private readonly byte[] _digest, _canonicalBeforeReadbackSha256;
        internal CapturedConsentWorldFrame(OriginalMultiplayerConsentFrame observedWorld, byte[] domainSeparatedConsentDigest, PublishedSignedChoreFrontier actualOriginalFrontier, ulong ordinal)
        {
            if (observedWorld == null || actualOriginalFrontier == null || domainSeparatedConsentDigest == null || domainSeparatedConsentDigest.Length != 32 || observedWorld.NativeSharedTick < 1 || observedWorld.NativePhysicalSettlementAuthorized || actualOriginalFrontier.ActualNativeMapTick != observedWorld.NativeSharedTick || actualOriginalFrontier.ActualNativeDay != observedWorld.NativeGameDay || actualOriginalFrontier.OriginalMapGeneration != observedWorld.LocalOriginalMapGeneration || ordinal < 1)
                throw new InvalidDataException("No actual original signed input capture");
            OriginalObservation = observedWorld;
            ActualOriginalMapTick = observedWorld.NativeSharedTick;
            ActualNativeDay = observedWorld.NativeGameDay;
            SavedCanonicalInputEpoch = actualOriginalFrontier.SavedInputEpoch;
            EffectiveSignedInputEpoch = actualOriginalFrontier.EffectiveSignedInputEpoch;
            ConsumedSignedInputFrontier = actualOriginalFrontier.VerifiedGlobalSignedSequence;
            PublishedBaseSignedFrontier = actualOriginalFrontier.CanonicallyPublishedGlobalSignedSequence;
            CaptureOrdinal = ordinal;
            _digest = (byte[])domainSeparatedConsentDigest.Clone();
            _canonicalBeforeReadbackSha256 = actualOriginalFrontier.OwnerReadbackSha256();
        }

        internal byte[] ConsentOnlyDigest()
        {
            return (byte[])_digest.Clone();
        }

        internal byte[] OriginalOwnerReadbackDigest()
        {
            return (byte[])_canonicalBeforeReadbackSha256.Clone();
        }
    }

    // Exactly ONE owner for observed original-world history, not a second
    // controller cache. A native tick may have multiple real observed states
    // because original signed Chores can mutate business consent without a
    // native map-clock advance.
    internal sealed class OriginalConsentFrameHistory
    {
        internal const int MaxRetainedExactFrames = 160;
        internal const long MaxSignedDeliveryLagOriginalTicks = 600;
        private readonly Bytes _session, _scope;
        private readonly ulong _hostSteam, _localSteam, _effectiveEpoch;
        private readonly long _mapGeneration, _networkGeneration;
        private readonly SortedDictionary<long, List<CapturedConsentWorldFrame>> _observed = new SortedDictionary<long, List<CapturedConsentWorldFrame>>();
        private int _actualObservations;
        private long _lastCapturedOriginalTick;
        private int _lastCapturedOriginalDay;
        private ulong _lastConsumedInputSequence;
        private ulong _captureOrdinal;
        private CapturedConsentWorldFrame _pendingPinnedOriginalWorld;
        private ulong _pendingPinnedWorldSequence;
        private bool _revoked;
        internal OriginalConsentFrameHistory(TrustedContext originalNetworkContext, Bytes signedSession, long localMapGeneration, ulong actualCollectorEffectiveEpoch)
        {
            if (originalNetworkContext == null || originalNetworkContext.Generation < 1 || originalNetworkContext.HostSteamId <= 1000 || originalNetworkContext.LocalSteamId <= 1000 || originalNetworkContext.Scope == null || originalNetworkContext.Scope.Length != 128 || signedSession == null || signedSession.Length != 32 || localMapGeneration < 1 || actualCollectorEffectiveEpoch < 1 || actualCollectorEffectiveEpoch >= ulong.MaxValue - 1)
                throw new InvalidDataException("Missing original frame history scope");
            _session = new Bytes(signedSession.Copy());
            _scope = new Bytes(originalNetworkContext.Scope.Copy());
            _hostSteam = originalNetworkContext.HostSteamId;
            _localSteam = originalNetworkContext.LocalSteamId;
            _effectiveEpoch = actualCollectorEffectiveEpoch;
            _mapGeneration = localMapGeneration;
            _networkGeneration = originalNetworkContext.Generation;
        }

        private bool SameSession(TrustedContext original, Bytes pinned, long localGeneration)
        {
            return !_revoked && original != null && pinned != null && localGeneration == _mapGeneration && original.Generation == _networkGeneration && original.HostSteamId == _hostSteam && original.LocalSteamId == _localSteam && original.Scope.Same(_scope) && pinned.Same(_session);
        }

        private bool SameInputFrontier(PublishedSignedChoreFrontier f, OriginalMultiplayerConsentFrame frame)
        {
            return f != null && frame != null && f.EffectiveSignedInputEpoch == _effectiveEpoch && f.SavedInputEpoch <= _effectiveEpoch && f.VerifiedGlobalSignedSequence >= 1 && f.CanonicallyPublishedGlobalSignedSequence <= f.VerifiedGlobalSignedSequence && f.OriginalSessionId.Same(_session) && f.Original128Scope.Same(_scope) && f.OriginalMapGeneration == _mapGeneration && f.ActualNativeMapTick == frame.NativeSharedTick && f.ActualNativeDay == frame.NativeGameDay && f.VerifiedGlobalSignedSequence >= _lastConsumedInputSequence;
        }

        // The unbound original map clock 0 does NOT invoke this method.
        // Inputs are the truly consumed original INPUT cursor and the
        // separately saved BEFORE canonical owner's published cursor.
        internal bool TryRecordActualOriginalFrame(OriginalMultiplayerConsentFrame observedWorld, ulong effectiveCollectorInputEpoch, long independentlyReadOriginalMapTick, TrustedContext originalNetwork, Bytes signedSession, PublishedSignedChoreFrontier actualOriginalSignedFrontier, bool originalEngineMonitorHeld, out string reason)
        {
            reason = "original_capture_unavailable";
            if (!originalEngineMonitorHeld || !DiplomacyFeature.IsNativeDiplomacySectionHeld || !SameSession(originalNetwork, signedSession, observedWorld == null ? 0 : observedWorld.LocalOriginalMapGeneration) || effectiveCollectorInputEpoch != _effectiveEpoch || observedWorld == null || observedWorld.NativeSharedTick < 1 || independentlyReadOriginalMapTick != observedWorld.NativeSharedTick || observedWorld.OriginalHostSteamId != _hostSteam || observedWorld.OriginalLocalSteamId != _localSteam || !new Bytes(observedWorld.CopyTrustedScope()).Same(_scope))
            {
                _revoked = true;
                reason = "original_capture_scope_or_identity_changed";
                return false;
            }

            if (!SameInputFrontier(actualOriginalSignedFrontier, observedWorld))
            {
                // Missing a real consumed Input or a saved BEFORE readback
                // is WAITING, not a fake early publication and not a global
                // original-session kill.
                reason = "waiting_for_real_collector_input_and_saved_before_owner";
                return false;
            }

            if (independentlyReadOriginalMapTick < _lastCapturedOriginalTick || (_lastCapturedOriginalDay != 0 && observedWorld.NativeGameDay < _lastCapturedOriginalDay))
            {
                _revoked = true;
                reason = "original_clock_or_game_day_rolled_back";
                return false;
            }

            byte[] digest;
            try
            {
                digest = ConsentWorldDigest.Compute(observedWorld);
            }
            catch (Exception e)
            {
                _revoked = true;
                reason = "original_world_digest_invalid:" + e.GetType().Name;
                return false;
            }

            List<CapturedConsentWorldFrame> sameTick;
            if (!_observed.TryGetValue(independentlyReadOriginalMapTick, out sameTick))
            {
                sameTick = new List<CapturedConsentWorldFrame>();
                _observed.Add(independentlyReadOriginalMapTick, sameTick);
            }

            foreach (var prior in sameTick)
            {
                if (prior.ActualNativeDay == observedWorld.NativeGameDay && prior.EffectiveSignedInputEpoch == _effectiveEpoch && prior.ConsumedSignedInputFrontier == actualOriginalSignedFrontier.VerifiedGlobalSignedSequence && prior.PublishedBaseSignedFrontier == actualOriginalSignedFrontier.CanonicallyPublishedGlobalSignedSequence && MpSnapshotDigest.Equal32(prior.ConsentOnlyDigest(), digest) && MpSnapshotDigest.Equal32(prior.OriginalOwnerReadbackDigest(), actualOriginalSignedFrontier.OwnerReadbackSha256()))
                {
                    reason = "duplicate_identical_original_same_tick_input_frontier";
                    return true;
                }
            }

            if (_captureOrdinal >= ulong.MaxValue - 2)
            {
                _revoked = true;
                reason = "original_observation_ordinal_overflow";
                return false;
            }

            // Bounded history: evict only UNPINNED old observations.
            // If no safe eviction exists, do not capture a new frame; do
            // NOT silently discard a signed frame before its barrier/abort.
            if (_actualObservations >= MaxRetainedExactFrames && !TryEvictOneOldestUnpinned())
            {
                reason = "original_world_history_full_signed_frame_is_pinned";
                return false;
            }

            var captured = new CapturedConsentWorldFrame(observedWorld, digest, actualOriginalSignedFrontier, ++_captureOrdinal);
            sameTick.Add(captured);
            _actualObservations++;
            _lastCapturedOriginalTick = independentlyReadOriginalMapTick;
            _lastCapturedOriginalDay = observedWorld.NativeGameDay;
            _lastConsumedInputSequence = actualOriginalSignedFrontier.VerifiedGlobalSignedSequence;
            // Expiry must retire ONLY elapsed entries. If the oldest tick
            // is SIGNED/PINNED, the previous unrestricted oldest-unpinned
            // eviction would delete NEWER in-window captures instead,
            // starving the next original WorldFrame. Keep its pin, retire
            // other expired entries, never sacrifice a fresh one for age.
            while (TryEvictOneExpiredUnpinned(independentlyReadOriginalMapTick))
            {
            }

            reason = "real_original_frame_with_separate_consumed_published_frontiers";
            return true;
        }

        private bool TryEvictOneOldestUnpinned()
        {
            foreach (var group in _observed.ToArray())
            {
                for (int i = 0; i < group.Value.Count; i++)
                {
                    if (Object.ReferenceEquals(group.Value[i], _pendingPinnedOriginalWorld))
                        continue;
                    group.Value.RemoveAt(i);
                    if (group.Value.Count == 0)
                        _observed.Remove(group.Key);
                    _actualObservations--;
                    return true;
                }
            }

            return false;
        }

        // Unlike the TOTAL entry-count cap, an age-based eviction is
        // never allowed to remove a recent frame merely because an older
        // sign-ordered witness remains pinned until original Barrier/Abort.
        // SortedDictionary keys are actual monotonically observed ticks.
        private bool TryEvictOneExpiredUnpinned(long latestNativeTick)
        {
            foreach (var group in _observed.ToArray())
            {
                if (latestNativeTick - group.Key <= MaxSignedDeliveryLagOriginalTicks)
                    break;
                for (int i = 0; i < group.Value.Count; i++)
                {
                    if (Object.ReferenceEquals(group.Value[i], _pendingPinnedOriginalWorld))
                        continue;
                    group.Value.RemoveAt(i);
                    if (group.Value.Count == 0)
                        _observed.Remove(group.Key);
                    _actualObservations--;
                    return true;
                }
            }

            return false;
        }

        // Called by HOST only after the chosen ONE HostBootstrap has
        // retained EXACT signed WorldFrame body bytes in its strict pending
        // Chore queue. The identical real source frame must already exist.
        // Peers can pin later upon real post-Chore RSA consumption.
        internal bool TryRetainHostQueuedOriginalWorldFrame(OrderedHostWorldWitness genuinelyQueuedHostWorldFrame, ulong originalConsumedInputHighwater, TrustedContext context, Bytes actualSession, long currentOriginalMapGeneration, out string reason)
        {
            reason = "host_world_frame_not_originally_queued";
            if (!SameSession(context, actualSession, currentOriginalMapGeneration) || genuinelyQueuedHostWorldFrame == null || genuinelyQueuedHostWorldFrame.OriginalHostSteamId != _hostSteam || !genuinelyQueuedHostWorldFrame.OriginalSessionId.Same(_session) || genuinelyQueuedHostWorldFrame.CanonicalInputEpoch != _effectiveEpoch || genuinelyQueuedHostWorldFrame.SignedHostChoreSequence != originalConsumedInputHighwater + 1 || originalConsumedInputHighwater < 1 || _pendingPinnedOriginalWorld != null)
                return false;
            CapturedConsentWorldFrame found;
            if (!FindExactObservedWorld(genuinelyQueuedHostWorldFrame, originalConsumedInputHighwater, out found))
                return false;
            _pendingPinnedOriginalWorld = found;
            _pendingPinnedWorldSequence = genuinelyQueuedHostWorldFrame.SignedHostChoreSequence;
            reason = "only_original_world_frame_pinned_until_ordered_barrier";
            return true;
        }

        private bool FindExactObservedWorld(OrderedHostWorldWitness signedWorld, ulong includedConsumedInputFrontier, out CapturedConsentWorldFrame matched)
        {
            matched = null;
            List<CapturedConsentWorldFrame> candidates;
            if (!_observed.TryGetValue(signedWorld.OriginalSharedTick, out candidates))
                return false;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var candidate = candidates[i];
                if (candidate.ConsumedSignedInputFrontier == includedConsumedInputFrontier && candidate.EffectiveSignedInputEpoch == _effectiveEpoch && MpSnapshotDigest.Equal32(candidate.ConsentOnlyDigest(), signedWorld.OriginalWorldSha256))
                {
                    // WorldFrame8 signs tick/frontier/world SHA but it does
                    // NOT carry a BEFORE canonical owner-readback SHA.
                    // Multiple actual captures with equal signed fields
                    // but DIFFERENT saved BEFORE-owner projections cannot
                    // be safely distinguished. Do not pick "latest" and
                    // bind it to the wrong canonical owner/SaveData scope.
                    if (matched != null && (candidate.SavedCanonicalInputEpoch != matched.SavedCanonicalInputEpoch || candidate.PublishedBaseSignedFrontier != matched.PublishedBaseSignedFrontier || !MpSnapshotDigest.Equal32(candidate.OriginalOwnerReadbackDigest(), matched.OriginalOwnerReadbackDigest())))
                    {
                        matched = null;
                        return false; // ambiguous => no native/consent publish
                    }

                    matched = candidate;
                }
            }

            return matched != null;
        }

        // Host pre-sign frame S: consumed INPUT frontier exactly S-1.
        // Published owner BASE may still be zero while initial Activation1
        // is staged. Never claim the controller has already published it.
        internal bool TryGetExactActuallyCapturedFrame(long currentOriginalTick, TrustedContext originalNetwork, Bytes originalSession, long currentLocalMapGeneration, ulong effectiveCollectorEpoch, ulong nextWorldFrameGlobalHostSequence, PublishedSignedChoreFrontier currentActualFrontier, out CapturedConsentWorldFrame frame)
        {
            frame = null;
            if (!SameSession(originalNetwork, originalSession, currentLocalMapGeneration) || effectiveCollectorEpoch != _effectiveEpoch || currentOriginalTick < 1 || currentOriginalTick != _lastCapturedOriginalTick || nextWorldFrameGlobalHostSequence < 2 || nextWorldFrameGlobalHostSequence >= ulong.MaxValue - 1 || currentActualFrontier == null || currentActualFrontier.EffectiveSignedInputEpoch != _effectiveEpoch || currentActualFrontier.VerifiedGlobalSignedSequence != nextWorldFrameGlobalHostSequence - 1 || currentActualFrontier.CanonicallyPublishedGlobalSignedSequence > currentActualFrontier.VerifiedGlobalSignedSequence || !currentActualFrontier.OriginalSessionId.Same(_session) || _pendingPinnedOriginalWorld != null)
                return false;
            List<CapturedConsentWorldFrame> observed;
            if (!_observed.TryGetValue(currentOriginalTick, out observed))
                return false;
            for (int i = observed.Count - 1; i >= 0; i--)
            {
                var c = observed[i];
                if (c.ConsumedSignedInputFrontier == nextWorldFrameGlobalHostSequence - 1 && c.PublishedBaseSignedFrontier == currentActualFrontier.CanonicallyPublishedGlobalSignedSequence && c.ActualNativeDay == currentActualFrontier.ActualNativeDay && MpSnapshotDigest.Equal32(c.OriginalOwnerReadbackDigest(), currentActualFrontier.OwnerReadbackSha256()))
                {
                    frame = c;
                    return true;
                }
            }

            return false;
        }

        // Strict post-RSA verified original WorldFrame S at native tick U.
        // Match only actually observed state T, consumed input frontier S-1
        // and the very same selected domain-separated  digest. Same
        // tick different states are DISTINCT observations, not an error.
        internal bool TryMatchDelayedVerifiedWorldFrame(OrderedHostWorldWitness verifiedOneStreamWorldFrame, ulong actuallyConsumedOriginalChoreSequence, long currentActualOriginalMapTick, int currentNativeDay, TrustedContext currentOriginalNetwork, Bytes currentPinnedOriginalSession, long currentLocalMapGeneration, bool originalEngineMonitorHeld, out CapturedConsentWorldFrame captured, out string reason)
        {
            captured = null;
            reason = "signed_world_frame_unavailable";
            if (!originalEngineMonitorHeld || !DiplomacyFeature.IsNativeDiplomacySectionHeld || !SameSession(currentOriginalNetwork, currentPinnedOriginalSession, currentLocalMapGeneration) || verifiedOneStreamWorldFrame == null || verifiedOneStreamWorldFrame.SignedHostChoreSequence != actuallyConsumedOriginalChoreSequence || actuallyConsumedOriginalChoreSequence < 2 || verifiedOneStreamWorldFrame.OriginalHostSteamId != _hostSteam || !verifiedOneStreamWorldFrame.OriginalSessionId.Same(_session) || verifiedOneStreamWorldFrame.CanonicalInputEpoch != _effectiveEpoch || verifiedOneStreamWorldFrame.OriginalSharedTick < 1 || currentActualOriginalMapTick < _lastCapturedOriginalTick)
            {
                _revoked = true;
                reason = "original_world_frame_scope_or_clock_changed";
                return false;
            }

            if (currentActualOriginalMapTick < verifiedOneStreamWorldFrame.OriginalSharedTick)
            {
                reason = "original_world_frame_not_yet_effective";
                return false;
            }

            if (currentActualOriginalMapTick - verifiedOneStreamWorldFrame.OriginalSharedTick > MaxSignedDeliveryLagOriginalTicks)
            {
                reason = "signed_world_frame_expired_new_progress_allowed";
                return false;
            }

            if (_pendingPinnedOriginalWorld != null && _pendingPinnedWorldSequence != actuallyConsumedOriginalChoreSequence)
            {
                reason = "different_signed_world_frame_still_pending_barrier";
                return false;
            }

            CapturedConsentWorldFrame found;
            if (!FindExactObservedWorld(verifiedOneStreamWorldFrame, actuallyConsumedOriginalChoreSequence - 1, out found))
            {
                reason = "no_actual_original_capture_at_signed_input_frontier";
                return false;
            }

            // Consent business time comes from this actual signed observation.
            // A later local delivery day must not select a different candidate.
            // The next shared signed frame advances/expiries on every peer.
            if (currentNativeDay < found.ActualNativeDay)
            {
                reason = "original_native_day_precedes_captured_frame";
                return false;
            }

            if (_pendingPinnedOriginalWorld == null)
            {
                _pendingPinnedOriginalWorld = found;
                _pendingPinnedWorldSequence = actuallyConsumedOriginalChoreSequence;
            }
            else if (!Object.ReferenceEquals(found, _pendingPinnedOriginalWorld))
            {
                reason = "signed_world_frame_differs_from_pinned_original";
                return false;
            }

            captured = found;
            reason = "selected_original_captured_consent_at_consumed_input_frontier";
            return true;
        }

        // Only after an actual RSA verified BARRIER S+1 was consumed and
        // the coordinator/controller bound this very pending original
        // WorldFrame S to that same ordered publication or abort.
        internal bool TryReleasePinnedAfterVerifiedOrderedBarrier(ulong exactWorldFrameSequence, ulong actuallyConsumedBarrierSequence, out string reason)
        {
            reason = "no_original_barrier_proof_for_pinned_frame";
            if (_revoked || _pendingPinnedOriginalWorld == null || _pendingPinnedWorldSequence != exactWorldFrameSequence || exactWorldFrameSequence >= ulong.MaxValue - 1 || actuallyConsumedBarrierSequence != exactWorldFrameSequence + 1)
                return false;
            _pendingPinnedOriginalWorld = null;
            _pendingPinnedWorldSequence = 0;
            reason = "one_original_frame_released_after_verified_barrier";
            return true;
        }

        internal int ActualFramesRecorded
        {
            get
            {
                return _actualObservations;
            }
        }

        internal bool HasPinnedWorldFrame
        {
            get
            {
                return _pendingPinnedOriginalWorld != null;
            }
        }

        internal void Revoke()
        {
            _revoked = true;
            _observed.Clear();
            _actualObservations = 0;
            _pendingPinnedOriginalWorld = null;
            _pendingPinnedWorldSequence = 0;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private OriginalMapClockCursor _originalMpClock;
        private OriginalConsentFrameHistory _originalMpFrames;
        // Clock lookup bound to ORIGINAL cached managed IL SHA pin:
        // VerifiedManagedSurface.RawraPins.GetElapsedMapTicks()
        //   e836333c3707d320c4698c4cc29c08ab90ec34c02c4405e25126d61d4c4195ce
        // This is NOT the old local OnTick/Interlocked callback counter.
        private bool TryReadOriginalMapTick(out long actualOriginalTick, out string reason)
        {
            actualOriginalTick = 0;
            reason = "original_elapsed_map_tick_unavailable";
            if (!IsNativeDiplomacySectionHeld || _signed == null || !_signed.Live || !_signed.Ready || _mapEpoch < 1 || _signed.Context == null || _signed.Descriptor == null)
                return false;
            try
            {
                if (_originalMpClock == null)
                {
                    string pinResult;
                    if (!VerifiedManagedSurface.VerifyRawraAssembly(out pinResult))
                    {
                        reason = "original_tick_method_IL_pin_not_attested:" + pinResult;
                        return false;
                    }

                    _originalMpClock = new OriginalMapClockCursor(_signed.Context, _signed.Descriptor.Id, _mapEpoch);
                }

                var api = GameTimeManagerAPI.Instance;
                if (api == null)
                    return false;
                int nativeSigned = api.GetElapsedMapTicks();
                return _originalMpClock.TryObserve(nativeSigned, _signed.Context, _signed.Descriptor.Id, _mapEpoch, out actualOriginalTick, out reason);
            }
            catch (Exception error)
            {
                reason = "original_elapsed_map_tick_refused:" + error.GetType().Name;
                return false;
            }
        }

        // Trigger from the EXISTING original managed post-native tick owner,
        // at each actually observed GetElapsedMapTicks value. If the engine
        // advances multiple ticks between callbacks, only the LAST truly
        // observed world may be recorded; past ticks are NEVER fabricated.
        private bool TryCaptureActualConsentWorldForCurrentNativeTick(ulong effectiveSignedCollectorInputEpoch, PublishedSignedChoreFrontier originalConsumedAndBaseFrontier, out string reason)
        {
            reason = "original_consent_frame_not_yet_attested";
            if (!IsNativeDiplomacySectionHeld || originalConsumedAndBaseFrontier == null || effectiveSignedCollectorInputEpoch < 1 || originalConsumedAndBaseFrontier.EffectiveSignedInputEpoch != effectiveSignedCollectorInputEpoch)
                return false;
            long current;
            string clockReason;
            if (!TryReadOriginalMapTick(out current, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            OriginalMultiplayerConsentFrame world;
            if (!TryObserveOriginalMpTradeOnlyFrame(current, out world, out reason))
                return false;
            long after;
            if (!TryReadOriginalMapTick(out after, out clockReason) || after != current)
            {
                reason = "original_clock_advanced_during_consent_frame_capture";
                return false;
            }

            if (_originalMpFrames == null)
                _originalMpFrames = new OriginalConsentFrameHistory(_signed.Context, _signed.Descriptor.Id, _mapEpoch, effectiveSignedCollectorInputEpoch);
            return _originalMpFrames.TryRecordActualOriginalFrame(world, effectiveSignedCollectorInputEpoch, after, _signed.Context, _signed.Descriptor.Id, originalConsumedAndBaseFrontier, true, out reason);
        }

        // Read from OLD recorded, ORIGINAL native frame T after delayed
        // original RSA+replay-verification of Kind8 at current native U>=T.
        // This does not issue native orders, saves, goods or controller IDs.
        private bool TryResolveDelayedKind8ConsentFrame(OrderedHostWorldWitness alreadyVerifiedOriginalKind8, ulong actuallyConsumedOriginalChoreSequence, out IOriginalConsentWorldSnapshot consentOnly, out string reason)
        {
            consentOnly = null;
            reason = "original_world_frame_was_not_captured";
            if (!IsNativeDiplomacySectionHeld || _originalMpFrames == null)
                return false;
            long current;
            string clockReason;
            if (!TryReadOriginalMapTick(out current, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            CapturedConsentWorldFrame original;
            if (!_originalMpFrames.TryMatchDelayedVerifiedWorldFrame(alreadyVerifiedOriginalKind8, actuallyConsumedOriginalChoreSequence, current, ReadOriginalNativeDay(), _signed.Context, _signed.Descriptor.Id, _mapEpoch, true, out original, out reason))
                return false;
            consentOnly = original.OriginalObservation;
            return true;
        }

        // HOST PREPARE (not sign/queue): attach Kind8's 101-byte
        // OrderedWorldDigestBodyCodec body to the one existing original
        //   HostBootstrap.SignCommand/QueueWorldFrame extension.
        // Actual original tick and goods MUST be observed before
        // the existing strict Chore queue obtains its signed body.
        private bool TryPrepareHostKind8ConsentFrameCandidate(ulong effectiveSignedCollectorInputEpoch, ulong nextActualHostChoreSequence, PublishedSignedChoreFrontier originalConsumedAndBaseFrontier, out OrderedHostWorldWitness unsignedHostContent, out string reason)
        {
            unsignedHostContent = null;
            reason = "host_world_frame_not_captured";
            if (!IsNativeDiplomacySectionHeld || _signed == null || _signed.Context == null || _signed.Context.LocalSteamId != _signed.Context.HostSteamId || effectiveSignedCollectorInputEpoch < 1 || nextActualHostChoreSequence < 2 || nextActualHostChoreSequence >= ulong.MaxValue - 1 || originalConsumedAndBaseFrontier == null || originalConsumedAndBaseFrontier.EffectiveSignedInputEpoch != effectiveSignedCollectorInputEpoch || originalConsumedAndBaseFrontier.VerifiedGlobalSignedSequence != nextActualHostChoreSequence - 1 || originalConsumedAndBaseFrontier.CanonicallyPublishedGlobalSignedSequence > originalConsumedAndBaseFrontier.VerifiedGlobalSignedSequence)
                return false;
            if (!TryCaptureActualConsentWorldForCurrentNativeTick(effectiveSignedCollectorInputEpoch, originalConsumedAndBaseFrontier, out reason))
                return false;
            long tick;
            string clockReason;
            if (!TryReadOriginalMapTick(out tick, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            CapturedConsentWorldFrame original;
            if (!_originalMpFrames.TryGetExactActuallyCapturedFrame(tick, _signed.Context, _signed.Descriptor.Id, _mapEpoch, effectiveSignedCollectorInputEpoch, nextActualHostChoreSequence, originalConsumedAndBaseFrontier, out original))
            {
                reason = "host_original_world_advanced_before_signing";
                return false;
            }

            unsignedHostContent = new OrderedHostWorldWitness(_signed.Descriptor.Id, original.ConsentOnlyDigest(), nextActualHostChoreSequence, _signed.Context.HostSteamId, original.ActualOriginalMapTick, effectiveSignedCollectorInputEpoch);
            reason = "host_candidate_for_one_existing_strict_kind8_chore";
            return true;
        }

        // calls this only after the existing original strict Chore
        // queue has retained the exact signer's WorldFrame8 bytes. No
        // independent signature, second cache or invented delivery status.
        private bool TryRetainQueuedHostSignedWorldFrame(OrderedHostWorldWitness actuallyQueuedHostWorldFrame, ulong lastVerifiedConsumedInputSequence, out string reason)
        {
            reason = "no_actual_signed_world_chore_queued";
            return IsNativeDiplomacySectionHeld && _signed != null && _originalMpFrames != null && _originalMpFrames.TryRetainHostQueuedOriginalWorldFrame(actuallyQueuedHostWorldFrame, lastVerifiedConsumedInputSequence, _signed.Context, _signed.Descriptor.Id, _mapEpoch, out reason);
        }

        // calls this after the ONE RSA-verified ordered
        // Engine-selected PublicationBarrier10 S+1. A failed/aborted
        // original session instead uses Revoke... under lifecycle.
        private bool TryReleasePinnedAfterOrderedBarrier(ulong exactWorldFrameSequence, ulong actuallyConsumedSignedBarrierSequence, out string reason)
        {
            reason = "no_original_world_frame_awaiting_barrier";
            return IsNativeDiplomacySectionHeld && _originalMpFrames != null && _originalMpFrames.TryReleasePinnedAfterVerifiedOrderedBarrier(exactWorldFrameSequence, actuallyConsumedSignedBarrierSequence, out reason);
        }

        // MANAGED POST-CHORE CALLBACK only. The existing ChoreSession has
        // already authenticated and consumed Kind8's original global
        // sequence. The original native map clock may now be LATER than T.
        // This is the causal bridge from retained exact T to 's
        // all-human comparison, never current-game rewind or Native release.
        private bool TryPinDelayedVerifiedKind8ToOriginalPeerAgreement(PeerWorldFrameAgreement agreement, OrderedHostWorldWitness originalRsaVerifiedKind8, ulong actuallyConsumedOriginalGlobalSequence, out string reason)
        {
            reason = "delayed_kind8_not_in_original_chore";
            if (!IsNativeDiplomacySectionHeld || _originalMpFrames == null || agreement == null || _signed == null)
                return false;
            long actualNow;
            string clockReason;
            if (!TryReadOriginalMapTick(out actualNow, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            int day = ReadOriginalNativeDay();
            CapturedConsentWorldFrame exactOriginal;
            if (!_originalMpFrames.TryMatchDelayedVerifiedWorldFrame(originalRsaVerifiedKind8, actuallyConsumedOriginalGlobalSequence, actualNow, day, _signed.Context, _signed.Descriptor.Id, _mapEpoch, true, out exactOriginal, out reason))
                return false;
            return agreement.PinPreviouslyCapturedOriginalHostConsentFrame(exactOriginal, originalRsaVerifiedKind8, actuallyConsumedOriginalGlobalSequence, _signed.Context, _signed.Descriptor.Id, actualNow, day, true, out reason);
        }

        // Each official Steam-origin peer echo is UNTRUSTED content until
        // the caller uses its real SteamDelivery and the current original
        // GameNetworkAPI slot mapper. Read current ORIGINAL tick here.
        private bool TryObserveOriginalPeerEchoForAgreement(PeerWorldFrameAgreement agreement, AuthenticatedPeerFrameEcho echo, SteamDelivery officialOriginalDelivery, Func<byte, ulong, bool> originalLiveSlotToSteamPredicate, out string reason)
        {
            reason = "original_peer_echo_unauthenticated";
            if (!IsNativeDiplomacySectionHeld || agreement == null || _signed == null || originalLiveSlotToSteamPredicate == null)
                return false;
            long actualNow;
            string clockReason;
            if (!TryReadOriginalMapTick(out actualNow, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            return agreement.ObserveOriginalSteamPeer(echo, officialOriginalDelivery, originalLiveSlotToSteamPredicate, true, _signed.Context, _signed.Descriptor.Id, actualNow, ReadOriginalNativeDay(), out reason);
        }

        private bool TryConfirmAllOriginalPeerConsentWorldEchos(PeerWorldFrameAgreement agreement, out string reason)
        {
            reason = "original_peer_world_not_unanimous";
            if (!IsNativeDiplomacySectionHeld || agreement == null || _signed == null)
                return false;
            long actualNow;
            string clockReason;
            if (!TryReadOriginalMapTick(out actualNow, out clockReason))
            {
                reason = clockReason;
                return false;
            }

            return agreement.AllCurrentHumanReportsAgree(_signed.Context, _signed.Descriptor.Id, actualNow, ReadOriginalNativeDay(), out reason);
        }

        // Coordinator's original map/load/disconnect/hostchange lifecycle
        // must call THIS along with ResetSignedSession. No new hooks.
        private void RevokeTradeOnlyOriginalClockAndWorldHistory()
        {
            if (_originalMpClock != null)
                _originalMpClock.Revoke();
            if (_originalMpFrames != null)
                _originalMpFrames.Revoke();
            _originalMpClock = null;
            _originalMpFrames = null;
        }

        // Use ONLY after the existing owner has actually published signed
        // Activation and new canonical input highwater via Schema19; it
        // cannot rewind the native clock or create an unsigned epoch.
        private void RevokeOldConsentFrameEpochAfterSignedPublication()
        {
            if (_originalMpFrames != null)
                _originalMpFrames.Revoke();
            _originalMpFrames = null;
        }
    }
}
