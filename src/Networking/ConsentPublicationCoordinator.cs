// Controller adapter for ENGINE-owned 188-byte Kind9/Kind10 publication ABI.
// No second quorum, signer, Steam handler, world digest or frame dictionary.
using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private PendingConsentPublication _stagedConsent;
        private SignedTradeTickBatch _stagedTicket;
        private ObservedOriginalConsentFrame _stagedOriginal;
        private LegacyDiplomacySave _tentativeOriginalOuter;
        private byte[] _tentativeSerializedOuter;
        private PublicationFrameBinding _controllerBinding;
        private PeerPublicationAcknowledgement _controllerReady, _controllerApplied;
        private static byte[] HashOwnerBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        // Shared projection excludes LocalPlayerId and local-only compatibility
        // relations. Each peer MUST nevertheless roundtrip its FULL actual outer.
        private static byte[] SharedOwnerReadbackSha(LegacyDiplomacySave outer)
        {
            byte[] legacy = OriginalProjectionValidation.SerializeProjection(outer.TradeArchive, outer.TradeMailbox, outer.LastProcessedDay, outer.MapFingerprint, outer.RosterFingerprint);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.SavedOwnerProjectionDomain);
                writer.Write(outer.CanonicalTradeState.Length);
                writer.Write(outer.CanonicalTradeState);
                writer.Write(legacy.Length);
                writer.Write(legacy);
                writer.Flush();
                return HashOwnerBytes(stream.ToArray());
            }
        }

        private PublicationFrameBinding StageCandidateForOrderedCommit(PendingConsentPublication stage, SignedTradeTickBatch ticket, ObservedOriginalConsentFrame original, OrderedHostWorldWitness signedWorld, TrustedContext current, LegacyDiplomacySave actualOuter, Func<byte, ulong, bool> slotMapper)
        {
            if (stage == null || ticket == null || original == null || signedWorld == null || current == null || slotMapper == null || !IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || _stagedConsent != null || !slotMapper(original.Original.LocalOriginalHumanSlot, current.LocalSteamId))
                throw new InvalidOperationException("Original MP candidate owner busy");
            byte[] preliminary, actual;
            var projected = ProjectToExistingOriginalSaveEnvelope(actualOuter, stage, original.Original, out preliminary);
            var readback = ReadCandidateThroughOriginalSaveData(projected, stage.CandidateBytes(), out actual);
            if (!ExistingSaveReceiptGate.ExactBytes(preliminary, actual))
                throw new InvalidDataException("Actual original owner candidate differs");
            var binding = new PublicationFrameBinding(stage.SignedSession, signedWorld.SignedHostChoreSequence, stage.OriginalTick, stage.OriginalDay, stage.InputEpoch, signedWorld.SignedHostChoreSequence - 1, stage.BaseRevision, signedWorld.OriginalWorldSha256, HashOwnerBytes(readback.CanonicalTradeState), SharedOwnerReadbackSha(readback));
            _stagedConsent = stage;
            _stagedTicket = ticket;
            _stagedOriginal = original;
            _tentativeOriginalOuter = readback;
            _tentativeSerializedOuter = actual;
            _controllerBinding = binding;
            _controllerApplied = null;
            _controllerReady = new PeerPublicationAcknowledgement(PublicationReceiptPhase.CandidateReady, original.Original.LocalOriginalHumanSlot, 0, binding);
            return binding;
        }

        // uses these exact typed objects in its sole host quorum/1902 router.
        private PublicationFrameBinding GetControllerPreparedBinding()
        {
            return _controllerBinding;
        }

        private PeerPublicationAcknowledgement GetControllerLocalReady()
        {
            return _controllerReady;
        }

        private PeerPublicationAcknowledgement GetControllerLocalApplied()
        {
            return _controllerApplied;
        }

        private byte[] GetOriginalLocalPreparedBody()
        {
            return _controllerReady == null ? null : _controllerReady.EncodeBody();
        }

        // ONLY the existing RSA-verified global Kind10 Chore callback.
        private void OnOriginalSignedOrderedConsentCommit(SignedPublicationBarrier barrier, ulong consumedSequence)
        {
            if (!IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || barrier == null || barrier.GlobalChoreSequence != consumedSequence || _controllerBinding == null || !_controllerBinding.Same(barrier.Binding) || _stagedConsent == null || _stagedOriginal == null || _signed == null || !_signed.Live || !_signed.Ready)
                throw new InvalidDataException("No exact original signed publication stage");
            TrustedContext current;
            string key, why;
            if (!ReadTrustedNetworkContext(out current, out key, out why) || key != _trustedKey)
                throw new InvalidDataException("Original publication Steam lifecycle changed");
            // Preallocate the applied report BEFORE collector ACK.
            var applied = new PeerPublicationAcknowledgement(PublicationReceiptPhase.OwnerReadbackApplied, _stagedOriginal.Original.LocalOriginalHumanSlot, consumedSequence, barrier.Binding);
            PublishOriginalMpConsentUnderExistingOwner(_stagedConsent, _stagedTicket, _consentCollector, _stagedOriginal.Original, _tentativeOriginalOuter);
            _controllerApplied = applied;
            _stagedConsent = null;
            _stagedTicket = null;
            _stagedOriginal = null;
            _tentativeOriginalOuter = null;
            _tentativeSerializedOuter = null;
            _controllerReady = null;
            AcknowledgeUiFromOriginalSavedCanonical();
        }

        private void RevokeUnconfirmedConsentBarrier()
        {
            if (_stagedConsent != null)
                _mpQuarantined = true;
            _stagedConsent = null;
            _stagedTicket = null;
            _stagedOriginal = null;
            _tentativeOriginalOuter = null;
            _tentativeSerializedOuter = null;
            _controllerBinding = null;
            _controllerReady = null;
            _controllerApplied = null;
        // The actual canonical pending stage is preserved until exact rollback or
        // independently verified cold restore. Never ACK an ambiguous revocation.
        }
    }
}
