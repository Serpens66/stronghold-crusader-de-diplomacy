using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    // Append-only extension AFTER the existing schema21 three-UInt64
    // Mod-gift binding suffix. Earlier save byte layouts are untouched.
    // These are bindings on existing pending package lines, not a ledger.
    internal static class RecordedGoodsResponseSuffix
    {
        internal const int MaximumPendingLines = 21;

        internal static void Write(BinaryWriter writer, LegacyDiplomacySave state)
        {
            if (writer == null || state == null)
                throw new InvalidDataException("Missing response-suffix writer scope");
            DiplomacyPendingPackage package = state.PendingPackage;
            int count = package == null ? 0 : package.Lines.Count;
            if (count < 0 || count > MaximumPendingLines)
                throw new InvalidDataException("Invalid response-package line count");
            writer.Write((byte)count);
            if (package == null)
                return;
            foreach (DiplomacyPendingTransfer line in package.Lines)
            {
                if (line == null)
                    throw new InvalidDataException("Null response-package line");
                writer.Write(line.BoundRequestId);
                writer.Write(line.ModGiftReceiptId);
            }
        }

        internal static void Read(BinaryReader reader, LegacyDiplomacySave state)
        {
            if (reader == null || state == null)
                throw new InvalidDataException("Missing response-suffix reader scope");
            int count = reader.ReadByte();
            DiplomacyPendingPackage package = state.PendingPackage;
            int expected = package == null ? 0 : package.Lines.Count;
            if (count != expected || count > MaximumPendingLines)
                throw new InvalidDataException("Response suffix does not match its package");
            if (package == null)
                return;
            foreach (DiplomacyPendingTransfer line in package.Lines)
            {
                if (line == null)
                    throw new InvalidDataException("Null saved response-package line");
                line.BoundRequestId = reader.ReadUInt64();
                line.ModGiftReceiptId = reader.ReadUInt64();
            }
        }
    }
}
