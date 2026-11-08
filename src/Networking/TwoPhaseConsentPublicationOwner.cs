// One two-phase publication inside original ONE  Format3 controller.
// Candidate is detached; no active _model/_save/actor highwater until the
// EXISTING Schema19 SaveData owner has been projected and read back.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class PendingConsentPublication
    {
        private readonly object owner;
        private readonly CanonicalModel model;
        private readonly byte[] before, after;
        internal readonly SignedTradeTickBatch Ticket;
        internal readonly long OriginalTick, OriginalGeneration, BaseRevision;
        internal readonly int OriginalDay;
        internal readonly Bytes SignedSession;
        internal readonly ulong HostSteam, InputEpoch;
        internal readonly string Map, Roster;
        internal PendingConsentPublication(object ownerId, CanonicalModel result, byte[] existing, byte[] next, SignedTradeTickBatch ticket, ObservedOriginalConsentFrame frame)
        {
            if (ownerId == null || result == null || existing == null || next == null || ticket == null || frame == null)
                throw new ArgumentNullException("Original single-owner staged MP");
            owner = ownerId;
            model = result.Copy();
            before = (byte[])existing.Clone();
            after = (byte[])next.Clone();
            Ticket = ticket;
            OriginalTick = frame.Tick;
            OriginalGeneration = frame.NativeGeneration;
            OriginalDay = frame.Day;
            Map = model.Archive.MapFingerprint;
            Roster = model.Archive.RosterFingerprint;
            InputEpoch = model.InputEpoch;
            SignedSession = new Bytes(frame.SessionId.Copy());
            HostSteam = frame.HostSteam;
            BaseRevision = result.Revision - 1;
        }

        internal bool Owned(object check)
        {
            return Object.ReferenceEquals(owner, check);
        }

        internal byte[] BeforeBytes()
        {
            return (byte[])before.Clone();
        }

        internal byte[] CandidateBytes()
        {
            return (byte[])after.Clone();
        }

        internal CanonicalModel CandidateRuntime()
        {
            return model.Copy();
        }

        internal bool SameBefore(byte[] b)
        {
            return ExistingSaveReceiptGate.ExactBytes(before, b);
        }

        internal bool SameAfter(byte[] b)
        {
            return ExistingSaveReceiptGate.ExactBytes(after, b);
        }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        internal bool HasPendingMpConsentOnly
        {
            get
            {
                return _pendingConsentPublication != null;
            }
        }

        internal PendingConsentPublication PrepareOriginalMpConsentOnly(SignedTradeTickBatch ticket, ObservedOriginalConsentFrame captured)
        {
            if (ticket == null || captured == null || _pendingConsentPublication != null || _prepared != null || _pendingWorldPublication != null || ticket.Input == null || ticket.Session == null || !ticket.Session.Same(captured.SessionId))
                throw new InvalidOperationException("Only one actual original MP tick may stage");
            var before = SaveBytes();
            var isolated = new CanonicalTradeReducer(_model.Copy());
            byte[] bytes = isolated.ReduceVerifiedOriginalConsentOnly(ticket.Input, captured.Original);
            CanonicalModel decoded = CanonicalTradeCodec.Decode(bytes);
            CanonicalModel runtime = isolated._model.Copy();
            if (!ExistingSaveReceiptGate.ExactBytes(bytes, CanonicalTradeCodec.Encode(runtime)) || !ExistingSaveReceiptGate.ExactBytes(before, SaveBytes()) || runtime.Revision != checked(_model.Revision + 1) || runtime.InputEpoch != decoded.InputEpoch || runtime.LastSharedTick != captured.Tick || runtime.Archive.PendingNativeId != 0 || runtime.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative))
                throw new InvalidDataException("Detached consent model or journal changed");
            var stage = new PendingConsentPublication(_identity, runtime, before, bytes, ticket, captured);
            _pendingConsentPublication = stage;
            _mpNoNativeSession = true;
            return stage;
        }

        internal void ConfirmOriginalMpConsentProjection(PendingConsentPublication stage, SignedTradeTickBatch ticket, SignedTradeActionBatchCollector collector, IVerifiedExistingProjectionReceipt originalOwner)
        {
            if (stage == null || !Object.ReferenceEquals(stage, _pendingConsentPublication) || !stage.Owned(_identity) || ticket == null || collector == null || !Object.ReferenceEquals(ticket, stage.Ticket) || !stage.SameBefore(SaveBytes()) || _model.Revision != stage.BaseRevision || originalOwner == null || !collector.MustDelayOriginalSave || !originalOwner.OriginalEngineMonitorHeld || !originalOwner.OriginalStableSaveSectionHeld || !originalOwner.OriginalSaveOwnerConfirmedReadback || !originalOwner.ExistingSchema19AndLegacyProjectionValidated || originalOwner.OriginalSharedSimulationTick != stage.OriginalTick || originalOwner.OriginalMapGeneration != stage.OriginalGeneration || originalOwner.OriginalOuterSaveDay != stage.OriginalDay || originalOwner.OriginalMapFingerprint != stage.Map || originalOwner.OriginalRosterFingerprint != stage.Roster)
                throw new InvalidOperationException("No original locked Schema19 MP consent projection");
            // Compare ACTUAL serialized outer + model against staged
            // canonical bytes; a caller's Boolean can never replace this check.
            OriginalProjectionValidation.RequireExactProjection(stage, originalOwner, false);
            byte[] observed = originalOwner.ReadCanonicalSubrecordFromExistingOwner();
            if (!stage.SameAfter(observed))
                throw new InvalidDataException("Real SaveData readback differs");
            CanonicalModel saved = CanonicalTradeCodec.Decode(observed);
            if (saved.LastSharedTick != stage.OriginalTick || saved.InputEpoch != stage.InputEpoch || saved.Revision != stage.BaseRevision + 1)
                throw new InvalidDataException("Saved original Chore highwater changed");
            // All potentially allocating work BEFORE the signed collector ACK.
            var publish = stage.CandidateRuntime();
            byte[] publishBytes = stage.CandidateBytes();
            if (!stage.SameAfter(publishBytes))
                throw new InvalidDataException("Prebuilt staged model differs");
            collector.AcknowledgePublished(ticket, saved);
            _model = publish;
            _save = publishBytes;
            _pendingConsentPublication = null;
        }

        // Only after SAME existing SaveData owner proves exact old BEFORE bytes,
        // old real / projection and original  session has been revoked.
        internal void RevokeAfterProvenOriginalMpRollback(PendingConsentPublication stage, SignedTradeActionBatchCollector collector, IVerifiedExistingProjectionReceipt owner)
        {
            if (stage == null || _pendingConsentPublication != stage || !stage.Owned(_identity) || collector == null || owner == null || !owner.OriginalEngineMonitorHeld || !owner.OriginalStableSaveSectionHeld || !owner.OriginalSaveOwnerConfirmedReadback || !owner.ExistingSchema19AndLegacyProjectionValidated || owner.OriginalSharedSimulationTick != stage.OriginalTick || owner.OriginalMapGeneration != stage.OriginalGeneration || !stage.SameBefore(SaveBytes()))
                throw new InvalidOperationException("Ambiguous MP SaveData rollback must remain quarantined");
            OriginalProjectionValidation.RequireExactProjection(stage, owner, true);
            if (!stage.SameBefore(owner.ReadCanonicalSubrecordFromExistingOwner()))
                throw new InvalidDataException("Original owner has not rolled back");
            collector.Invalidate();
            _pendingConsentPublication = null;
        // _mpNoNativeSession remains sticky even after rollback.
        }
    }
}
