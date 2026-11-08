// Complete trade history embedded in the checked diplomacy save envelope.
// The diplomacy and canonical codecs serialize this model. Original-game
// durability is verified separately by OriginalGameSaveBridge.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrongholdEuropeDE.Runtime
{
    internal enum TradeDisposition : byte
    {
        PendingCounter = 1,
        PreparingNative = 2,
        Committed = 3,
        Rejected = 4,
        Cancelled = 5,
        Quarantined = 6,
        Superseded = 7, // original quote superseded by issued counteroffer
    }

    internal sealed class TradeRelationship
    {
        internal byte A, B;
        internal int Goodwill = 50;
        internal int RejectionStreak;
        internal int LastRejectedDay = -100;
        internal int LastAttemptDay = -100;
        internal int LastPenaltyDay = -100;
        internal int RejectedToday;
        internal int AcceptedTrades;
        internal int LastDealDay = -100;
        internal TradeRelationship Copy()
        {
            return (TradeRelationship)MemberwiseClone();
        }
    }

    internal sealed class TradeRecord
    {
        internal ulong Id, RootId;
        internal byte Requester, Recipient;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount, IssuedDay, ExpiryDay;
        internal byte Generosity;
        internal TradeDisposition State;
        internal bool AiToAi;
        internal int ChanceBasisPoints, RollBasisPoints;
        internal TradeRecord Copy()
        {
            return (TradeRecord)MemberwiseClone();
        }

        internal void Validate()
        {
            // Every issued trade ID begins at the archive's reserved
            // high-water 1001. Older integers are NEVER authorized receipts.
            if (Id < TradeHistoryArchive.MinimumTradeId || (RootId != 0 && RootId < TradeHistoryArchive.MinimumTradeId) || Requester < 1 || Recipient < 1 || Requester > 8 || Recipient > 8 || Requester == Recipient || OfferedGood == RequestedGood || OfferedGood == 0 || RequestedGood == 0 || StrongholdEuropeDE.TradeModels.GoodCatalog.Find(OfferedGood) == null || StrongholdEuropeDE.TradeModels.GoodCatalog.Find(RequestedGood) == null || OfferedAmount <= 0 || RequestedAmount <= 0 || OfferedAmount > 100000 || RequestedAmount > 100000 || IssuedDay < 1 || ExpiryDay < IssuedDay || ExpiryDay > IssuedDay + 20 || Generosity > 2 || ChanceBasisPoints < 0 || ChanceBasisPoints > 10000 || RollBasisPoints < 0 || RollBasisPoints >= 10000 || (RootId != 0 && RootId >= Id) || (State == TradeDisposition.PendingCounter && RootId == 0) || !Enum.IsDefined(typeof(TradeDisposition), State))
                throw new InvalidDataException("Trade record has invalid canonical terms");
        }
    }

    internal sealed class TradeHistoryArchive
    {
        internal const int MaximumRelations = 28;
        internal const int MaximumRecords = 2048;
        internal const ulong MinimumTradeId = 1001UL;
        // authoritative ledger admission must refuse a trade whose
        // success cannot be serialized by the checked archive codec. The production
        // relationship effect uses the identical upper bound.
        internal const int MaximumSaveableAcceptedTrades = int.MaxValue - 1;
        internal static bool CanBookAnotherTrade(int acceptedTrades)
        {
            return acceptedTrades >= 0 && acceptedTrades < MaximumSaveableAcceptedTrades;
        }

        internal ulong NextId = MinimumTradeId;
        private static bool ValidRelationDay(int value, int currentDay)
        {
            // A missing event date is exactly -100.
            // Any other negative, day 0 or future day cannot be replayed.
            return value == -100 || value >= 1 && value <= currentDay;
        }

        internal int LastAutonomousDay = -1;
        internal string MapFingerprint;
        internal string RosterFingerprint;
        internal ulong PendingNativeId;
        internal readonly List<TradeRelationship> Relationships = new List<TradeRelationship>();
        internal readonly List<TradeRecord> Records = new List<TradeRecord>();
        internal static int PairKey(int x, int y)
        {
            if (x < 1 || x > 8 || y < 1 || y > 8 || x == y)
                throw new InvalidDataException("Trade pair must use two different living slots");
            return (Math.Min(x, y) << 8) | Math.Max(x, y);
        }

        internal TradeHistoryArchive Copy()
        {
            var copy = new TradeHistoryArchive
            {
                NextId = NextId,
                LastAutonomousDay = LastAutonomousDay,
                MapFingerprint = MapFingerprint,
                RosterFingerprint = RosterFingerprint,
                PendingNativeId = PendingNativeId,
            };
            copy.Relationships.AddRange(Relationships.Select(r => r.Copy()));
            copy.Records.AddRange(Records.Select(r => r.Copy()));
            return copy;
        }

        internal TradeRelationship Relation(byte a, byte b)
        {
            int pair = PairKey(a, b);
            TradeRelationship result = Relationships.FirstOrDefault(r => PairKey(r.A, r.B) == pair);
            if (result != null)
                return result;
            if (Relationships.Count >= MaximumRelations)
                throw new InvalidDataException("Trade relation ledger full");
            result = new TradeRelationship
            {
                A = (byte)Math.Min(a, b),
                B = (byte)Math.Max(a, b)
            };
            Relationships.Add(result);
            return result;
        }

        internal TradeRecord Record(ulong id)
        {
            return Records.FirstOrDefault(r => r.Id == id);
        }

        internal void Add(TradeRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");
            record.Validate();
            if (Records.Count >= MaximumRecords || Record(record.Id) != null)
                throw new InvalidOperationException("Duplicate/overflow trade id");
            Records.Add(record.Copy());
        }

        internal void Validate(string knownMap, string knownRoster, int currentDay)
        {
            if (string.IsNullOrEmpty(MapFingerprint) || MapFingerprint.Length > 256 || string.IsNullOrEmpty(RosterFingerprint) || RosterFingerprint.Length > 256 || (knownMap != null && !string.Equals(MapFingerprint, knownMap, StringComparison.Ordinal)) || (knownRoster != null && !string.Equals(RosterFingerprint, knownRoster, StringComparison.Ordinal)) || NextId < MinimumTradeId || NextId == ulong.MaxValue || LastAutonomousDay > currentDay || LastAutonomousDay < -1 || Relationships.Count > MaximumRelations || Records.Count > MaximumRecords)
                throw new InvalidDataException("Trade archive not bound to this session/map");
            var relationKeys = new HashSet<int>();
            foreach (TradeRelationship rel in Relationships)
            {
                if (rel == null || rel.Goodwill < 0 || rel.Goodwill > 100 || rel.RejectionStreak < 0 || rel.RejectionStreak > 5000 || rel.RejectedToday < 0 || rel.RejectedToday > 24 || rel.AcceptedTrades < 0 || rel.AcceptedTrades >= int.MaxValue || !ValidRelationDay(rel.LastRejectedDay, currentDay) || !ValidRelationDay(rel.LastAttemptDay, currentDay) || !ValidRelationDay(rel.LastPenaltyDay, currentDay) || !ValidRelationDay(rel.LastDealDay, currentDay) || !relationKeys.Add(PairKey(rel.A, rel.B)))
                    throw new InvalidDataException("Trade relation ledger corrupted");
            }

            var ids = new HashSet<ulong>();
            var counterRoots = new HashSet<ulong>();
            int prepares = 0;
            foreach (TradeRecord rec in Records)
            {
                if (rec == null)
                    throw new InvalidDataException("Null trade receipt");
                rec.Validate();
                if (rec.IssuedDay > currentDay || rec.Id >= NextId || !ids.Add(rec.Id))
                    throw new InvalidDataException("Replayed or future trade receipt");
                if (rec.State == TradeDisposition.PreparingNative)
                    prepares++;
                // A numeric RootId is insufficient: the actual original
                // terms must still exist in the checked trade archive.
                // An attacker cannot fabricate an orphan counter or attach
                // one to a different partner or pair of goods.
                if (rec.RootId != 0)
                {
                    TradeRecord root = Record(rec.RootId);
                    if (root == null || root.RootId != 0 || root.Requester != rec.Requester || root.Recipient != rec.Recipient || root.OfferedGood != rec.OfferedGood || root.RequestedGood != rec.RequestedGood || root.IssuedDay != rec.IssuedDay || root.State != TradeDisposition.Superseded || !counterRoots.Add(rec.RootId))
                        throw new InvalidDataException("Counteroffer root absent, settled already, or duplicated");
                }
            }

            if (prepares > 1 || (PendingNativeId == 0) != (prepares == 0) || PendingNativeId != 0 && (Record(PendingNativeId) == null || Record(PendingNativeId).State != TradeDisposition.PreparingNative))
                throw new InvalidDataException("Inconsistent pending native barter");
        }
    }

    internal static class TradeSaveCodec
    {
        internal static void Write(BinaryWriter w, TradeHistoryArchive archive)
        {
            w.Write(archive != null);
            if (archive == null)
                return;
            archive.Validate(null, null, int.MaxValue);
            w.Write(archive.MapFingerprint);
            w.Write(archive.RosterFingerprint);
            w.Write(archive.NextId);
            w.Write(archive.LastAutonomousDay);
            w.Write(archive.PendingNativeId);
            w.Write(archive.Relationships.Count);
            foreach (var rel in archive.Relationships.OrderBy(r => r.A).ThenBy(r => r.B))
            {
                w.Write(rel.A);
                w.Write(rel.B);
                w.Write(rel.Goodwill);
                w.Write(rel.RejectionStreak);
                w.Write(rel.LastRejectedDay);
                w.Write(rel.LastAttemptDay);
                w.Write(rel.LastPenaltyDay);
                w.Write(rel.RejectedToday);
                w.Write(rel.AcceptedTrades);
                w.Write(rel.LastDealDay);
            }

            w.Write(archive.Records.Count);
            foreach (var rec in archive.Records.OrderBy(r => r.Id))
            {
                rec.Validate();
                w.Write(rec.Id);
                w.Write(rec.RootId);
                w.Write(rec.Requester);
                w.Write(rec.Recipient);
                w.Write(rec.OfferedGood);
                w.Write(rec.RequestedGood);
                w.Write(rec.OfferedAmount);
                w.Write(rec.RequestedAmount);
                w.Write(rec.IssuedDay);
                w.Write(rec.ExpiryDay);
                w.Write(rec.Generosity);
                w.Write((byte)rec.State);
                w.Write(rec.AiToAi);
                w.Write(rec.ChanceBasisPoints);
                w.Write(rec.RollBasisPoints);
            }
        }

        internal static TradeHistoryArchive Read(BinaryReader r, int currentDay)
        {
            if (!r.ReadBoolean())
                return null;
            var a = new TradeHistoryArchive
            {
                MapFingerprint = r.ReadString(),
                RosterFingerprint = r.ReadString(),
                NextId = r.ReadUInt64(),
                LastAutonomousDay = r.ReadInt32(),
                PendingNativeId = r.ReadUInt64()
            };
            if (a.MapFingerprint.Length > 256 || a.RosterFingerprint.Length > 256)
                throw new InvalidDataException("Oversized trading scope");
            int count = r.ReadInt32();
            if (count < 0 || count > TradeHistoryArchive.MaximumRelations)
                throw new InvalidDataException("Invalid trading relation count");
            for (int i = 0; i < count; i++)
                a.Relationships.Add(new TradeRelationship { A = r.ReadByte(), B = r.ReadByte(), Goodwill = r.ReadInt32(), RejectionStreak = r.ReadInt32(), LastRejectedDay = r.ReadInt32(), LastAttemptDay = r.ReadInt32(), LastPenaltyDay = r.ReadInt32(), RejectedToday = r.ReadInt32(), AcceptedTrades = r.ReadInt32(), LastDealDay = r.ReadInt32() });
            count = r.ReadInt32();
            if (count < 0 || count > TradeHistoryArchive.MaximumRecords)
                throw new InvalidDataException("Invalid trading receipt count");
            for (int i = 0; i < count; i++)
                a.Records.Add(new TradeRecord { Id = r.ReadUInt64(), RootId = r.ReadUInt64(), Requester = r.ReadByte(), Recipient = r.ReadByte(), OfferedGood = r.ReadUInt16(), RequestedGood = r.ReadUInt16(), OfferedAmount = r.ReadInt32(), RequestedAmount = r.ReadInt32(), IssuedDay = r.ReadInt32(), ExpiryDay = r.ReadInt32(), Generosity = r.ReadByte(), State = (TradeDisposition)r.ReadByte(), AiToAi = r.ReadBoolean(), ChanceBasisPoints = r.ReadInt32(), RollBasisPoints = r.ReadInt32() });
            a.Validate(null, null, currentDay);
            return a;
        }
    }
}
