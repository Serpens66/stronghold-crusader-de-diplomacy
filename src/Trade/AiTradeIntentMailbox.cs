// bounded, deterministic simulation-time mailbox. No native or network authority.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal enum TradeIntentAction : byte
    {
        Propose = 0,
        AcceptCounter = 1,
        DeclineCounter = 2,
        EvaluateAiCounter = 3
    }

    internal enum TradeIntentStatus : byte
    {
        Waiting = 0,
        Processing = 1,
        Finished = 2,
        Cancelled = 3
    }

    internal sealed class TradeIntent
    {
        internal ulong Id, CounterId, CounterRootId;
        internal byte Requester, Recipient, Generosity;
        internal ushort OfferedGood, RequestedGood;
        internal int RequestedAmount, IssuedDay, CounterOfferedAmount, CounterCreatedDay, CounterExpiresDay;
        internal long EnqueuedClock, DueClock, ExpiryClock;
        internal bool AiToAi;
        internal TradeIntentAction Action;
        internal TradeIntentStatus State;
        internal string Outcome = string.Empty;
        internal TradeIntent Copy()
        {
            return (TradeIntent)MemberwiseClone();
        }

        internal bool MatchesCounter(TradeRecord counter)
        {
            return counter != null && Action != TradeIntentAction.Propose && counter.Id == CounterId && counter.RootId == CounterRootId && counter.Requester == Requester && counter.Recipient == Recipient && counter.OfferedGood == OfferedGood && counter.RequestedGood == RequestedGood && counter.OfferedAmount == CounterOfferedAmount && counter.RequestedAmount == RequestedAmount && counter.IssuedDay == CounterCreatedDay && counter.ExpiryDay == CounterExpiresDay && counter.Generosity == Generosity && counter.AiToAi == AiToAi;
        }

        internal void Validate(long clock, int day, ulong highWater)
        {
            if (Id < TradeHistoryArchive.MinimumTradeId || Id > highWater || Id >= ulong.MaxValue - 4 || Requester < 1 || Requester > 8 || Recipient < 1 || Recipient > 8 || Requester == Recipient || Generosity > 2 || RequestedAmount < 1 || RequestedAmount > 100000 || IssuedDay < 1 || IssuedDay > day || OfferedGood != 0 && GoodCatalog.Find(OfferedGood) == null || RequestedGood != 0 && GoodCatalog.Find(RequestedGood) == null || OfferedGood != 0 && OfferedGood == RequestedGood || EnqueuedClock < 0 || EnqueuedClock > clock || DueClock <= EnqueuedClock || DueClock - EnqueuedClock > TradeIntentMailbox.MaximumDelayTicks || ExpiryClock < DueClock || ExpiryClock - EnqueuedClock > TradeIntentMailbox.MaximumLifetimeTicks || !Enum.IsDefined(typeof(TradeIntentAction), Action) || !Enum.IsDefined(typeof(TradeIntentStatus), State) || (Action == TradeIntentAction.Propose ? CounterId != 0 || CounterRootId != 0 || CounterOfferedAmount != 0 || CounterCreatedDay != 0 || CounterExpiresDay != 0 : CounterId < TradeHistoryArchive.MinimumTradeId || CounterId >= Id || CounterRootId < TradeHistoryArchive.MinimumTradeId || CounterRootId >= CounterId || CounterOfferedAmount < 1 || CounterOfferedAmount > 100000 || OfferedGood == 0 || RequestedGood == 0 || CounterCreatedDay < 1 || CounterCreatedDay > IssuedDay || CounterExpiresDay < IssuedDay || (long)CounterExpiresDay - CounterCreatedDay > 20) || Action == TradeIntentAction.EvaluateAiCounter && !AiToAi || Outcome == null || Outcome.Length > 240)
                throw new InvalidDataException("Invalid delayed trade intent");
        }
    }

    internal sealed class TradeRebellionMemory
    {
        internal byte Vassal, Overlord;
        internal int SinceDay, LastDay = -1, StrengthDays, PoorDays, LastRebellionDay = -100;
        internal bool AtRisk;
        internal TradeRebellionMemory Copy()
        {
            return (TradeRebellionMemory)MemberwiseClone();
        }

        internal void Validate(int day)
        {
            if (Vassal < 1 || Vassal > 8 || Overlord < 1 || Overlord > 8 || Vassal == Overlord || SinceDay < 0 || SinceDay > day || LastDay < 0 || LastDay > day || StrengthDays < 0 || StrengthDays > 100000 || PoorDays < 0 || PoorDays > 100000 || LastRebellionDay != -100 && (LastRebellionDay < 0 || LastRebellionDay > day))
                throw new InvalidDataException("Invalid rebellion observation");
        }
    }

    internal sealed class TradeIntentMailbox
    {
        internal const int MaximumIntents = 96, MaximumPending = 28, MaximumDelayTicks = 240, MaximumLifetimeTicks = 2400;
        // Eight slots allow 8*7 = 56 directional vassal/overlord identities.
        // Saving only 28 historical directions could permanently lock a
        // later legitimate regime change without any native trade fault.
        internal const int MaximumRebellionHistories = 56;
        internal string MapFingerprint, RosterFingerprint;
        internal long Clock;
        internal ulong LastIssuedId;
        internal readonly List<TradeIntent> Intents = new List<TradeIntent>();
        internal readonly List<TradeRebellionMemory> Rebellions = new List<TradeRebellionMemory>();
        // Schema17 host-origin replay high-water; no native goods authority.
        internal TradeSteamReplay SteamReplay = new TradeSteamReplay();
        // This cursor is deliberately not serialized. Reload rebases to the
        // restored engine clock; elapsed wall time never consumes a response.
        private long _lastNativeTick = -1;
        internal TradeIntentMailbox Copy()
        {
            var c = new TradeIntentMailbox
            {
                MapFingerprint = MapFingerprint,
                RosterFingerprint = RosterFingerprint,
                Clock = Clock,
                LastIssuedId = LastIssuedId,
                _lastNativeTick = _lastNativeTick
            };
            c.Intents.AddRange(Intents.Select(x => x.Copy()));
            c.Rebellions.AddRange(Rebellions.Select(x => x.Copy()));
            c.SteamReplay = SteamReplay == null ? null : SteamReplay.Copy();
            return c;
        }

        internal void Rebase(long nativeTick)
        {
            _lastNativeTick = Math.Max(0, nativeTick);
        }

        internal void Observe(long nativeTick)
        {
            if (nativeTick < 0)
                throw new InvalidOperationException("Invalid simulation clock");
            if (_lastNativeTick < 0 || nativeTick < _lastNativeTick)
            {
                _lastNativeTick = nativeTick;
                return;
            }

            long elapsed = nativeTick - _lastNativeTick;
            Clock = checked(Clock + elapsed);
            _lastNativeTick = nativeTick;
        }

        internal static int ResponseDelay(ulong id, byte a, byte b)
        {
            // Stable arithmetic, independent of frame rate, process RNG or locale.
            ulong mixed = unchecked(id * 11400714819323198485UL + ((ulong)a << 16) + b * 131UL);
            return 48 + (int)(mixed % 73UL);
        }

        internal bool HasPending(int a, int b)
        {
            return Intents.Any(x => (x.State == TradeIntentStatus.Waiting || x.State == TradeIntentStatus.Processing) && (x.Requester == a && x.Recipient == b || x.Requester == b && x.Recipient == a));
        }

        internal void Enqueue(TradeIntent incoming, int day)
        {
            if (incoming == null)
                throw new ArgumentNullException("incoming");
            var x = incoming.Copy();
            if (x.Id <= LastIssuedId || x.State != TradeIntentStatus.Waiting || x.EnqueuedClock != Clock || HasPending(x.Requester, x.Recipient) || Intents.Count(z => z.State == TradeIntentStatus.Waiting || z.State == TradeIntentStatus.Processing) >= MaximumPending)
                throw new InvalidOperationException("Duplicate, stale or busy trade request");
            x.Validate(Clock, day, x.Id);
            // Terminal history is bounded; the monotonic high-water survives pruning.
            if (Intents.Count >= MaximumIntents)
            {
                var remove = Intents.Where(z => z.State == TradeIntentStatus.Finished || z.State == TradeIntentStatus.Cancelled).OrderBy(z => z.Id).FirstOrDefault();
                if (remove == null)
                    throw new InvalidOperationException("Trade mailbox full");
                Intents.Remove(remove);
            }

            LastIssuedId = x.Id;
            Intents.Add(x);
        }

        internal TradeIntent[] Due()
        {
            return Intents.Where(x => x.State == TradeIntentStatus.Waiting && x.DueClock <= Clock).OrderBy(x => x.DueClock).ThenBy(x => x.Id).ToArray();
        }

        internal void Validate(string map, string roster, int day, TradeHistoryArchive archive)
        {
            if (string.IsNullOrEmpty(MapFingerprint) || MapFingerprint.Length > 256 || string.IsNullOrEmpty(RosterFingerprint) || RosterFingerprint.Length > 256 || map != null && MapFingerprint != map || roster != null && RosterFingerprint != roster || Clock < 0 || Clock > long.MaxValue - MaximumLifetimeTicks || LastIssuedId >= ulong.MaxValue - 4 || LastIssuedId != 0 && LastIssuedId < TradeHistoryArchive.MinimumTradeId || Intents.Count > MaximumIntents || Rebellions.Count > MaximumRebellionHistories)
                throw new InvalidDataException("Trade mailbox scope or bounds invalid");
            if (SteamReplay == null)
                throw new InvalidDataException("Missing authenticated transport replay ledger");
            SteamReplay.Validate();
            var ids = new HashSet<ulong>();
            var pendingPairs = new HashSet<int>();
            foreach (var x in Intents)
            {
                if (x == null)
                    throw new InvalidDataException("Null trade intent");
                x.Validate(Clock, Math.Max(1, day), LastIssuedId);
                if (!ids.Add(x.Id) || archive == null || x.Id + 4 > archive.NextId || x.State == TradeIntentStatus.Waiting && archive.Record(x.Id) != null)
                    throw new InvalidDataException("Unreserved or duplicate delayed trade identity");
                if (x.State == TradeIntentStatus.Waiting || x.State == TradeIntentStatus.Processing)
                {
                    if (!pendingPairs.Add(TradeHistoryArchive.PairKey(x.Requester, x.Recipient)))
                        throw new InvalidDataException("Multiple active requests for one pair");
                    if (x.Action != TradeIntentAction.Propose && x.State == TradeIntentStatus.Waiting)
                    {
                        var counter = archive.Record(x.CounterId);
                        if (!x.MatchesCounter(counter) || counter.State != TradeDisposition.PendingCounter)
                            throw new InvalidDataException("Delayed counter scope mismatch");
                    }
                }
            }

            if (pendingPairs.Count > MaximumPending || LastIssuedId != 0 && (archive == null || LastIssuedId + 4 > archive.NextId))
                throw new InvalidDataException("Trade mailbox high-water invalid");
            var parties = new HashSet<int>();
            foreach (var x in Rebellions)
            {
                if (x == null)
                    throw new InvalidDataException("Null rebellion observation");
                x.Validate(day);
                if (!parties.Add((x.Vassal << 8) | x.Overlord))
                    throw new InvalidDataException("Duplicate rebellion observation");
            }
        }

        internal void RestoreAfterLoad(long nativeTick)
        {
            Rebase(nativeTick);
            foreach (var x in Intents.Where(z => z.State == TradeIntentStatus.Processing))
            {
                // Interrupted processing belongs to the existing native journal.
                // A mailbox may never replay a possibly applied transaction.
                x.State = TradeIntentStatus.Cancelled;
                x.Outcome = "interrupted_processing_quarantined";
            }
        }
    }

    internal static class TradeIntentMailboxCodec
    {
        internal static void Write(BinaryWriter w, TradeIntentMailbox m, string map, string roster, int day, TradeHistoryArchive archive)
        {
            w.Write(m != null);
            if (m == null)
                return;
            m.Validate(map, roster, day, archive);
            w.Write(m.MapFingerprint);
            w.Write(m.RosterFingerprint);
            w.Write(m.Clock);
            w.Write(m.LastIssuedId);
            w.Write(m.Intents.Count);
            foreach (var x in m.Intents.OrderBy(z => z.Id))
            {
                w.Write(x.Id);
                w.Write(x.CounterId);
                w.Write(x.Requester);
                w.Write(x.Recipient);
                w.Write(x.Generosity);
                w.Write(x.CounterRootId);
                w.Write(x.CounterOfferedAmount);
                w.Write(x.CounterCreatedDay);
                w.Write(x.CounterExpiresDay);
                w.Write(x.OfferedGood);
                w.Write(x.RequestedGood);
                w.Write(x.RequestedAmount);
                w.Write(x.IssuedDay);
                w.Write(x.EnqueuedClock);
                w.Write(x.DueClock);
                w.Write(x.ExpiryClock);
                w.Write(x.AiToAi);
                w.Write((byte)x.Action);
                w.Write((byte)x.State);
                w.Write(x.Outcome);
            }

            w.Write(m.Rebellions.Count);
            foreach (var x in m.Rebellions.OrderBy(z => z.Vassal).ThenBy(z => z.Overlord))
            {
                w.Write(x.Vassal);
                w.Write(x.Overlord);
                w.Write(x.SinceDay);
                w.Write(x.LastDay);
                w.Write(x.StrengthDays);
                w.Write(x.PoorDays);
                w.Write(x.LastRebellionDay);
                w.Write(x.AtRisk);
            }

            w.Write((byte)0xB6);
            w.Write(m.SteamReplay.HostTerm);
            for (int sender = 1; sender <= 8; sender++)
                w.Write(m.SteamReplay.LastSeenBySender[sender]);
        }

        private static string ReadBounded(BinaryReader r, int maximum)
        {
            // BinaryReader.ReadString allocates from attacker-controlled length.
            // Decode its 7-bit byte prefix with a strict bound before allocation.
            int bytes = 0, shift = 0;
            for (int i = 0; i < 5; i++)
            {
                byte b = r.ReadByte();
                if (i == 4 && (b & 0xf8) != 0)
                    throw new InvalidDataException("Text length overflow");
                bytes |= (b & 127) << shift;
                if ((b & 128) == 0)
                {
                    if (bytes < 0 || bytes > maximum * 4)
                        throw new InvalidDataException("Text exceeds mailbox bound");
                    byte[] raw = r.ReadBytes(bytes);
                    if (raw.Length != bytes)
                        throw new EndOfStreamException();
                    string text = new System.Text.UTF8Encoding(false, true).GetString(raw);
                    if (text.Length > maximum)
                        throw new InvalidDataException("Text exceeds character bound");
                    return text;
                }

                shift += 7;
            }

            throw new InvalidDataException("Invalid text prefix");
        }

        internal static TradeIntentMailbox Read(BinaryReader r, string map, string roster, int day, TradeHistoryArchive archive)
        {
            return ReadExtended(r, map, roster, day, archive, true);
        }

        // One unique legacy Read name; migration owns the explicit extended reader.
        internal static TradeIntentMailbox ReadExtended(BinaryReader r, string map, string roster, int day, TradeHistoryArchive archive, bool hasSteamReplay)
        {
            if (!r.ReadBoolean())
                return null;
            var m = new TradeIntentMailbox
            {
                MapFingerprint = ReadBounded(r, 256),
                RosterFingerprint = ReadBounded(r, 256),
                Clock = r.ReadInt64(),
                LastIssuedId = r.ReadUInt64()
            };
            int n = r.ReadInt32();
            if (n < 0 || n > TradeIntentMailbox.MaximumIntents)
                throw new InvalidDataException("Mailbox count invalid");
            for (int i = 0; i < n; i++)
                m.Intents.Add(new TradeIntent { Id = r.ReadUInt64(), CounterId = r.ReadUInt64(), Requester = r.ReadByte(), Recipient = r.ReadByte(), Generosity = r.ReadByte(), CounterRootId = r.ReadUInt64(), CounterOfferedAmount = r.ReadInt32(), CounterCreatedDay = r.ReadInt32(), CounterExpiresDay = r.ReadInt32(), OfferedGood = r.ReadUInt16(), RequestedGood = r.ReadUInt16(), RequestedAmount = r.ReadInt32(), IssuedDay = r.ReadInt32(), EnqueuedClock = r.ReadInt64(), DueClock = r.ReadInt64(), ExpiryClock = r.ReadInt64(), AiToAi = r.ReadBoolean(), Action = (TradeIntentAction)r.ReadByte(), State = (TradeIntentStatus)r.ReadByte(), Outcome = ReadBounded(r, 240) });
            n = r.ReadInt32();
            if (n < 0 || n > TradeIntentMailbox.MaximumRebellionHistories)
                throw new InvalidDataException("Rebellion count invalid");
            for (int i = 0; i < n; i++)
                m.Rebellions.Add(new TradeRebellionMemory { Vassal = r.ReadByte(), Overlord = r.ReadByte(), SinceDay = r.ReadInt32(), LastDay = r.ReadInt32(), StrengthDays = r.ReadInt32(), PoorDays = r.ReadInt32(), LastRebellionDay = r.ReadInt32(), AtRisk = r.ReadBoolean() });
            if (hasSteamReplay)
            {
                if (r.ReadByte() != 0xB6)
                    throw new InvalidDataException("Missing strict Steam replay ledger magic");
                m.SteamReplay.HostTerm = r.ReadUInt64();
                for (int sender = 1; sender <= 8; sender++)
                    m.SteamReplay.LastSeenBySender[sender] = r.ReadUInt64();
            }

            m.Validate(map, roster, day, archive);
            return m;
        }
    }
}
