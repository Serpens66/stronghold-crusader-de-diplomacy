// isolated two-phase publication for the ONE Format3/Schema19
// CanonicalTradeReducer. No native port, no Steam, no Chore/Save hooks.
// The original engine monitor and existing SaveData owner remain EXTERNAL.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    // One immutable candidate. It is NOT a second canonical model owner:
    // all data is detached and discarded on completion or explicit revocation.
    internal sealed class PendingMpReduction
    {
        private readonly object _owner;
        private readonly CanonicalModel _candidateModel;
        private readonly byte[] _before, _candidate;
        private readonly ulong[] _steamBySlot;
        private readonly Bytes _privateHookScope;
        internal readonly AuthenticatedTradeTick OriginalVerifiedBatch;
        internal readonly long BaseRevision, Tick, MapGeneration, InventoryGeneration;
        internal readonly ulong HostSteamId, LocalSteamId;
        internal readonly byte OriginalLocalSlot;
        internal readonly string MapFingerprint, RosterFingerprint;
        internal readonly int Actions;
        internal PendingMpReduction(object owner, CanonicalModel candidateModel, byte[] before, byte[] candidate, AuthenticatedTradeTick verifiedBatch, IObservedOriginalConsentFrame frame)
        {
            if (owner == null || candidateModel == null || before == null || candidate == null || verifiedBatch == null || frame == null || frame.PinnedOriginalNetworkContext == null)
                throw new ArgumentNullException("staged original MP reduction");
            var original = frame.PinnedOriginalNetworkContext;
            _owner = owner;
            _candidateModel = candidateModel.Copy();
            _before = (byte[])before.Clone();
            _candidate = (byte[])candidate.Clone();
            _steamBySlot = (ulong[])frame.CurrentOriginalSteamBySlot.Clone();
            _privateHookScope = new Bytes(original.Scope.Copy());
            OriginalVerifiedBatch = verifiedBatch;
            BaseRevision = checked(candidateModel.Revision - 1);
            Tick = frame.OriginalSharedSimulationTick;
            MapGeneration = frame.OriginalMapGeneration;
            InventoryGeneration = original.Generation;
            HostSteamId = original.HostSteamId;
            LocalSteamId = original.LocalSteamId;
            OriginalLocalSlot = frame.CurrentOriginalLocalHumanSlot;
            MapFingerprint = candidateModel.Archive.MapFingerprint;
            RosterFingerprint = candidateModel.Archive.RosterFingerprint;
            Actions = verifiedBatch.Requests().Length;
        }

        internal byte[] BeforeBytes()
        {
            return (byte[])_before.Clone();
        }

        internal byte[] CandidateBytes()
        {
            return (byte[])_candidate.Clone();
        }

        internal CanonicalModel CandidateModel()
        {
            return _candidateModel.Copy();
        }

        internal bool OwnedBy(object owner)
        {
            return Object.ReferenceEquals(_owner, owner);
        }

        internal bool BeforeMatches(byte[] bytes)
        {
            return ExistingSaveReceiptGate.ExactBytes(_before, bytes);
        }

        internal bool CandidateMatches(byte[] bytes)
        {
            return ExistingSaveReceiptGate.ExactBytes(_candidate, bytes);
        }

        internal void RequireSameOriginalFrame(IObservedOriginalConsentFrame frame)
        {
            if (frame == null || !frame.HoldsOriginalEngineMonitor || !frame.VerifiedPostNativeOrderedChoreBoundary || frame.OriginalMapGeneration != MapGeneration || frame.OriginalSharedSimulationTick != Tick || frame.CurrentOriginalLocalHumanSlot != OriginalLocalSlot)
                throw new InvalidOperationException("Staged MP original monitor, tick or map changed");
            TrustedContext context = frame.PinnedOriginalNetworkContext;
            if (context == null || context.Generation != InventoryGeneration || context.HostSteamId != HostSteamId || context.LocalSteamId != LocalSteamId || !context.Scope.Same(_privateHookScope))
                throw new InvalidOperationException("Staged MP signed Steam session has changed");
            ulong[] actual = frame.CurrentOriginalSteamBySlot;
            if (actual == null || actual.Length != 9 || !_steamBySlot.SequenceEqual(actual))
                throw new InvalidDataException("Staged MP original Steam-controlled slots changed");
        }
    }

    // Coordinator-only witness for its existing ONE Schema19 owner.
    // Both the exact canonical subrecord and its / legacy projection
    // must already have passed CanonicalTradeStatePersistence.Validate.
    internal interface IOriginalProjectionReceipt : ICanonicalExistingSaveOwnerReceipt
    {
        bool OriginalStableSaveSectionHeld { get; }

        bool ExistingSchema19AndLegacyProjectionValidated { get; }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        // This is a transient pointer only, not a second mailbox/save ledger.
        // This guard additionally makes .AdvanceVerifiedMpWithoutNative
        // refuse a second direct MP tick while a candidate is staged.
        private PendingMpReduction _pendingWorldPublication;
        // Sticky for this controller lifetime. Once a verified MP session
        // is admitted, the old native AdvanceSharedTick may never run,
        // including BETWEEN completed stages (no one-tick unlock window).
        private bool _mpNoNativeSession;
        internal bool HasUnpublishedMpReduction
        {
            get
            {
                return _pendingWorldPublication != null;
            }
        }

        // Evaluates  on a DETACHED copy. The original _model, _save and
        // / projections retain their exact previous bytes until a real
        // existing SaveData-owner in-memory readback has been witnessed.
        internal PendingMpReduction PrepareVerifiedMpWithoutPublishing(AuthenticatedTradeTick orderedChoreBatch, IObservedOriginalConsentFrame originalFrame)
        {
            if (orderedChoreBatch == null || originalFrame == null)
                throw new ArgumentNullException("verified ordered MP input");
            if (_pendingWorldPublication != null || _prepared != null)
                throw new InvalidOperationException("Only one original controller transition may be unresolved");
            byte[] before = SaveBytes();
            var detached = new CanonicalTradeReducer(_model.Copy());
            byte[] candidate = detached.AdvanceVerifiedMpWithoutNative(orderedChoreBatch, originalFrame);
            // Decode validates the persisted Format3 wire, but NEVER adopt
            // decoded Mailbox as the active runtime model here: the native
            // simulation-tick cursor is intentionally nonserialized and
            // decoding it on each frame would starve due AI replies.
            CanonicalModel decoded = CanonicalTradeCodec.Decode(candidate);
            CanonicalModel after = detached._model.Copy();
            if (!ExistingSaveReceiptGate.ExactBytes(candidate, CanonicalTradeCodec.Encode(after)) || decoded.Revision != after.Revision || !ExistingSaveReceiptGate.ExactBytes(candidate, detached.SaveBytes()) || !ExistingSaveReceiptGate.ExactBytes(before, SaveBytes()) || after.Revision != checked(_model.Revision + 1) || after.MatchScope != _model.MatchScope || after.MatchTerm != _model.MatchTerm || after.LastSharedTick != originalFrame.OriginalSharedSimulationTick || after.Quarantined || after.Archive.PendingNativeId != 0 || after.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative) || after.Mailbox.Intents.Any(x => x.State == TradeIntentStatus.Processing))
                throw new InvalidDataException("Non-native staged MP candidate differs from original owner");
            var stage = new PendingMpReduction(_identity, after, before, candidate, orderedChoreBatch, originalFrame);
            _pendingWorldPublication = stage;
            _mpNoNativeSession = true; // Never fall back to legacy native MP path.
            return stage;
        }

        // The EXCLUSIVE coordinator, under its existing original engine and
        // feature save locks, must first project stage.CandidateBytes() into
        // the ONE Schema19 SaveData owner, fully validate the existing legacy
        // projection, then supply a readback generated by that SAME owner.
        // No physical disk-save or native goods authority is claimed.
        internal void ConfirmVerifiedMpExistingOwnerPublication(PendingMpReduction stage, IObservedOriginalConsentFrame frame, SignedTradeActionBatchCollector collector, SignedTradeTickBatch ticket, IVerifiedExistingProjectionReceipt originalOwnerReadback)
        {
            if (stage == null || _pendingWorldPublication != stage || !stage.OwnedBy(_identity) || frame == null || collector == null || ticket == null || !Object.ReferenceEquals(ticket.Input, stage.OriginalVerifiedBatch) || stage.Actions != ticket.ActionCount || !stage.BeforeMatches(SaveBytes()) || _model.Revision != stage.BaseRevision)
                throw new InvalidOperationException("Wrong staged owner, batch, revision or save-before");
            stage.RequireSameOriginalFrame(frame);
            if (originalOwnerReadback == null || !originalOwnerReadback.OriginalEngineMonitorHeld || !originalOwnerReadback.OriginalStableSaveSectionHeld || !originalOwnerReadback.ExistingSchema19AndLegacyProjectionValidated || !originalOwnerReadback.OriginalSaveOwnerConfirmedReadback || originalOwnerReadback.OriginalMapGeneration != stage.MapGeneration || originalOwnerReadback.OriginalSharedSimulationTick != stage.Tick || !String.Equals(originalOwnerReadback.OriginalMapFingerprint, stage.MapFingerprint, StringComparison.Ordinal) || !String.Equals(originalOwnerReadback.OriginalRosterFingerprint, stage.RosterFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Original single SaveData owner not attested for staged MP frame");
            // Independently compare the actual one-owner / codec bytes;
            // a boolean 'legacy valid' claim is never sufficient by itself.
            OriginalProjectionValidation.RequireExactStagedProjection(stage, originalOwnerReadback, false);
            byte[] readback = originalOwnerReadback.ReadCanonicalSubrecordFromExistingOwner();
            if (!stage.CandidateMatches(readback))
                throw new InvalidDataException("One existing Schema19 owner readback differs from staged bytes");
            CanonicalModel model = CanonicalTradeCodec.Decode(readback);
            if (model.Revision != stage.BaseRevision + 1 || model.LastSharedTick != stage.Tick)
                throw new InvalidDataException("Canonical original SaveData revision/tick mismatch");
            // Allocate the ENTIRE final runtime state and final byte[] BEFORE
            // acknowledging the  collector. After ACK there must be no
            // cloning, serialization, parser, I/O or other throwing operation
            // between clearing the signed queue and publishing _model/_save.
            CanonicalModel toPublish = stage.CandidateModel();
            byte[] toPublishBytes = stage.CandidateBytes();
            if (!ExistingSaveReceiptGate.ExactBytes(toPublishBytes, readback))
                throw new InvalidDataException("Final staged bytes changed");
            // AcknowledgePublished first validates ALL actor high-waters.
            // The coordinator holds its one original monitor throughout.
            collector.AcknowledgePublished(ticket, model);
            _model = toPublish;
            _save = toPublishBytes;
            _pendingWorldPublication = null;
        }

        // Only for an explicitly revoked / session AFTER the original
        // SaveData owner demonstrates it has rolled back to the exact BEFORE
        // projection. An ambiguous readback retains the staged pointer and
        // must stop the session; never replay a possibly published Chore.
        internal void AbandonUnpublishedMpAfterExactRollback(PendingMpReduction stage, IObservedOriginalConsentFrame frame, SignedTradeActionBatchCollector collector, IVerifiedExistingProjectionReceipt originalOwnerReadback)
        {
            if (stage == null || _pendingWorldPublication != stage || !stage.OwnedBy(_identity) || collector == null || frame == null || !stage.BeforeMatches(SaveBytes()) || originalOwnerReadback == null || !originalOwnerReadback.OriginalEngineMonitorHeld || !originalOwnerReadback.OriginalStableSaveSectionHeld || !originalOwnerReadback.ExistingSchema19AndLegacyProjectionValidated || !originalOwnerReadback.OriginalSaveOwnerConfirmedReadback || originalOwnerReadback.OriginalMapGeneration != stage.MapGeneration || originalOwnerReadback.OriginalSharedSimulationTick != stage.Tick || originalOwnerReadback.OriginalMapFingerprint != stage.MapFingerprint || originalOwnerReadback.OriginalRosterFingerprint != stage.RosterFingerprint)
                throw new InvalidOperationException("Unverified original SaveData rollback cannot release MP stage");
            stage.RequireSameOriginalFrame(frame);
            OriginalProjectionValidation.RequireExactStagedProjection(stage, originalOwnerReadback, true);
            if (!stage.BeforeMatches(originalOwnerReadback.ReadCanonicalSubrecordFromExistingOwner()))
                throw new InvalidDataException("Original one-owner rollback has no exact prior bytes");
            collector.Invalidate(); // Coordinator ALSO revokes original  session.
            _pendingWorldPublication = null;
        }
    }
}
