// One optional subrecord in the existing save owner. Legacy fields are checked
// projections of the canonical model, never a second active trade ledger.
using System;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal static class CanonicalTradeStatePersistence
    {
        internal const int MaximumBytes = 512 * 1024;
        internal static void Write(BinaryWriter writer, LegacyDiplomacySave state)
        {
            byte[] bytes = state.CanonicalTradeState;
            Validate(state);
            writer.Write(bytes == null ? 0 : bytes.Length);
            if (bytes != null)
                writer.Write(bytes);
        }

        internal static void Read(BinaryReader reader, LegacyDiplomacySave state)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumBytes || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid canonical trade subrecord length");
            if (count == 0)
                return;
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException();
            state.CanonicalTradeState = bytes;
            Validate(state);
        }

        internal static void Validate(LegacyDiplomacySave state)
        {
            byte[] bytes = state.CanonicalTradeState;
            if (bytes == null)
                return;
            if (bytes.Length < 1 || bytes.Length > MaximumBytes)
                throw new InvalidDataException("Invalid canonical trade subrecord");
            CanonicalModel m = CanonicalTradeCodec.Decode(bytes);
            if (m.Day != state.LastProcessedDay || m.Archive.MapFingerprint != state.MapFingerprint || m.Archive.RosterFingerprint != state.RosterFingerprint || state.TradeArchive == null || state.TradeMailbox == null)
                throw new InvalidDataException("Canonical/outer save scope mismatch");
            if (!Projection(m.Archive, m.Mailbox, m.Day).SequenceEqual(Projection(state.TradeArchive, state.TradeMailbox, state.LastProcessedDay)))
                throw new InvalidDataException("Divergent canonical and legacy trade projections");
        }

        private static byte[] Projection(TradeHistoryArchive archive, TradeIntentMailbox mailbox, int day)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                TradeSaveCodec.Write(writer, archive);
                TradeIntentMailboxCodec.Write(writer, mailbox, archive.MapFingerprint, archive.RosterFingerprint, day, archive);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        internal static byte[] HoldSavedStateAtOriginalDay(byte[] state, int originalDay)
        {
            var m = CanonicalTradeCodec.Decode(state);
            if (originalDay < m.Day)
                throw new InvalidDataException("Original save day moved backwards");
            if (originalDay == m.Day)
                return (byte[])state.Clone();
            AdvanceDay(m, originalDay);
            ExpireHumanOffers(m);
            // The unverified native lane remains paused: no due settlements, AI
            // replies, input nonce changes, new quotes or invented inventory facts.
            m.Revision = checked(m.Revision + 1);
            return CanonicalTradeCodec.Encode(m);
        }

        internal void HoldAtOriginalDay(int originalDay)
        {
            if (_prepared != null)
                return;
            if (originalDay < _model.Day)
                throw new InvalidDataException("Original save day moved backwards");
            if (originalDay == _model.Day)
                return;
            // Keep the live mailbox's nonserialized native-tick cursor. Decoding a
            // save here on every frame would rebase it and starve all delayed replies.
            var next = _model.Copy();
            AdvanceDay(next, originalDay);
            ExpireHumanOffers(next);
            next.Revision = checked(next.Revision + 1);
            Publish(next);
        }

        // Rebellion observations share the historical envelope but are not trade
        // commands. Only the already-owned diplomacy lane can publish these facts.
        internal void ObserveOriginalRebellionHistory(TradeIntentMailbox observed)
        {
            if (observed == null || _prepared != null)
                return;
            if (observed.MapFingerprint != _model.Archive.MapFingerprint || observed.RosterFingerprint != _model.Archive.RosterFingerprint)
                throw new InvalidDataException("Wrong original rebellion scope");
            var next = _model.Copy();
            next.Mailbox.Rebellions.Clear();
            next.Mailbox.Rebellions.AddRange(observed.Rebellions.Select(x => x.Copy()));
            Publish(next);
        }
    }
}
