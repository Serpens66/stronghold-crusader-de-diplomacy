// Same ONE original SaveData handler supplies candidate and published readback.
// Memory bytes prove owner projection only; no claim of a durable game .sav.
using System;
using DiplomacyPersistenceCodec = StrongholdEuropeDE.Runtime.DiplomacyStateCodec;
using System.IO;
using System.Threading;
using SHCDESE.API.Components.SaveData;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private LegacyDiplomacySave _internalReadbackOuter;
        // Invoked only by SaveData's internal-owner branch, before its external guard.
        private byte[] SerializeInternalOwnerReadback()
        {
            if (!_originalOwnerProjectionStable || !IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || _internalReadbackOuter == null)
                throw new InvalidOperationException("No original locked SaveData owner section");
            var outer = _internalReadbackOuter;
            outer.CanonicalTradeState = _canonicalSaved == null ? null : (byte[])_canonicalSaved.Clone();
            outer.TradeArchive = _archive == null ? null : _archive.Copy();
            outer.TradeMailbox = _mailbox == null ? null : _mailbox.Copy();
            CanonicalTradeStatePersistence.Validate(outer);
            return DiplomacyPersistenceCodec.Serialize(outer, _rules);
        }

        private LegacyDiplomacySave ReadCurrentThroughOriginalSaveData(LegacyDiplomacySave outer, out byte[] serialized)
        {
            if (!IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || _originalOwnerProjectionStable || outer == null)
                throw new InvalidOperationException("Original current owner not exclusive");
            try
            {
                _internalReadbackOuter = outer;
                _originalOwnerProjectionStable = true;
                serialized = SaveData(null);
                var observed = DiplomacyPersistenceCodec.Deserialize(serialized, _rules);
                CanonicalTradeStatePersistence.Validate(observed);
                return observed;
            }
            finally
            {
                _originalOwnerProjectionStable = false;
                _internalReadbackOuter = null;
            }
        }

        // Stage a candidate through the SAME handler while retaining exact before
        // fields. Nothing is acknowledged until ordered publication repeats readback.
        private LegacyDiplomacySave ReadCandidateThroughOriginalSaveData(LegacyDiplomacySave outer, byte[] canonical, out byte[] serialized)
        {
            if (!IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || _originalOwnerProjectionStable || outer == null)
                throw new InvalidOperationException("Existing owner projection unavailable");
            var oldBytes = _canonicalSaved;
            var oldArchive = _archive;
            var oldMailbox = _mailbox;
            try
            {
                var model = CanonicalTradeCodec.Decode(canonical);
                _canonicalSaved = (byte[])canonical.Clone();
                _archive = model.Archive.Copy();
                _mailbox = model.Mailbox.Copy();
                _internalReadbackOuter = outer;
                _originalOwnerProjectionStable = true;
                serialized = SaveData(null); // the registered original handler, internal scope
                var observed = DiplomacyPersistenceCodec.Deserialize(serialized, _rules);
                CanonicalTradeStatePersistence.Validate(observed);
                if (!ExistingSaveReceiptGate.ExactBytes(observed.CanonicalTradeState, canonical))
                    throw new InvalidDataException("Original SaveData candidate readback differs");
                return observed;
            }
            finally
            {
                _originalOwnerProjectionStable = false;
                _internalReadbackOuter = null;
                _canonicalSaved = oldBytes;
                _archive = oldArchive;
                _mailbox = oldMailbox;
            }
        }

        private void PublishOriginalMpConsentUnderExistingOwner(PendingConsentPublication stage, SignedTradeTickBatch ticket, SignedTradeActionBatchCollector collector, OriginalMultiplayerConsentFrame world, LegacyDiplomacySave originalOuter)
        {
            if (stage == null || ticket == null || collector == null || world == null || _canonical == null || !_mpConsentMode || !IsNativeDiplomacySectionHeld || !Monitor.IsEntered(_gate) || stage.OriginalTick != world.NativeSharedTick || stage.OriginalGeneration != world.LocalOriginalMapGeneration)
                throw new InvalidOperationException("Original SaveData owner not exclusive");
            var oldBytes = _canonicalSaved;
            var oldArchive = _archive;
            var oldMailbox = _mailbox;
            try
            {
                byte[] preliminary;
                var outer = ProjectToExistingOriginalSaveEnvelope(originalOuter, stage, world, out preliminary);
                var model = CanonicalTradeCodec.Decode(stage.CandidateBytes());
                _canonicalSaved = stage.CandidateBytes();
                _archive = model.Archive.Copy();
                _mailbox = model.Mailbox.Copy();
                _internalReadbackOuter = outer;
                _originalOwnerProjectionStable = true;
                // Read actual serialized fields from the original registered handler.
                byte[] actual = SaveData(null);
                var readback = DiplomacyPersistenceCodec.Deserialize(actual, _rules);
                CanonicalTradeStatePersistence.Validate(readback);
                if (!ExistingSaveReceiptGate.ExactBytes(preliminary, actual))
                    throw new InvalidDataException("Prepared and original SaveData envelope differ");
                var receipt = new OriginalMultiplayerOwnerReadbackReceipt(this, readback, stage.OriginalTick, stage.OriginalGeneration);
                _canonical.ConfirmOriginalMpConsentProjection(stage, ticket, collector, receipt);
                // Allocations/parsing completed BEFORE ACK; field assignments only after.
                _confirmedOuterSaveBytes = actual;
                _originalOuterSeed = readback;
                _lastProcessedDay = stage.OriginalDay;
                _lastObservedNativeDay = world.NativeGameDay;
            }
            catch
            {
                _canonicalSaved = oldBytes;
                _archive = oldArchive;
                _mailbox = oldMailbox;
                _mpQuarantined = true;
                throw;
            }
            finally
            {
                _originalOwnerProjectionStable = false;
                _internalReadbackOuter = null;
            }
        }

        private bool PrepareOriginalMpExternalSave(string destination, out SaveCall call)
        {
            call = null;
            var c = new SaveCall
            {
                Owner = this,
                OriginalPath = Path.GetFullPath(destination),
                ThreadId = Environment.CurrentManagedThreadId,
                Generation = _sessionGeneration,
                MpConsentOriginalSave = true
            };
            if (!Monitor.TryEnter(_gate))
                return false;
            c.StateGateHeld = true;
            try
            {
                // Reject pending/revoked owners before touching the native monitor; repeat
                // under both existing monitors before freezing the published payload.
                if (MustDelayExternalGameSave || _confirmedOuterSaveBytes == null || _activeSave != null)
                    return false;
                if (!TryEnterNativeInventorySection(out c.NativeLease))
                    return false;
                if (MustDelayExternalGameSave || _confirmedOuterSaveBytes == null || _activeSave != null)
                    return false;
                // Hold both existing monitors through the original engine's save.
                // This freezes the exact published owner against concurrent Chore work.
                c.MpConsentPayload = (byte[])_confirmedOuterSaveBytes.Clone();
                c.LegacyPayload = DiplomacySaveCodec.SerializeInactive("schema19_primary_owns_mp_consent");
                _activeSave = c;
                call = c;
                return true;
            }
            finally
            {
                if (call == null)
                    ReleaseGameSave(c);
            }
        }

        private bool TryReadExistingMpSave(SaveContext context, out byte[] payload)
        {
            payload = null;
            if (!_mpBootstrapRequested && !_mpConsentMode && _pendingOriginalMpLoadBytes == null)
                return false;
            if (context == null || !context.IsSaveFile)
                return true;
            if (MustDelayExternalGameSave)
                throw new InvalidOperationException("External save blocked by pending/invalid MP consent");
            if (_confirmedOuterSaveBytes == null)
                throw new InvalidDataException("No original MP owner readback available");
            payload = (byte[])_confirmedOuterSaveBytes.Clone();
            return true;
        }
    }
}
