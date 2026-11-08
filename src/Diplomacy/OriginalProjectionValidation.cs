//  source-only: inspect actual legacy projections from the ONE
// original Schema19 owner, not a caller-supplied "validated" boolean alone.
// No independent SaveData hook, serialization owner, active  mailbox,
// allocator, native read/write, or Chore/Steam sender is installed.
using System;
using System.IO;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal interface IVerifiedExistingProjectionReceipt : IOriginalProjectionReceipt
    {
        int OriginalOuterSaveDay { get; }

        string OriginalOuterMapFingerprint { get; }

        string OriginalOuterRosterFingerprint { get; }

        // Both reads MUST come from the same existing owner under its
        // original stable SaveData lock; neither may be built from the
        // staged candidate. A production owner also checks this itself.
        TradeHistoryArchive ReadOriginalOwnerArchiveProjection();
        TradeIntentMailbox ReadOriginalOwnerMailboxProjection();
    }

    internal static class OriginalProjectionValidation
    {
        internal static byte[] SerializeProjection(TradeHistoryArchive archive, TradeIntentMailbox mailbox, int day, string map, string roster)
        {
            if (archive == null || mailbox == null || day < 1 || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(roster))
                throw new InvalidDataException("Absent original Schema19 trade projection");
            if (archive.MapFingerprint != map || archive.RosterFingerprint != roster || mailbox.MapFingerprint != map || mailbox.RosterFingerprint != roster)
                throw new InvalidDataException("Original SaveData projection scope mismatch");
            // Use EXACT original legacy codec pair, not a new serializer
            // or a structurally weaker comparison of selected IDs.
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                TradeSaveCodec.Write(writer, archive);
                TradeIntentMailboxCodec.Write(writer, mailbox, map, roster, day, archive);
                writer.Flush();
                return stream.ToArray();
            }
        }

        // Actual capacity-free consent stage: SAME original outer
        // Schema19 owner and EXACT legacy codecs, never an invented receipt.
        internal static void RequireExactProjection(PendingConsentPublication stage, IVerifiedExistingProjectionReceipt original, bool before)
        {
            if (stage == null || original == null || !original.OriginalStableSaveSectionHeld || !original.OriginalEngineMonitorHeld || !original.OriginalSaveOwnerConfirmedReadback || !original.ExistingSchema19AndLegacyProjectionValidated)
                throw new InvalidOperationException("No independently read original stable legacy projection");
            CanonicalModel canonical = CanonicalTradeCodec.Decode(before ? stage.BeforeBytes() : stage.CandidateBytes());
            if (canonical.Day != original.OriginalOuterSaveDay || canonical.Archive.MapFingerprint != original.OriginalOuterMapFingerprint || canonical.Archive.RosterFingerprint != original.OriginalOuterRosterFingerprint)
                throw new InvalidDataException("Real outer SaveData map/day/roster does not match");
            byte[] canonicalLegacy = SerializeProjection(canonical.Archive, canonical.Mailbox, canonical.Day, canonical.Archive.MapFingerprint, canonical.Archive.RosterFingerprint);
            byte[] actualLegacy = SerializeProjection(original.ReadOriginalOwnerArchiveProjection(), original.ReadOriginalOwnerMailboxProjection(), original.OriginalOuterSaveDay, original.OriginalOuterMapFingerprint, original.OriginalOuterRosterFingerprint);
            if (!ExistingSaveReceiptGate.ExactBytes(canonicalLegacy, actualLegacy))
                throw new InvalidDataException("Actual one-owner / projection differs from canonical");
        }

        // Transaction-intent preview ONLY. Must be called before ACK; no
        // single field in this helper is permission to commit native goods.
        internal static void RequireExactStagedProjection(PendingMpReduction stage, IVerifiedExistingProjectionReceipt existingOwner, bool expectingRollbackToBefore)
        {
            if (stage == null || existingOwner == null || !existingOwner.OriginalStableSaveSectionHeld || !existingOwner.OriginalEngineMonitorHeld || !existingOwner.OriginalSaveOwnerConfirmedReadback || !existingOwner.ExistingSchema19AndLegacyProjectionValidated)
                throw new InvalidOperationException("No one-original-owner stable projection authority");
            byte[] required = expectingRollbackToBefore ? stage.BeforeBytes() : stage.CandidateBytes();
            CanonicalModel model = CanonicalTradeCodec.Decode(required);
            if (model.Day != existingOwner.OriginalOuterSaveDay || model.Archive.MapFingerprint != existingOwner.OriginalOuterMapFingerprint || model.Archive.RosterFingerprint != existingOwner.OriginalOuterRosterFingerprint)
                throw new InvalidDataException("Staged canonical and outer original save header diverged");
            var observedArchive = existingOwner.ReadOriginalOwnerArchiveProjection();
            var observedMailbox = existingOwner.ReadOriginalOwnerMailboxProjection();
            byte[] expectedLegacy = SerializeProjection(model.Archive, model.Mailbox, model.Day, model.Archive.MapFingerprint, model.Archive.RosterFingerprint);
            byte[] actuallyOwned = SerializeProjection(observedArchive, observedMailbox, existingOwner.OriginalOuterSaveDay, existingOwner.OriginalOuterMapFingerprint, existingOwner.OriginalOuterRosterFingerprint);
            if (!ExistingSaveReceiptGate.ExactBytes(expectedLegacy, actuallyOwned))
                throw new InvalidDataException("Existing one-owner / legacy projection differs from canonical");
        }
    }
}
