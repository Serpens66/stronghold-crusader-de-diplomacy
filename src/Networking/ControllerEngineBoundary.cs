// Exact successor API. All calls run under original engine monitor and
// the existing feature gate; no new save/network/world owner.
using System;
using DiplomacyPersistenceCodec = StrongholdEuropeDE.Runtime.DiplomacyStateCodec;
using System.IO;
using System.Threading;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private void InitializeOriginalConsentOwner(OriginalMultiplayerConsentFrame observed, TrustedContext current)
        {
            if (current == null || _signed == null || current.Generation != _signed.Context.Generation || !current.Scope.Same(_signed.Context.Scope))
                throw new InvalidDataException("Original initialization context differs");
            EnsureOriginalMpConsentOwner(observed);
        }

        private PersistedHumanActionCursor ReadSavedCanonicalActor(byte actor)
        {
            return ReadOriginalSavedActor(actor);
        }

        private ulong CapturedSignedInputEpoch()
        {
            if (_consentCollector != null && _consentCollector.EffectiveSignedInputEpochHint > 0)
                return _consentCollector.EffectiveSignedInputEpochHint;
            if (_canonicalSaved == null)
                return 0;
            return CanonicalTradeCodec.Decode(_canonicalSaved).InputEpoch;
        }

        private ulong NextOriginalActivationEpoch()
        {
            if (_canonicalSaved == null)
                throw new InvalidOperationException("No original base owner");
            ulong current = CanonicalTradeCodec.Decode(_canonicalSaved).InputEpoch;
            if (current >= ulong.MaxValue - 2)
                throw new InvalidDataException("MP input epoch exhausted");
            return current + 1;
        }

        private void StageVerifiedWorldFrame(OrderedHostWorldWitness signedWorld, long nativeNow, int nativeDay)
        {
            if (_originalMpFrames == null || _signed == null)
                throw new InvalidOperationException("No selected original frame history");
            CapturedConsentWorldFrame retained;
            string reason;
            if (!_originalMpFrames.TryMatchDelayedVerifiedWorldFrame(signedWorld, signedWorld.SignedHostChoreSequence, nativeNow, nativeDay, _signed.Context, _signed.Descriptor.Id, _mapEpoch, true, out retained, out reason))
                throw new InvalidDataException("Original retained consent frame unavailable: " + reason);
            var outer = CaptureExistingOriginalMpOuter(retained.OriginalObservation);
            OnVerifiedSignedWorldFrame(signedWorld, nativeNow, nativeDay, outer);
        }

        private bool TryGetPreparedPublication(out PublicationFrameBinding binding, out PeerPublicationAcknowledgement ready)
        {
            binding = _controllerBinding;
            ready = _controllerReady;
            return binding != null && ready != null && _stagedConsent != null && ready.Binding.Same(binding);
        }

        private PeerPublicationAcknowledgement PublishVerifiedBarrier(SignedPublicationBarrier barrier, ulong consumed)
        {
            OnOriginalSignedOrderedConsentCommit(barrier, consumed);
            return _controllerApplied;
        }

        private byte[] ReadActualBeforeCanonicalOwnerBytes()
        {
            if (!IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || _canonicalSaved == null || _originalOuterSeed == null)
                throw new InvalidOperationException("Original BEFORE owner not readable");
            byte[] serialized;
            var before = ReadCurrentThroughOriginalSaveData(DiplomacyPersistenceCodec.Deserialize(DiplomacyPersistenceCodec.Serialize(_originalOuterSeed, _rules), _rules), out serialized);
            // Return only the actual original handler's deserialized subrecord.
            return (byte[])before.CanonicalTradeState.Clone();
        }

        private byte[] ReadActualPublishedOwnerProjectionSha()
        {
            if (_confirmedOuterSaveBytes == null || _canonicalSaved == null || !IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate))
                throw new InvalidOperationException("No actual published original owner");
            byte[] serialized;
            var outer = DiplomacyPersistenceCodec.Deserialize(_confirmedOuterSaveBytes, _rules);
            var readback = ReadCurrentThroughOriginalSaveData(outer, out serialized);
            return SharedOwnerReadbackSha(readback);
        }

        private void RevokeControllerOriginalSession(bool preserveStagedLoad)
        {
            RevokeOriginalMpLifecycle(preserveStagedLoad);
        }
    }
}
