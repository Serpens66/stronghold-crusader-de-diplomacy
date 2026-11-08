//  standalone checksummed aid promises, memory and reciprocal favors.
// Pure managed model. NOT registered with game's schema14 SaveData handler.
// Real Native/Save authorization is still absent; no game goods can be moved.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal enum AidPledgeStatus : byte
    {
        Pending = 1,
        Delivered = 2,
        Cancelled = 3,
        Expired = 4
    }

    internal sealed class AidPledge
    {
        internal ulong Id;
        internal byte Donor, Recipient;
        internal ushort Good;
        internal int Quantity, IssuedDay, DueDay;
        internal AidReliefReason Reason;
        internal AidPledgeStatus Status;
        internal AidPledge Copy()
        {
            return (AidPledge)MemberwiseClone();
        }

        internal void Validate(int day, ulong highWater)
        {
            if (Id < 5001 || Id >= highWater || Donor < 1 || Donor > 8 || Recipient < 1 || Recipient > 8 || Donor == Recipient || GoodCatalog.Find(Good) == null || Quantity < 3 || Quantity > 40 || IssuedDay < 1 || IssuedDay > day || DueDay < IssuedDay || DueDay > (long)IssuedDay + 5 || (!Enum.IsDefined(typeof(AidReliefReason), Reason)) || (!Enum.IsDefined(typeof(AidPledgeStatus), Status)))
                throw new InvalidDataException("Invalid bounded native aid promise");
        }
    }

    // Signed balance: positive means B owes A, negative means A owes B.
    internal sealed class ReciprocalAidBalance
    {
        internal byte A, B;
        internal int Balance, LastDeliveryDay = -100, Completed;
        internal ReciprocalAidBalance Copy()
        {
            return (ReciprocalAidBalance)MemberwiseClone();
        }
    }

    internal sealed partial class AidLedger
    {
        internal const int MaxPromises = 128, MaxPairs = 28, MaxBalance = 400;
        internal readonly string Map, Roster;
        internal int Day;
        internal ulong NextId = 5001;
        internal readonly List<AidPledge> Promises = new List<AidPledge>();
        internal readonly List<ReciprocalAidBalance> Favors = new List<ReciprocalAidBalance>();
        // PROCESS-LOCAL only. A checksum-valid restored Delivered/Favor
        // is NOT proof of an original Native gift, even in a disposable
        // private build. Only the actual  two-cell native readback
        // and journal completion may latch this bit. It is NEVER encoded.
        private bool _originalNativeGiftObservedThisProcess;
        // Independent of the live observation flag above. The private
        // history-verification partial may set this bit only after the
        // owner-confirmed cryptographically protected original-save receipt
        // verifies the COMPLETE current .sav and COMPLETE AidLedger hashes,
        // generation, native scope and pinned original live Gift history.
        // NO bool, rehashed ledger or QA flag is by itself such authority.
        // Neither field is ever serialized in the  envelope.
        private bool _originalNativeHistoryAttested;
        internal bool HasUnattestedHistoricalNativeAid
        {
            get
            {
                return (Promises.Any(x => x != null && x.Status == AidPledgeStatus.Delivered) || Favors.Count != 0) && !_originalNativeHistoryAttested;
            }
        }

        internal bool OriginalNativeGiftObservedThisProcess
        {
            get
            {
                return _originalNativeGiftObservedThisProcess;
            }
        }

        internal AidLedger(string map, string roster, int day)
        {
            Map = map;
            Roster = roster;
            Day = day;
            Validate();
        }

        internal AidLedger(string map, string roster, int day, ulong next)
        {
            Map = map;
            Roster = roster;
            Day = day;
            NextId = next;
        }

        internal static int PairKey(int a, int b)
        {
            if (a < 1 || a > 8 || b < 1 || b > 8 || a == b)
                throw new InvalidDataException("Bad bilateral favor");
            return Math.Min(a, b) * 256 + Math.Max(a, b);
        }

        internal AidLedger Copy()
        {
            var copy = new AidLedger(Map, Roster, Day, NextId);
            copy.Promises.AddRange(Promises.Select(p => p.Copy()));
            copy.Favors.AddRange(Favors.Select(p => p.Copy()));
            // Preserve only while COPYING this live process's validated
            // model. Encode/Decode never serializes this bit; on cold
            // load it is always false until fresh original readback.
            copy._originalNativeGiftObservedThisProcess = _originalNativeGiftObservedThisProcess;
            copy._originalNativeHistoryAttested = _originalNativeHistoryAttested;
            return copy;
        }

        internal void Validate()
        {
            if (string.IsNullOrEmpty(Map) || string.IsNullOrEmpty(Roster) || Encoding.UTF8.GetByteCount(Map) > 128 || Encoding.UTF8.GetByteCount(Roster) > 128 || Day < 1 || NextId < 5001 || NextId >= ulong.MaxValue - 3 || Promises.Count > MaxPromises || Favors.Count > MaxPairs)
                throw new InvalidDataException("Aid ledger is malformed");
            // RELEASE SAFETY: Schema15 persists promises, but this runtime
            // intentionally has NO verified native delivery/receipt authority.
            // The nested SHA256 checksum and cross-ledger arithmetic only
            // prove self-consistency, NOT a successful native goods transfer.
            // An attacker could otherwise forge BOTH a Delivered promise and
            // its matching Favor, recompute SHA256, and mint goodwill/debt.
            // Keep research-only synthetic completion tests strictly isolated
            // from the production plugin until a genuine authoritative,
            // durable native-game receipt contract is implemented.
#if !DIPLOMACY_PRIVATE_TESTS
            if (Promises.Any(p => p != null && p.Status == AidPledgeStatus.Delivered) || Favors.Count != 0)
                throw new InvalidDataException("Native aid delivery authority not attested: no completed aid or favor credits");
#endif
            var ids = new HashSet<ulong>();
            foreach (var p in Promises)
            {
                if (p == null)
                    throw new InvalidDataException("Null aid promise");
                p.Validate(Day, NextId);
                if (!ids.Add(p.Id))
                    throw new InvalidDataException("Repeated aid receipt");
                if (p.Status == AidPledgeStatus.Delivered && p.DueDay < Day - 30L)
                {
                // Old receipts remain valid; they should never be treated as
                // a *fresh* reason to give more aid this turn.
                }
            }

            // A persisted "completed" favor must be backed by actual
            // terminal delivery receipts from the SAME immutable archive.
            // Without this cross-ledger invariant, a crafted but correctly
            // rechecksummed record could invent a lifelong diplomatic debt.
            // Research-only completion is NOT present in production builds.
            var delivered = new Dictionary<int, Tuple<int, int, int>>();
            foreach (var p in Promises.Where(x => x.Status == AidPledgeStatus.Delivered).OrderBy(x => x.Id))
            {
                int key = PairKey(p.Donor, p.Recipient);
                Tuple<int, int, int> before;
                delivered.TryGetValue(key, out before);
                int balance = before == null ? 0 : before.Item1;
                int count = before == null ? 0 : before.Item2;
                int newestIssue = before == null ? 0 : before.Item3;
                long signed = (long)balance + (p.Donor < p.Recipient ? p.Quantity : -p.Quantity);
                int bounded = (int)Math.Max(-MaxBalance, Math.Min(MaxBalance, signed));
                delivered[key] = Tuple.Create(bounded, checked(count + 1), Math.Max(newestIssue, p.IssuedDay));
            }

            var pairs = new HashSet<int>();
            foreach (var f in Favors)
            {
                if (f == null || f.A < 1 || f.B > 8 || f.A >= f.B || Math.Abs((long)f.Balance) > MaxBalance || f.Completed < 0 || f.Completed >= int.MaxValue || (f.LastDeliveryDay != -100 && (f.LastDeliveryDay < 1 || f.LastDeliveryDay > Day)))
                    throw new InvalidDataException("Invalid reciprocal aid favor");
                int key = PairKey(f.A, f.B);
                Tuple<int, int, int> expected;
                if (!pairs.Add(key) || !delivered.TryGetValue(key, out expected) || f.Balance != expected.Item1 || f.Completed != expected.Item2 || f.LastDeliveryDay < expected.Item3)
                    throw new InvalidDataException("Aid favor has no matching delivered inventory receipt");
                delivered.Remove(key);
            }

            if (delivered.Count != 0)
                throw new InvalidDataException("Delivered aid receipt has no corresponding favor evidence");
        }

        internal AidPledge Promise(ProposedAidShipment plan)
        {
            Validate();
            if (plan == null || plan.Day != Day || plan.Donor < 1 || plan.Donor > 8 || plan.Recipient < 1 || plan.Recipient > 8 || plan.Donor == plan.Recipient || plan.Quantity < 3 || plan.Quantity > 40 || GoodCatalog.Find(plan.Good) == null || !Enum.IsDefined(typeof(AidReliefReason), plan.Reason) || Promises.Any(p => p.Status == AidPledgeStatus.Pending && (p.Donor == plan.Donor || p.Recipient == plan.Recipient)))
                throw new InvalidOperationException("Aid budget, identity or receipt unavailable");
            // Terminal cancellations/expirations have NO favor or delivery
            // effect. Reuse their bounded memory slot so a game can keep
            // negotiating after 128 unsuccessful relief proposals. Never
            // prune a Delivered receipt: every credited favor needs proof.
            AidPledge disposable = null;
            if (Promises.Count >= MaxPromises)
            {
                disposable = Promises.Where(p => p.Status == AidPledgeStatus.Cancelled || p.Status == AidPledgeStatus.Expired).OrderBy(p => p.Id).FirstOrDefault();
                if (disposable == null)
                    throw new InvalidOperationException("Aid receipts full; delivered native proof cannot be discarded");
            }

            var newPromise = new AidPledge
            {
                Id = NextId,
                Donor = plan.Donor,
                Recipient = plan.Recipient,
                Good = plan.Good,
                Quantity = plan.Quantity,
                IssuedDay = Day,
                DueDay = checked(Day + 2),
                Reason = plan.Reason,
                Status = AidPledgeStatus.Pending
            };
            newPromise.Validate(Day, NextId + 1);
            if (disposable != null)
                Promises.Remove(disposable);
            Promises.Add(newPromise);
            NextId = checked(NextId + 1);
            return newPromise.Copy();
        }

        internal bool Cancel(ulong id)
        {
            Validate();
            var p = Promises.SingleOrDefault(x => x.Id == id);
            if (p == null)
                throw new InvalidDataException("Unknown aid identity");
            if (p.Status != AidPledgeStatus.Pending)
                return false;
            p.Status = AidPledgeStatus.Cancelled;
            return true;
        }

        internal void AdvanceDay(int day)
        {
            Validate();
            if (day < Day || day < 1)
                throw new ArgumentOutOfRangeException("day");
            foreach (var p in Promises)
                if (p.Status == AidPledgeStatus.Pending && day > p.DueDay)
                    p.Status = AidPledgeStatus.Expired;
            Day = day;
            Validate();
        }

#if DIPLOMACY_PRIVATE_TESTS
        // Recorded only after the native transfer journal has independently
        // read back both original goods cells under EngineInterface.threadLock.
        // This method does not
        // authorize, simulate or write any goods.
        internal void RecordObservedNativeDelivery(byte donor, byte recipient, ushort good, int amount, int gameDay)
        {
            Validate();
            if (HasUnattestedHistoricalNativeAid)
                throw new InvalidOperationException("Loaded historical delivered/favor claims are not original native receipts");
            if (gameDay != Day || donor < 1 || donor > 8 || recipient < 1 || recipient > 8 || donor == recipient || amount < 3 || amount > 40)
                throw new InvalidOperationException("Native aid terms out of scope");
            AidPledge[] matches = Promises.Where(x => x.Status == AidPledgeStatus.Pending && x.Donor == donor && x.Recipient == recipient && x.Good == good && x.Quantity == amount && x.IssuedDay <= gameDay && x.DueDay >= gameDay).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("No unique pending native aid promise");
            AidLedger next = Copy();
            AidPledge confirmed = next.Promises.Single(x => x.Id == matches[0].Id);
            int pair = PairKey(donor, recipient);
            ReciprocalAidBalance existing = next.Favors.FirstOrDefault(x => PairKey(x.A, x.B) == pair);
            if (existing == null)
            {
                if (next.Favors.Count >= MaxPairs)
                    throw new InvalidOperationException("Aid favor table full");
                existing = new ReciprocalAidBalance
                {
                    A = (byte)Math.Min(donor, recipient),
                    B = (byte)Math.Max(donor, recipient)
                };
                next.Favors.Add(existing);
            }

            long signed = (long)existing.Balance + (donor == existing.A ? amount : -amount);
            existing.Balance = (int)Math.Max(-MaxBalance, Math.Min(MaxBalance, signed));
            existing.Completed = checked(existing.Completed + 1);
            existing.LastDeliveryDay = gameDay;
            confirmed.Status = AidPledgeStatus.Delivered;
            next.Validate();
            // Commit the complete managed receipt as one exception-free swap
            // after validation. The outer  save boundary owns durability.
            Promises.Clear();
            Promises.AddRange(next.Promises);
            Favors.Clear();
            Favors.AddRange(next.Favors);
            // The ONLY transition to this process-local marker is after
            // actual original native donor+recipient readback, the same
            //  Gift mutation journal and validated completed receipt.
            // Persistence deliberately does not retain this marker.
            _originalNativeGiftObservedThisProcess = true;
            _originalNativeHistoryAttested = true;
        }

#endif
        internal ObservedMutualAidAgreement AidTerms(byte a, byte b, int goodwill, bool allied)
        {
            Validate();
            int key = PairKey(a, b);
            var f = Favors.FirstOrDefault(x => PairKey(x.A, x.B) == key);
            return new ObservedMutualAidAgreement
            {
                A = (byte)Math.Min(a, b),
                B = (byte)Math.Max(a, b),
                Allied = allied,
                Goodwill = goodwill,
                LastCompletedAidDay = f == null ? -100 : f.LastDeliveryDay,
                // Runtime observations carry the signed balance, while the planner
                // also supports explicitly supplied unsigned observations.
                HasDirectionalFavorBalance = true,
                SignedOutstandingFavor = f == null ? 0 : f.Balance
            };
        }
    }

    internal static class AidPromiseLedgerCodec
    {
        internal const int MaximumBytes = 32768, Version = 1;
        // Reject foreign-world memories BEFORE creating a save payload.
        // A matching checksum only proves consistency, not ownership.
        internal static void ValidateScope(AidLedger memory, string map, string roster, int currentDay)
        {
            if (memory == null || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(roster) || currentDay < 1)
                throw new InvalidDataException("Missing aid/world scope");
            memory.Validate();
            if (!string.Equals(memory.Map, map, StringComparison.Ordinal) || !string.Equals(memory.Roster, roster, StringComparison.Ordinal) || memory.Day > currentDay)
                throw new InvalidDataException("Aid memory bound to another map/roster or future day");
        }

        // A loaded but no-longer-current promise must not survive into the
        // *next* save as a usable Pending aid obligation. Time progression
        // only expires promises; it does NOT create completion receipts.
        internal static AidLedger SnapshotForSave(AidLedger memory, string map, string roster, int currentDay)
        {
            ValidateScope(memory, map, roster, currentDay);
            AidLedger copy = memory.Copy();
            if (copy.Day < currentDay)
                copy.AdvanceDay(currentDay);
            ValidateScope(copy, map, roster, currentDay);
            return copy;
        }

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SE-AID73");
        internal static byte[] Encode(AidLedger memory)
        {
            if (memory == null)
                throw new ArgumentNullException("memory");
            memory.Validate();
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(memory.Map);
                w.Write(memory.Roster);
                w.Write(memory.Day);
                w.Write(memory.NextId);
                w.Write(memory.Promises.Count);
                foreach (var p in memory.Promises.OrderBy(x => x.Id))
                {
                    w.Write(p.Id);
                    w.Write(p.Donor);
                    w.Write(p.Recipient);
                    w.Write(p.Good);
                    w.Write(p.Quantity);
                    w.Write(p.IssuedDay);
                    w.Write(p.DueDay);
                    w.Write((byte)p.Reason);
                    w.Write((byte)p.Status);
                }

                w.Write(memory.Favors.Count);
                foreach (var f in memory.Favors.OrderBy(x => x.A).ThenBy(x => x.B))
                {
                    w.Write(f.A);
                    w.Write(f.B);
                    w.Write(f.Balance);
                    w.Write(f.LastDeliveryDay);
                    w.Write(f.Completed);
                }

                w.Flush();
                byte[] payload = stream.ToArray();
                using (var sha = SHA256.Create())
                    w.Write(sha.ComputeHash(payload));
                w.Flush();
                if (stream.Length > MaximumBytes)
                    throw new InvalidDataException("Aid save too large");
                return stream.ToArray();
            }
        }

        internal static AidLedger Decode(byte[] data, string map, string roster, int maxDay)
        {
            if (data == null || data.Length < Magic.Length + 4 + 32 || data.Length > MaximumBytes || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(roster) || maxDay < 1)
                throw new InvalidDataException("Aid envelope missing or over-sized");
            int unsignedLength = data.Length - 32;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data, 0, unsignedLength);
                for (int i = 0; i < 32; i++)
                    if (hash[i] != data[unsignedLength + i])
                        throw new InvalidDataException("Aid envelope checksum mismatch");
            }

            using (var s = new MemoryStream(data, false))
            using (var r = new BinaryReader(s, Encoding.UTF8, true))
            {
                if (!r.ReadBytes(Magic.Length).SequenceEqual(Magic) || r.ReadInt32() != Version)
                    throw new InvalidDataException("Aid envelope schema unsupported");
                var srcMap = r.ReadString();
                var srcRoster = r.ReadString();
                int day = r.ReadInt32();
                ulong next = r.ReadUInt64();
                if (srcMap != map || srcRoster != roster || day > maxDay || day < 1)
                    throw new InvalidDataException("Wrong aid map, roster or day");
                var record = new AidLedger(srcMap, srcRoster, day, next);
                int count = r.ReadInt32();
                if (count < 0 || count > AidLedger.MaxPromises)
                    throw new InvalidDataException("Aid receipt count invalid");
                for (int i = 0; i < count; i++)
                    record.Promises.Add(new AidPledge { Id = r.ReadUInt64(), Donor = r.ReadByte(), Recipient = r.ReadByte(), Good = r.ReadUInt16(), Quantity = r.ReadInt32(), IssuedDay = r.ReadInt32(), DueDay = r.ReadInt32(), Reason = (AidReliefReason)r.ReadByte(), Status = (AidPledgeStatus)r.ReadByte() });
                count = r.ReadInt32();
                if (count < 0 || count > AidLedger.MaxPairs)
                    throw new InvalidDataException("Favor pair count invalid");
                for (int i = 0; i < count; i++)
                    record.Favors.Add(new ReciprocalAidBalance { A = r.ReadByte(), B = r.ReadByte(), Balance = r.ReadInt32(), LastDeliveryDay = r.ReadInt32(), Completed = r.ReadInt32() });
                if (s.Position != unsignedLength)
                    throw new InvalidDataException("Aid envelope has trailing bytes");
                record.Validate();
                return record;
            }
        }
    }
}
