//  source-only peer-frame comparison. It is NOT a new transport,
// SaveData owner, ChoreSession, packet registration, or native goods authority.
// Reports from the existing original Steam event are intentionally UNTRUSTED
// observations until matched to that event's real sender and pinned session.
// Host-signed world identity must still be checked in the ONE existing Chore.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class AuthenticatedPeerFrameEcho
    {
        // Exactly 90 bytes: independent local mapEpoch and sessionGeneration
        // are not included. Original Steam metadata determines the actor.
        internal const uint Magic = 0x31323156; // ASCII , little endian
        internal const byte Version = 1;
        internal const int EncodedLength = 4 + 1 + 32 + 8 + 8 + 4 + 1 + 32;
        internal readonly Bytes SessionId;
        internal readonly ulong CanonicalInputEpoch;
        internal readonly long OriginalSharedTick;
        internal readonly int OriginalNativeDay;
        internal readonly byte ClaimedActorSlot;
        private readonly byte[] _worldDigest;
        internal AuthenticatedPeerFrameEcho(Bytes originalSessionId, ulong epoch, long sharedTick, int nativeDay, byte playerSlot, byte[] reportedWorldDigest)
        {
            if (originalSessionId == null || originalSessionId.Length != 32 || epoch < 1 || epoch >= ulong.MaxValue - 1 || sharedTick < 1 || nativeDay < 1 || playerSlot < 1 || playerSlot > 8 || reportedWorldDigest == null || reportedWorldDigest.Length != 32)
                throw new ArgumentException("Invalid bounded peer-frame content");
            SessionId = new Bytes(originalSessionId.Copy());
            CanonicalInputEpoch = epoch;
            OriginalSharedTick = sharedTick;
            OriginalNativeDay = nativeDay;
            ClaimedActorSlot = playerSlot;
            _worldDigest = (byte[])reportedWorldDigest.Clone();
        }

        internal byte[] ReportedDigest()
        {
            return (byte[])_worldDigest.Clone();
        }

        internal byte[] Encode()
        {
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(SessionId.Copy());
                w.Write(CanonicalInputEpoch);
                w.Write(OriginalSharedTick);
                w.Write(OriginalNativeDay);
                w.Write(ClaimedActorSlot);
                w.Write(_worldDigest);
                w.Flush();
                if (s.Length != EncodedLength)
                    throw new InvalidDataException("Peer-frame layout not canonical");
                return s.ToArray();
            }
        }

        internal static bool TryDecode(byte[] raw, out AuthenticatedPeerFrameEcho value)
        {
            value = null;
            if (raw == null || raw.Length != EncodedLength)
                return false;
            try
            {
                using (var s = new MemoryStream(raw, false))
                using (var r = new BinaryReader(s))
                {
                    if (r.ReadUInt32() != Magic || r.ReadByte() != Version)
                        return false;
                    byte[] session = r.ReadBytes(32);
                    ulong epoch = r.ReadUInt64();
                    long tick = r.ReadInt64();
                    int day = r.ReadInt32();
                    byte slot = r.ReadByte();
                    byte[] digest = r.ReadBytes(32);
                    if (s.Position != EncodedLength || session.Length != 32 || digest.Length != 32)
                        return false;
                    var decoded = new AuthenticatedPeerFrameEcho(new Bytes(session), epoch, tick, day, slot, digest);
                    if (!decoded.Encode().SequenceEqual(raw))
                        return false;
                    value = decoded;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // An EPHEMERAL no-goods state, not a SaveData business journal or an
    // authenticated peer signature. All original Steam senders must report
    // identical content before the coordinator can even CONSIDER reducing a
    // common tick. It never supplies authorization for Native MP commits.
    internal sealed class PeerWorldFrameAgreement
    {
        private readonly TrustedContext _originalHostContext;
        private readonly Bytes _pinnedSessionId;
        private readonly ulong _epoch;
        private readonly long _sharedTick;
        private readonly int _day;
        // Pure safety budget in ORIGINAL simulation ticks, never elapsed wall
        // time. Waiting beyond the bounded frame is a refusal, not a permit
        // to reuse old goods / quote terms from a future simulation frame.
        internal const long MaximumUnpublishedFrameAgeTicks = 600;
        private long _lastObservedNativeTick;
        private readonly ulong[] _expectedHumans;
        private readonly Dictionary<ulong, byte[]> _reported = new Dictionary<ulong, byte[]>();
        private byte[] _hostDigest;
        private ulong _signedHostWorldChoreSequence;
        private bool _invalidated;
        private string _invalidReason;
        internal PeerWorldFrameAgreement(TrustedContext originalHostContext, Bytes singleOriginalSignedSessionId, ulong savedCanonicalInputEpoch, long sharedTick, int nativeDay)
        {
            if (originalHostContext == null || originalHostContext.LocalSteamId != originalHostContext.HostSteamId || originalHostContext.Generation < 1 || originalHostContext.Scope == null || originalHostContext.Scope.Length != 128 || singleOriginalSignedSessionId == null || singleOriginalSignedSessionId.Length != 32 || savedCanonicalInputEpoch < 1 || savedCanonicalInputEpoch >= ulong.MaxValue - 1 || sharedTick < 1 || nativeDay < 1)
                throw new ArgumentException("No original pinned host world frame");
            _originalHostContext = originalHostContext;
            _pinnedSessionId = new Bytes(singleOriginalSignedSessionId.Copy());
            _epoch = savedCanonicalInputEpoch;
            _sharedTick = sharedTick;
            _day = nativeDay;
            _lastObservedNativeTick = sharedTick;
            _expectedHumans = originalHostContext.HumanIds();
            if (_expectedHumans.Length < 2 || _expectedHumans.Length > 8 || !_expectedHumans.Contains(originalHostContext.HostSteamId))
                throw new InvalidDataException("Incomplete actual multiplayer roster");
        }

        internal bool Invalidated
        {
            get
            {
                return _invalidated;
            }
        }

        internal int ExpectedHumans
        {
            get
            {
                return _expectedHumans.Length;
            }
        }

        internal int ObservedHumans
        {
            get
            {
                return _reported.Count;
            }
        }

        internal string FailureReason
        {
            get
            {
                return _invalidReason;
            }
        }

        private bool Refuse(string reason, out string status)
        {
            if (!_invalidated)
            {
                _invalidReason = reason;
                _invalidated = true;
            }

            status = _invalidReason;
            return false;
        }

        // Check CURRENT ORIGINAL local host lifecycle on every admission and
        // before reporting success. The shared InputEpoch is NOT the local
        // map/session generation: retain both in their correct scopes.
        internal bool ObserveCurrentOriginalLifecycle(TrustedContext currentOriginalContext, Bytes currentOriginalSignedSessionId, long originalNativeTick, int originalNativeDay, out string status)
        {
            status = "unverified_original_lifecycle";
            if (_invalidated)
            {
                status = _invalidReason;
                return false;
            }

            if (currentOriginalContext == null || currentOriginalSignedSessionId == null || currentOriginalContext.Generation != _originalHostContext.Generation || currentOriginalContext.HostSteamId != _originalHostContext.HostSteamId || currentOriginalContext.LocalSteamId != _originalHostContext.LocalSteamId || !currentOriginalContext.Scope.Same(_originalHostContext.Scope) || !currentOriginalContext.HumanIds().SequenceEqual(_expectedHumans) || !currentOriginalSignedSessionId.Same(_pinnedSessionId) || originalNativeTick < _sharedTick || originalNativeTick < _lastObservedNativeTick || originalNativeDay < _day)
                return Refuse("original_map_session_or_native_clock_changed", out status);
            // A bounded later native delivery day retains the exact captured
            // consent business boundary; only a rewind invalidates identity.
            if (originalNativeTick - _sharedTick > MaximumUnpublishedFrameAgeTicks)
            {
                status = "original_frame_expired_new_frame_progress_allowed";
                return false;
            }

            _lastObservedNativeTick = originalNativeTick;
            status = "same_original_lifecycle_observed_without_native_grant";
            return true;
        }

        // This is called ONLY by the existing post-native ChoreSession RSA
        // callback. It does not authenticate the witness merely by decoding
        // its bytes. Crucially, the host must also independently observe its
        // OWN real native 19-good world before accepting its signed hash.
        internal bool PinAuthenticatedHostWorld(OrderedHostWorldWitness actualOrderedSignedWorld, ulong actualVerifiedChoreSequence, CanonicalTradeSnapshot currentHostOriginalWorld, VerifiedOriginalHumanRoster currentHostOriginalRoster, TrustedContext currentOriginalContext, Bytes currentOriginalSignedSessionId, bool originalEngineMonitorHeld, out string status)
        {
            status = "unverified_original_host_world";
            if (_invalidated)
            {
                status = _invalidReason;
                return false;
            }

            if (!originalEngineMonitorHeld || !DiplomacyFeature.IsNativeDiplomacySectionHeld || currentHostOriginalWorld == null || currentHostOriginalRoster == null)
                return Refuse("original_host_monitor_or_snapshot_missing", out status);
            if (!ObserveCurrentOriginalLifecycle(currentOriginalContext, currentOriginalSignedSessionId, currentHostOriginalWorld.SharedTick, currentHostOriginalWorld.Day, out status))
                return false;
            if (actualOrderedSignedWorld == null || actualOrderedSignedWorld.SignedHostChoreSequence < 1 || actualVerifiedChoreSequence < 1 || actualOrderedSignedWorld.SignedHostChoreSequence != actualVerifiedChoreSequence || actualOrderedSignedWorld.OriginalHostSteamId != _originalHostContext.HostSteamId || actualOrderedSignedWorld.CanonicalInputEpoch != _epoch || actualOrderedSignedWorld.OriginalSharedTick != _sharedTick || !actualOrderedSignedWorld.OriginalSessionId.Same(_pinnedSessionId) || currentHostOriginalWorld.SharedTick != _sharedTick || currentHostOriginalWorld.Day != _day || currentHostOriginalWorld.LocalOfflineHumanSlot != 0 || currentHostOriginalRoster.OriginalSharedTick != _sharedTick || currentHostOriginalRoster.Day != _day || currentHostOriginalRoster.OriginalMapGeneration != currentHostOriginalWorld.MapGeneration || currentHostOriginalRoster.LocalSteamId != _originalHostContext.HostSteamId || currentHostOriginalRoster.HostSteamId != _originalHostContext.HostSteamId || !new Bytes(currentHostOriginalRoster.Scope()).Same(_originalHostContext.Scope))
                return Refuse("wrong_global_host_signed_scope", out status);
            // The external signed content MUST equal the host's actual
            // ORIGINAL engine snapshot under the existing engine monitor.
            // There is no fake self-attesting host digest to tell peers.
            byte[] observed;
            try
            {
                observed = MpSnapshotDigest.Compute(currentHostOriginalWorld, currentHostOriginalRoster);
            }
            catch (Exception exception)
            {
                return Refuse("host_native_snapshot_refused:" + exception.GetType().Name, out status);
            }

            byte[] claimed = actualOrderedSignedWorld.OriginalWorldSha256;
            if (!MpSnapshotDigest.Equal32(observed, claimed))
                return Refuse("host_signed_world_not_equal_original_native", out status);
            if (_hostDigest != null)
            {
                if (_signedHostWorldChoreSequence != actualVerifiedChoreSequence || !MpSnapshotDigest.Equal32(_hostDigest, claimed))
                    return Refuse("conflicting_host_world_replay", out status);
                status = "duplicate_identical_verified_original_host_world";
                return true;
            }

            _hostDigest = observed;
            _signedHostWorldChoreSequence = actualVerifiedChoreSequence;
            _reported.Add(_originalHostContext.HostSteamId, (byte[])_hostDigest.Clone());
            status = "host_signed_and_native_observed_content_no_goods_grant";
            return true;
        }

        // VERSION 2 DELAYED CHORE BARRIER: the original saved world was
        // genuinely observed at native tick T BEFORE it was signed. The
        // existing ChoreSession may deliver it at a later native tick U.
        // This method accepts ONLY the retained, exact, consent-only
        // recorded frame; it never fabricates world values by rewinding U.
        // The enclosing original owner first calls
        // OriginalConsentFrameHistory.TryMatchDelayedVerifiedWorldFrame
        // under its existing post-native managed monitor.
        internal bool PinPreviouslyCapturedOriginalHostConsentFrame(CapturedConsentWorldFrame observedAtRealTick, OrderedHostWorldWitness signedWorld, ulong actuallyVerifiedGlobalChoreSequence, TrustedContext currentOriginalNetwork, Bytes currentSignedOriginalSession, long currentOriginalElapsedMapTick, int currentOriginalNativeDay, bool originalEngineMonitorHeld, out string status)
        {
            status = "delayed_original_consent_world_unattested";
            if (_invalidated)
            {
                status = _invalidReason;
                return false;
            }

            if (!originalEngineMonitorHeld || !DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return Refuse("delayed_frame_original_monitor_missing", out status);
            if (!ObserveCurrentOriginalLifecycle(currentOriginalNetwork, currentSignedOriginalSession, currentOriginalElapsedMapTick, currentOriginalNativeDay, out status))
                return false;
            if (observedAtRealTick == null || signedWorld == null || actuallyVerifiedGlobalChoreSequence == 0 || signedWorld.SignedHostChoreSequence != actuallyVerifiedGlobalChoreSequence || signedWorld.OriginalHostSteamId != _originalHostContext.HostSteamId || !signedWorld.OriginalSessionId.Same(_pinnedSessionId) || signedWorld.CanonicalInputEpoch != _epoch || signedWorld.OriginalSharedTick != _sharedTick || observedAtRealTick.ActualOriginalMapTick != _sharedTick || observedAtRealTick.ActualNativeDay != _day || observedAtRealTick.EffectiveSignedInputEpoch != _epoch || observedAtRealTick.ConsumedSignedInputFrontier >= ulong.MaxValue - 1 || observedAtRealTick.ConsumedSignedInputFrontier + 1 != actuallyVerifiedGlobalChoreSequence || observedAtRealTick.PublishedBaseSignedFrontier > observedAtRealTick.ConsumedSignedInputFrontier || observedAtRealTick.OriginalObservation == null || observedAtRealTick.OriginalObservation.NativePhysicalSettlementAuthorized || observedAtRealTick.OriginalObservation.OriginalHostSteamId != _originalHostContext.HostSteamId || !observedAtRealTick.OriginalObservation.OriginalPinnedSignedSessionId.Same(_pinnedSessionId) || !new Bytes(observedAtRealTick.OriginalObservation.CopyTrustedScope()).Same(_originalHostContext.Scope))
                return Refuse("unproven_original_retained_host_frame", out status);
            byte[] observed;
            try
            {
                observed = ConsentWorldDigest.Compute(observedAtRealTick.OriginalObservation);
            }
            catch (Exception error)
            {
                return Refuse("retained_original_consent_world_invalid:" + error.GetType().Name, out status);
            }

            if (!MpSnapshotDigest.Equal32(observed, observedAtRealTick.ConsentOnlyDigest()) || !MpSnapshotDigest.Equal32(observed, signedWorld.OriginalWorldSha256))
                return Refuse("signed_world_not_equal_retained_native_consent", out status);
            if (_hostDigest != null)
            {
                if (_signedHostWorldChoreSequence != actuallyVerifiedGlobalChoreSequence || !MpSnapshotDigest.Equal32(_hostDigest, observed))
                    return Refuse("conflicting_delayed_host_frame_replay", out status);
                status = "identical_retained_host_frame_already_pinned";
                return true;
            }

            _hostDigest = (byte[])observed.Clone();
            _signedHostWorldChoreSequence = actuallyVerifiedGlobalChoreSequence;
            _reported.Add(_originalHostContext.HostSteamId, (byte[])observed.Clone());
            status = "pinned_original_retained_consent_only_no_native_authority";
            return true;
        }

        // Bind a PEER report to the original Steam event AND original
        // GameNetworkAPI Steam-to-player-slot table under its ORIGINAL
        // engine monitor. A client's self-declared actor in the payload
        // does not establish control of that player. The predicate is
        // coordinator-owned and must use the current original roster.
        internal bool ObserveOriginalSteamPeer(AuthenticatedPeerFrameEcho untrustedEcho, SteamDelivery originalOfficialSteamDelivery, Func<byte, ulong, bool> readActualOriginalSlotMappingUnderMonitor, bool originalEngineMonitorHeld, TrustedContext currentOriginalContext, Bytes currentOriginalSignedSessionId, long currentOriginalNativeTick, int currentOriginalNativeDay, out string status)
        {
            status = "unauthenticated_peer_world_report";
            if (_invalidated)
            {
                status = _invalidReason;
                return false;
            }

            if (!originalEngineMonitorHeld || !DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return Refuse("original_peer_monitor_not_held", out status);
            if (!ObserveCurrentOriginalLifecycle(currentOriginalContext, currentOriginalSignedSessionId, currentOriginalNativeTick, currentOriginalNativeDay, out status))
                return false;
            if (_hostDigest == null)
            {
                status = "host_world_chore_not_seen";
                return false;
            }

            if (untrustedEcho == null || originalOfficialSteamDelivery == null || readActualOriginalSlotMappingUnderMonitor == null || !originalEngineMonitorHeld || originalOfficialSteamDelivery.Generation != _originalHostContext.Generation || !untrustedEcho.SessionId.Same(_pinnedSessionId) || untrustedEcho.CanonicalInputEpoch != _epoch || untrustedEcho.OriginalSharedTick != _sharedTick || untrustedEcho.OriginalNativeDay != _day)
                return Refuse("stale_original_peer_frame_scope", out status);
            ulong steam = originalOfficialSteamDelivery.SenderSteamId;
            if (steam == _originalHostContext.HostSteamId || !_originalHostContext.Contains(steam) || !readActualOriginalSlotMappingUnderMonitor(untrustedEcho.ClaimedActorSlot, steam))
                return Refuse("original_sender_slot_mismatch", out status);
            byte[] peerDigest = untrustedEcho.ReportedDigest();
            if (!MpSnapshotDigest.Equal32(_hostDigest, peerDigest))
                return Refuse("original_peer_world_content_differs", out status);
            byte[] seen;
            if (_reported.TryGetValue(steam, out seen))
            {
                if (!MpSnapshotDigest.Equal32(seen, peerDigest))
                    return Refuse("conflicting_peer_world_replay", out status);
                status = "duplicate_identical_peer_world";
                return true;
            }

            _reported.Add(steam, peerDigest);
            status = "original_sender_matched_world_report_no_native_authority";
            return true;
        }

        internal bool AllCurrentHumanReportsAgree(TrustedContext currentOriginalContext, Bytes currentOriginalSignedSessionId, long currentOriginalNativeTick, int currentOriginalNativeDay, out string status)
        {
            status = _invalidReason ?? "awaiting_original_peer_reports";
            if (!DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return Refuse("original_final_monitor_not_held", out status);
            if (!ObserveCurrentOriginalLifecycle(currentOriginalContext, currentOriginalSignedSessionId, currentOriginalNativeTick, currentOriginalNativeDay, out status))
                return false;
            if (_hostDigest == null || _reported.Count != _expectedHumans.Length)
            {
                status = "awaiting_original_peer_reports";
                return false;
            }

            foreach (ulong steam in _expectedHumans)
            {
                byte[] digest;
                if (!_reported.TryGetValue(steam, out digest) || !MpSnapshotDigest.Equal32(_hostDigest, digest))
                    return false;
            }

            status = "all_original_steam_reports_match_content_only_native_mp_closed";
            return true;
        }

        internal void Invalidate()
        {
            if (!_invalidated)
            {
                _invalidated = true;
                _invalidReason = "original_map_or_host_lifecycle_revoked";
            }

            _reported.Clear();
            _hostDigest = null;
        }
    }
}
