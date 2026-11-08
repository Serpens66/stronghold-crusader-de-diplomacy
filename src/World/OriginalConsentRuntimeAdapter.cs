// Concrete managed original lane: invokes these methods from the
// EXISTING DrainOrderedChores and managed post-native tick, never from
// raw ReceiveChore prefix. ONE selected _signed ChoreSession remains owner.
using System;
using System.IO;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // WorldAI owns the ONE OriginalConsentFrameHistory field.
        // No controller-owned duplicate digest or retained original frame map.
        private SignedTradeActionBatchCollector _consentCollector;
        private OriginalMultiplayerConsentFrame _latestOriginalFrame;
        private static ulong GlobalHostTerm(OriginalMultiplayerConsentFrame world)
        {
            // Same pinned scope among peers: never local mapEpoch, process RNG
            // or per-machine time. This scope must be preserved across cold load.
            byte[] scope = world.VerifiedRoster.Scope();
            if (scope == null || scope.Length != 128)
                throw new InvalidDataException("Missing signed global match scope");
            ulong term = BitConverter.ToUInt64(scope, 64);
            if (term == 0 || term == ulong.MaxValue)
                throw new InvalidDataException("Invalid durable global MP term");
            return term;
        }

        // These THREE exact callbacks are supplied to 's chosen
        // HostSignedTradeSession.ConsumeOrderedMixed(...,worldFrameReducer).
        private void OnVerifiedSignedActivation(AuthenticatedHumanActionActivation activation)
        {
            if (!IsNativeDiplomacySectionHeld || _consentCollector == null)
                throw new InvalidOperationException("Signed MP activation without owner");
            _consentCollector.OnVerifiedActivation(activation);
        }

        private void OnVerifiedSignedHumanAction(HumanTradeActionEnvelope action)
        {
            if (!IsNativeDiplomacySectionHeld || _consentCollector == null)
                throw new InvalidOperationException("Signed MP action without owner");
            _consentCollector.OnVerifiedHumanAction(action);
        }

        // The engine adapter supplies actual ORIGINAL clock and original outer
        // under the already held original monitor. The Kind8 body has ALREADY
        // passed the SAME  RSA-ChoreSession replay window upstream.
        private void OnVerifiedSignedWorldFrame(OrderedHostWorldWitness signedWorld, long currentOriginalNativeTick, int currentOriginalDay, LegacyDiplomacySave genuinelyObservedOriginalOuter)
        {
            if (!IsNativeDiplomacySectionHeld || _consentCollector == null || _signed == null || !_signed.Live || !_signed.Ready || _canonical == null || signedWorld == null)
                throw new InvalidOperationException("WorldFrame not under original  Chore");
            TrustedContext current;
            string key, reason;
            if (!ReadTrustedNetworkContext(out current, out key, out reason) || key != _trustedKey || current.Generation != _signed.Context.Generation || !current.Scope.Same(_signed.Context.Scope))
                throw new InvalidDataException("Original Steam session changed");
            // A delayed host frame need not equal CURRENT native tick. The cache
            // must contain its original immutable content observed at signed tick n.
            CapturedConsentWorldFrame worldObserved;
            if (_originalMpFrames == null || !_originalMpFrames.TryMatchDelayedVerifiedWorldFrame(signedWorld, signedWorld.SignedHostChoreSequence, currentOriginalNativeTick, currentOriginalDay, current, _signed.Descriptor.Id, _mapEpoch, true, out worldObserved, out reason))
                throw new InvalidDataException("Missing original WorldAI delayed signed frame: " + reason);
            var retained = new ObservedOriginalConsentFrame(worldObserved);
            _consentCollector.OnVerifiedWorldFrame(signedWorld);
            var stored = CanonicalTradeCodec.Decode(_canonical.SaveBytes());
            var ticket = _consentCollector.BuildForAuthenticatedConsentFrame(retained, stored.InputEpoch);
            var stage = _canonical.PrepareOriginalMpConsentOnly(ticket, retained);
            // never publish on WorldFrame delivery. Only stage
            // the ACTUAL existing owner serializer to a verified tentative readback,
            // then issue one sender-bound PeerReady through 's existing 1902.
            // The canonical _model, _save, /63 remain EXACTLY before-state.
            StageCandidateForOrderedCommit(stage, ticket, retained, signedWorld, current, genuinelyObservedOriginalOuter, (slot, steam) => _boundSteamParticipants != null && slot >= 1 && slot <= 8 && _boundSteamParticipants[slot] == steam);
        // Engine takes GetOriginalLocalPreparedBody(), sends via its
        // existing official Steam handler, and routes PeerReady/HostCommit
        // through the ONE selected signed Chore stream.
        // reads the typed Ready and feeds its ONE actual host quorum.
        // No controller-owned quorum or unsigned local publication.
        }

        // Called only on original map end, rejoin or original host/key change,
        // while preserving an independently staged LoadData payload when loading.
        private void RevokeOriginalMpLifecycle(bool preserveStagedLoad)
        {
            RevokeUnconfirmedConsentBarrier();
            if (_consentCollector != null)
                _consentCollector.Invalidate();
            _consentCollector = null;
            RevokeTradeOnlyOriginalClockAndWorldHistory();
            _latestOriginalFrame = null;
            // A disconnect/rejoin is not permission to overwrite existing MP
            // consent with map_inactive. Keep external saves delayed until reattested.
            // Generic canonical bytes also belong to SOLO. Only actual MP ownership
            // or an explicitly staged MP load may keep the MP external-save guard alive.
            _mpBootstrapRequested = _mpBootstrapRequested || _mpConsentMode || _pendingOriginalMpLoadBytes != null;
            _mpConsentMode = false;
            _existingEngineSubmitIntent = null;
            _originalOuterSeed = null;
            _confirmedOuterSaveBytes = null;
            if (!preserveStagedLoad)
                _pendingOriginalMpLoadBytes = null;
            ClearPendingLocalUiIntent();
        // If a staged publication is ambiguous, leave controller's pending
        // candidate quarantined and don't silently repeat its host Chore.
        }
    }
}
