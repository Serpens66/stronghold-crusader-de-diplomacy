// Isolated  addition, no SaveData registration and no file writes.
// The original coordinator ALONE supplies a receipt created by its already
// existing SaveData owner while still holding the original engine monitor.
// DO NOT call this on queue success, a Steam ACK or plain in-memory Encode().
using System;
using System.IO;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal interface ICanonicalExistingSaveOwnerReceipt
    {
        bool OriginalEngineMonitorHeld { get; }

        bool OriginalSaveOwnerConfirmedReadback { get; }

        long OriginalMapGeneration { get; }

        long OriginalSharedSimulationTick { get; }

        string OriginalMapFingerprint { get; }

        string OriginalRosterFingerprint { get; }

        // Exact canonical subrecord bytes already embedded in the existing
        // Schema19 SaveData; a speculative pre-save buffer is not a receipt.
        byte[] ReadCanonicalSubrecordFromExistingOwner();
    }

    internal static class ExistingSaveReceiptGate
    {
        internal static bool ExactBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++)
                difference |= a[i] ^ b[i];
            return difference == 0;
        }

        // Doesn't start a save or create a second ledger. It only compares
        // an owner-issued readback to the candidate from the pure reducer.
        // All checks happen before the existing  collector removes
        // in-flight commands or permits the host to sign the next command.
        internal static void AcknowledgeExactExistingOwnerReadback(SignedTradeActionBatchCollector collector, SignedTradeTickBatch ticket, byte[] expectedCanonicalFromPureReduction, ICanonicalExistingSaveOwnerReceipt originalSaveReceipt)
        {
            if (collector == null || ticket == null || expectedCanonicalFromPureReduction == null || originalSaveReceipt == null || !collector.MustDelayOriginalSave || !originalSaveReceipt.OriginalEngineMonitorHeld || !originalSaveReceipt.OriginalSaveOwnerConfirmedReadback || originalSaveReceipt.OriginalMapGeneration < 1 || originalSaveReceipt.OriginalSharedSimulationTick < 1)
                throw new InvalidOperationException("No verified original Schema19 SaveData receipt");
            byte[] witnessed = originalSaveReceipt.ReadCanonicalSubrecordFromExistingOwner();
            if (!ExactBytes(witnessed, expectedCanonicalFromPureReduction))
                throw new InvalidDataException("Original save readback differs from canonical reducer output");
            CanonicalModel saved = CanonicalTradeCodec.Decode(witnessed);
            if (saved.LastSharedTick != originalSaveReceipt.OriginalSharedSimulationTick || !string.Equals(saved.Archive.MapFingerprint, originalSaveReceipt.OriginalMapFingerprint, StringComparison.Ordinal) || !string.Equals(saved.Archive.RosterFingerprint, originalSaveReceipt.OriginalRosterFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Original save tick or map/roster scope differs from reducer");
            // AcknowledgePublished itself checks all saved per-human
            // sequence and nonce highwaters and activation epoch; there is
            // deliberately no shadow replay store or invented native receipt.
            collector.AcknowledgePublished(ticket, saved);
        }
    }
}
