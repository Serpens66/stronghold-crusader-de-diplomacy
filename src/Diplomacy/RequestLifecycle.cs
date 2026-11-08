using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal enum DiplomaticRequestKind : byte
    {
        Goods = 1,
        Military = 2,
    }

    internal enum DiplomaticRequestOrigin : byte
    {
        OriginalHumanGoods = 1,
        OriginalAutomaticGoods = 2,
        OriginalHumanOrder = 3,
        OriginalAutomaticOrder = 4,
        ModAuthored = 5,
        // Observed GameAction goods request with NO authenticated HUD or CPU
        // caller. Requester stores the verified local routing context only.
        UnattributedManagedGoods = 6,
        // The original handler supplied a verified AI slot. Its producer is unknown.
        RefusedUnattributedNativeGoods = 7,
    }

    internal enum DiplomaticRequestDecision : byte
    {
        Undecided = 0,
        Allowed = 1,
        Refused = 2,
    }

    internal enum DiplomaticRequestDecisionEvidence : byte
    {
        None = 0,
        ModPolicy = 1,
        ObservedOriginalReply = 2,
    }

    internal enum DiplomaticRequestStatus : byte
    {
        Captured = 1,
        Approved = 2,
        Forwarded = 3,
        PartiallyFulfilled = 4,
        Fulfilled = 5,
        Refused = 6,
        Cancelled = 7,
        Expired = 8,
    }

    internal enum DiplomaticRequestOutcomeEvidence : byte
    {
        None = 0,
        OriginalSubmitted = 1,
        OriginalRefusal = 2,
        ModPolicyRefusal = 3,
        ExplicitCancellation = 4,
        ObservedNativeCancellation = 5,
        LogicalExpiry = 6,
        VerifiedNativeTwoCell = 7,
        VerifiedCausalMilitaryOrder = 8,
        // A Mod-authored response was submitted by the Mod, not the
        // original game request producer or native evaluator.
        ModSubmitted = 9,
    }

    internal sealed class DiplomaticRequestDelivery
    {
        internal ulong ReceiptId;
        internal int Amount;

        internal DiplomaticRequestDelivery Copy()
        {
            return (DiplomaticRequestDelivery)MemberwiseClone();
        }
    }

    internal sealed class DiplomaticRequestRecord
    {
        internal ulong Id;
        internal int Requester, Counterparty, Subject, GoodId, Quantity, Opcode;
        internal int IssuedDay, UpdatedDay, DeliveredQuantity;
        internal DiplomaticRequestKind Kind;
        internal DiplomaticRequestOrigin Origin;
        internal DiplomaticRequestDecision Decision;
        internal DiplomaticRequestDecisionEvidence DecisionEvidence;
        internal DiplomaticRequestStatus Status;
        internal DiplomaticRequestOutcomeEvidence OutcomeEvidence;
        internal bool OriginalApprovalObserved;
        internal readonly List<DiplomaticRequestDelivery> Deliveries = new List<DiplomaticRequestDelivery>();

        internal bool Terminal
        {
            get
            {
                return Status == DiplomaticRequestStatus.Fulfilled ||
                    Status == DiplomaticRequestStatus.Refused ||
                    Status == DiplomaticRequestStatus.Cancelled ||
                    Status == DiplomaticRequestStatus.Expired;
            }
        }

        internal DiplomaticRequestRecord Copy()
        {
            var result = new DiplomaticRequestRecord
            {
                Id = Id,
                Requester = Requester,
                Counterparty = Counterparty,
                Subject = Subject,
                GoodId = GoodId,
                Quantity = Quantity,
                Opcode = Opcode,
                IssuedDay = IssuedDay,
                UpdatedDay = UpdatedDay,
                DeliveredQuantity = DeliveredQuantity,
                Kind = Kind,
                Origin = Origin,
                Decision = Decision,
                DecisionEvidence = DecisionEvidence,
                Status = Status,
                OutcomeEvidence = OutcomeEvidence,
                OriginalApprovalObserved = OriginalApprovalObserved,
            };
            result.Deliveries.AddRange(Deliveries.Select(x => x.Copy()));
            return result;
        }
    }

    internal sealed class DiplomaticRequestHistory
    {
        internal const int MaximumRecords = 64;
        internal const int MaximumDeliveriesPerRequest = 8;
        // Unknown callers cannot monopolize the shared bounded history.
        internal const int MaximumUnattributedPending = 8;
        // Local tracking horizon only; never an assertion of native expiration.
        internal const int NeutralTrackingWindowDays = 7;
        internal readonly string MapFingerprint, RosterFingerprint;
        internal ulong NextId = 1;
        // Reserved for a monotonic owner-issued original goods receipt domain.
        // No such persistent native gift receipt exists in the current engine journal.
        // This high-water is NOT interchangeable with Barter/TradeRecordId.
        internal ulong HighestGoodsReceiptId;
        // Distinct owner-issued Mod gift journal serial, reserved before
        // any Mod-owned two-cell mutation. Old schema20 has no such lineage.
        internal ulong NextModGiftReceiptId = 1;
        internal readonly List<DiplomaticRequestRecord> Records = new List<DiplomaticRequestRecord>();

        internal DiplomaticRequestHistory(string map, string roster)
        {
            if (string.IsNullOrEmpty(map) || string.IsNullOrEmpty(roster))
                throw new InvalidDataException("Request history requires a concrete owner scope");
            MapFingerprint = map;
            RosterFingerprint = roster;
        }

        internal DiplomaticRequestHistory Copy()
        {
            var copy = new DiplomaticRequestHistory(MapFingerprint, RosterFingerprint)
            {
                NextId = NextId,
                HighestGoodsReceiptId = HighestGoodsReceiptId,
                NextModGiftReceiptId = NextModGiftReceiptId,
            };
            copy.Records.AddRange(Records.Select(x => x.Copy()));
            return copy;
        }

        internal DiplomaticRequestRecord Find(ulong id)
        {
            return Records.FirstOrDefault(x => x.Id == id);
        }

        internal bool TryCapture(DiplomaticRequestKind kind, DiplomaticRequestOrigin origin,
            int requester, int counterparty, int subject, int good, int amount, int opcode,
            int day, IReadOnlyDictionary<int, int> roster, int local, out ulong id)
        {
            // Generic/manual ingress cannot assert that an unknown callback
            // has passed the separate actual owner/native lease verification.
            return TryCaptureCore(kind, origin, requester, counterparty, subject,
                good, amount, opcode, day, roster, local, false, out id);
        }

        // Called ONLY after native-lease/roles/terms and exact local generation,
        // map epoch, roster and owner checks. localContext identifies routing,
        // NOT the unknown original managed caller (never invent a HUD/CPU source).
        internal bool TryCaptureUnattributedManagedGoods(int localContext,
            int requestedPlayer, int good, int amount, int day,
            IReadOnlyDictionary<int, int> roster, int local, out ulong id)
        {
            return TryCaptureCore(DiplomaticRequestKind.Goods,
                DiplomaticRequestOrigin.UnattributedManagedGoods,
                localContext, requestedPlayer, 0, good, amount, 0, day,
                roster, local, true, out id);
        }

        internal bool TryRecordUnattributedNativeGoodsRefusal(int requester,
            int requested, int good, int amount, int day,
            IReadOnlyDictionary<int, int> roster, int local, out ulong id)
        {
            id = 0;
            var today = Records.Where(r => r.Origin ==
                DiplomaticRequestOrigin.RefusedUnattributedNativeGoods && r.IssuedDay == day);
            DiplomaticRequestRecord existing = today.FirstOrDefault(r => r.Requester == requester &&
                r.Counterparty == requested && r.GoodId == good && r.Quantity == amount);
            if (existing != null)
            {
                id = existing.Id;
                return true;
            }
            if (today.Count() >= MaximumUnattributedPending ||
                !TryCaptureCore(DiplomaticRequestKind.Goods,
                    DiplomaticRequestOrigin.RefusedUnattributedNativeGoods, requester,
                    requested, 0, good, amount, 0, day, roster, local, true, out id))
                return false;
            return TryDecide(id, false, DiplomaticRequestDecisionEvidence.ModPolicy, day);
        }

        private bool TryCaptureCore(DiplomaticRequestKind kind,
            DiplomaticRequestOrigin origin, int requester, int counterparty,
            int subject, int good, int amount, int opcode, int day,
            IReadOnlyDictionary<int, int> roster, int local,
            bool ownerVerifiedUnknown, out ulong id)
        {
            id = 0;
            if ((origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                 origin == DiplomaticRequestOrigin.RefusedUnattributedNativeGoods) !=
                    ownerVerifiedUnknown ||
                !DiplomaticRequestHistoryCodec.ValidTerms(kind, origin, requester,
                    counterparty, subject, good, amount, opcode, roster, local) ||
                !DiplomacyLogicalClock.IsValidSavedDay(day) ||
                NextId == 0 || NextId == ulong.MaxValue)
                return false;

            ExpireUnresolved(day);
            if (ownerVerifiedUnknown && Records.Count(r =>
                    r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods &&
                    !r.Terminal) >= MaximumUnattributedPending)
                return false;
            if (Records.Count >= MaximumRecords)
            {
                // Never forget a nonterminal request to make room for another.
                DiplomaticRequestRecord retired = Records.FirstOrDefault(x => x.Terminal);
                if (retired == null)
                    return false;
                Records.Remove(retired);
            }

            id = NextId++;
            Records.Add(new DiplomaticRequestRecord
            {
                Id = id,
                Requester = requester,
                Counterparty = counterparty,
                Subject = subject,
                GoodId = good,
                Quantity = amount,
                Opcode = opcode,
                Kind = kind,
                Origin = origin,
                IssuedDay = day,
                UpdatedDay = day,
                Decision = DiplomaticRequestDecision.Undecided,
                DecisionEvidence = DiplomaticRequestDecisionEvidence.None,
                Status = DiplomaticRequestStatus.Captured,
                OutcomeEvidence = DiplomaticRequestOutcomeEvidence.None,
            });
            return true;
        }

        internal bool TryDecide(ulong id, bool allowed, DiplomaticRequestDecisionEvidence evidence, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                r.Status != DiplomaticRequestStatus.Captured ||
                (r.Origin == DiplomaticRequestOrigin.ModAuthored &&
                 evidence != DiplomaticRequestDecisionEvidence.ModPolicy) ||
                (evidence != DiplomaticRequestDecisionEvidence.ModPolicy &&
                 evidence != DiplomaticRequestDecisionEvidence.ObservedOriginalReply))
                return false;
            r.Decision = allowed ? DiplomaticRequestDecision.Allowed : DiplomaticRequestDecision.Refused;
            r.DecisionEvidence = evidence;
            r.Status = allowed ? DiplomaticRequestStatus.Approved : DiplomaticRequestStatus.Refused;
            r.OutcomeEvidence = allowed ? DiplomaticRequestOutcomeEvidence.None :
                evidence == DiplomaticRequestDecisionEvidence.ModPolicy ?
                DiplomaticRequestOutcomeEvidence.ModPolicyRefusal :
                DiplomaticRequestOutcomeEvidence.OriginalRefusal;
            r.UpdatedDay = day;
            return true;
        }

        internal bool TryForward(ulong id, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                r.Status != DiplomaticRequestStatus.Approved)
                return false;
            r.Status = DiplomaticRequestStatus.Forwarded;
            r.OutcomeEvidence = r.Origin == DiplomaticRequestOrigin.ModAuthored ?
                DiplomaticRequestOutcomeEvidence.ModSubmitted :
                DiplomaticRequestOutcomeEvidence.OriginalSubmitted;
            r.UpdatedDay = day;
            return true; // Submission is never proof of native acceptance.
        }

        // The request ingress must independently bind an actual original reply to id.
        // This is not the initial policy approval, a native execution ACK,
        // a fulfilled goods request or military success.
        internal bool TryObserveApproval(ulong id, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                (r.Origin == DiplomaticRequestOrigin.ModAuthored &&
                 r.Kind == DiplomaticRequestKind.Goods) ||
                r.OriginalApprovalObserved ||
                (r.Status != DiplomaticRequestStatus.Forwarded &&
                 r.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                r.Decision != DiplomaticRequestDecision.Allowed ||
                day < r.UpdatedDay || !DiplomacyLogicalClock.IsValidSavedDay(day))
                return false;
            r.OriginalApprovalObserved = true;
            r.UpdatedDay = day;
            return true;
        }

        internal bool TryObserveRefusal(ulong id, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                (r.Origin == DiplomaticRequestOrigin.ModAuthored &&
                 r.Kind == DiplomaticRequestKind.Goods) ||
                r.OriginalApprovalObserved ||
                (r.Status != DiplomaticRequestStatus.Forwarded &&
                 r.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                day < r.UpdatedDay || !DiplomacyLogicalClock.IsValidSavedDay(day))
                return false;
            r.Decision = DiplomaticRequestDecision.Refused;
            r.DecisionEvidence = DiplomaticRequestDecisionEvidence.ObservedOriginalReply;
            r.Status = DiplomaticRequestStatus.Refused;
            r.OutcomeEvidence = DiplomaticRequestOutcomeEvidence.OriginalRefusal;
            r.UpdatedDay = day;
            return true;
        }

        internal bool TryCancel(ulong id, bool originalNativeCleared, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                r.Terminal)
                return false;
            r.Status = DiplomaticRequestStatus.Cancelled;
            r.OutcomeEvidence = originalNativeCleared ?
                DiplomaticRequestOutcomeEvidence.ObservedNativeCancellation :
                DiplomaticRequestOutcomeEvidence.ExplicitCancellation;
            r.UpdatedDay = day;
            return true; // An explicit intent to cancel is not native clearance.
        }

        // Neutral tracking expiry. It never cancels the original request,
        // interprets missing native replies, retries or changes any rating.
        internal int ExpireUnresolved(int day)
        {
            if (!DiplomacyLogicalClock.IsValidSavedDay(day))
                return 0;
            int count = 0;
            foreach (DiplomaticRequestRecord r in Records)
            {
                if (!r.Terminal && day >= r.UpdatedDay &&
                    (long)day - r.IssuedDay >= NeutralTrackingWindowDays)
                {
                    r.Status = DiplomaticRequestStatus.Expired;
                    r.OutcomeEvidence = DiplomaticRequestOutcomeEvidence.LogicalExpiry;
                    r.UpdatedDay = day;
                    count++;
                }
            }
            return count;
        }

        // Explicit neutral expiry has the same logical seven-day horizon as
        // the bulk drain. No native request is cancelled by this transition.
        internal bool TryExpire(ulong id, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Terminal ||
                !DiplomacyLogicalClock.IsValidSavedDay(day) ||
                day < r.UpdatedDay ||
                (long)day - r.IssuedDay < NeutralTrackingWindowDays)
                return false;
            r.Status = DiplomaticRequestStatus.Expired;
            r.OutcomeEvidence = DiplomaticRequestOutcomeEvidence.LogicalExpiry;
            r.UpdatedDay = day;
            return true;
        }

        // Only an explicit owner-side caller may reserve this identity; the
        // request tuple alone is NEVER a transfer correlation mechanism.
        internal bool TryReserveModGiftJournal(ulong requestId, int sender, int recipient,
            int good, int amount, int day, out ulong receiptId)
        {
            receiptId = 0;
            DiplomaticRequestRecord r = Find(requestId);
            if (r == null ||
                r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                r.Kind != DiplomaticRequestKind.Goods ||
                (r.Status != DiplomaticRequestStatus.Forwarded &&
                 r.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                r.Decision != DiplomaticRequestDecision.Allowed ||
                sender != r.Counterparty || recipient != r.Requester ||
                good != r.GoodId || amount < 1 ||
                amount > r.Quantity - r.DeliveredQuantity ||
                r.Deliveries.Count >= MaximumDeliveriesPerRequest ||
                day < r.UpdatedDay || !DiplomacyLogicalClock.IsValidSavedDay(day) ||
                NextModGiftReceiptId == 0 || NextModGiftReceiptId == ulong.MaxValue ||
                NextModGiftReceiptId <= HighestGoodsReceiptId)
                return false;
            receiptId = NextModGiftReceiptId++;
            return true;
        }

        // The runtime must independently prove a completed Mod-owned journal
        // with both native write checkpoints BEFORE invoking this method.
        internal bool TryCommitModGiftJournal(ulong requestId, ulong receiptId,
            int sender, int recipient, int good, int amount, int day)
        {
            DiplomaticRequestRecord r = Find(requestId);
            return r != null &&
                r.Origin != DiplomaticRequestOrigin.UnattributedManagedGoods &&
                day >= r.UpdatedDay &&
                receiptId != 0 && receiptId < NextModGiftReceiptId &&
                TryRecordVerifiedGoods(requestId, receiptId, sender, recipient,
                    good, amount, day);
        }

        internal bool TryRecordVerifiedGoods(ulong id, ulong receiptId, int sender, int recipient,
            int good, int deliveredAmount, int day)
        {
            DiplomaticRequestRecord r = Find(id);
            if (r == null || r.Kind != DiplomaticRequestKind.Goods ||
                r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                (r.Status != DiplomaticRequestStatus.Forwarded &&
                 r.Status != DiplomaticRequestStatus.PartiallyFulfilled) ||
                r.Decision != DiplomaticRequestDecision.Allowed ||
                receiptId == 0 || receiptId <= HighestGoodsReceiptId ||
                deliveredAmount < 1 || sender != r.Counterparty ||
                recipient != r.Requester || good != r.GoodId ||
                deliveredAmount > r.Quantity - r.DeliveredQuantity ||
                r.Deliveries.Count >= MaximumDeliveriesPerRequest ||
                Records.Any(x => x.Deliveries.Any(y => y.ReceiptId == receiptId)))
                return false;

            r.Deliveries.Add(new DiplomaticRequestDelivery { ReceiptId = receiptId, Amount = deliveredAmount });
            HighestGoodsReceiptId = receiptId;
            r.DeliveredQuantity = checked(r.DeliveredQuantity + deliveredAmount);
            r.Status = r.DeliveredQuantity == r.Quantity ?
                DiplomaticRequestStatus.Fulfilled : DiplomaticRequestStatus.PartiallyFulfilled;
            r.OutcomeEvidence = DiplomaticRequestOutcomeEvidence.VerifiedNativeTwoCell;
            r.UpdatedDay = day;
            return true;
        }
    }

    internal static class DiplomaticRequestHistoryCodec
    {
        private static bool InRoster(int player, IReadOnlyDictionary<int, int> roster)
        {
            return player >= 1 && player <= 8 && roster != null && roster.ContainsKey(player);
        }

        internal static bool ValidTerms(DiplomaticRequestKind kind, DiplomaticRequestOrigin origin,
            int requester, int counterparty, int subject, int good, int amount, int opcode,
            IReadOnlyDictionary<int, int> roster, int local)
        {
            if (!Enum.IsDefined(typeof(DiplomaticRequestKind), kind) ||
                !Enum.IsDefined(typeof(DiplomaticRequestOrigin), origin) ||
                !InRoster(requester, roster) || !InRoster(counterparty, roster) ||
                requester == counterparty || !InRoster(local, roster))
                return false;

            bool human = origin == DiplomaticRequestOrigin.OriginalHumanGoods ||
                origin == DiplomaticRequestOrigin.OriginalHumanOrder;
            bool automatic = origin == DiplomaticRequestOrigin.OriginalAutomaticGoods ||
                origin == DiplomaticRequestOrigin.OriginalAutomaticOrder;
            if ((human && requester != local) ||
                ((automatic || origin == DiplomaticRequestOrigin.RefusedUnattributedNativeGoods) &&
                 requester == local) ||
                (origin == DiplomaticRequestOrigin.UnattributedManagedGoods &&
                 requester != local))
                return false;

            if (kind == DiplomaticRequestKind.Goods)
                return (origin == DiplomaticRequestOrigin.OriginalHumanGoods ||
                        origin == DiplomaticRequestOrigin.OriginalAutomaticGoods ||
                        origin == DiplomaticRequestOrigin.ModAuthored ||
                        origin == DiplomaticRequestOrigin.UnattributedManagedGoods ||
                        origin == DiplomaticRequestOrigin.RefusedUnattributedNativeGoods) &&
                    subject == 0 && opcode == 0 && good >= 1 && good <= 24 &&
                    amount > 0 && amount <= 1000000;

            return (origin == DiplomaticRequestOrigin.OriginalHumanOrder ||
                    origin == DiplomaticRequestOrigin.OriginalAutomaticOrder ||
                    origin == DiplomaticRequestOrigin.ModAuthored) &&
                good == 0 && amount == 0 &&
                ((opcode >= 1 && opcode <= 6 && InRoster(subject, roster) &&
                  subject != requester && subject != counterparty) ||
                 ((opcode == 10 || opcode == 11) && subject == 0));
        }

        internal static void Validate(DiplomaticRequestHistory history, string map, string roster,
            int day, IReadOnlyDictionary<int, int> players, int local)
        {
            if (history == null)
                return;
            if (!string.Equals(history.MapFingerprint, map, StringComparison.Ordinal) ||
                !string.Equals(history.RosterFingerprint, roster, StringComparison.Ordinal) ||
                !DiplomacyLogicalClock.IsValidSavedDay(day) || history.NextId < 1 ||
                history.Records.Count > DiplomaticRequestHistory.MaximumRecords)
                throw new InvalidDataException("Diplomacy request history scope or high-water invalid");

            ulong previous = 0;
            int unknownPending = 0;
            var receipts = new HashSet<ulong>();
            foreach (DiplomaticRequestRecord r in history.Records)
            {
                if (r == null || r.Id <= previous || r.Id >= history.NextId ||
                    r.IssuedDay < 0 || r.IssuedDay > day ||
                    r.UpdatedDay < r.IssuedDay || r.UpdatedDay > day ||
                    !ValidTerms(r.Kind, r.Origin, r.Requester, r.Counterparty, r.Subject,
                        r.GoodId, r.Quantity, r.Opcode, players, local) ||
                    !Enum.IsDefined(typeof(DiplomaticRequestDecision), r.Decision) ||
                    !Enum.IsDefined(typeof(DiplomaticRequestDecisionEvidence), r.DecisionEvidence) ||
                    !Enum.IsDefined(typeof(DiplomaticRequestStatus), r.Status) ||
                    !Enum.IsDefined(typeof(DiplomaticRequestOutcomeEvidence), r.OutcomeEvidence) ||
                    r.DeliveredQuantity < 0 || r.DeliveredQuantity > r.Quantity ||
                    r.Deliveries.Count > DiplomaticRequestHistory.MaximumDeliveriesPerRequest ||
                    (r.Decision == DiplomaticRequestDecision.Undecided) !=
                    (r.DecisionEvidence == DiplomaticRequestDecisionEvidence.None) ||
                    (r.OriginalApprovalObserved &&
                     (r.Decision != DiplomaticRequestDecision.Allowed ||
                      (r.Status != DiplomaticRequestStatus.Forwarded &&
                       r.Status != DiplomaticRequestStatus.PartiallyFulfilled &&
                       r.Status != DiplomaticRequestStatus.Fulfilled &&
                       r.Status != DiplomaticRequestStatus.Cancelled &&
                       r.Status != DiplomaticRequestStatus.Expired))))
                    throw new InvalidDataException("Invalid diplomacy request identity, roles or disposition");

                if (r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods &&
                    !r.Terminal && ++unknownPending >
                        DiplomaticRequestHistory.MaximumUnattributedPending)
                    throw new InvalidDataException("Unattributed pending observation limit exceeded");
                // A permanently unattributed managed callback is observation
                // only. Even checksum-authenticated Cold cannot upgrade it to
                // Allowed/Forwarded, refusal, native delivery or any penalty.
                if (r.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods &&
                    (r.Kind != DiplomaticRequestKind.Goods ||
                     r.Decision != DiplomaticRequestDecision.Undecided ||
                     r.DecisionEvidence != DiplomaticRequestDecisionEvidence.None ||
                     r.OriginalApprovalObserved || r.DeliveredQuantity != 0 ||
                     r.Deliveries.Count != 0 ||
                     (r.Status != DiplomaticRequestStatus.Captured &&
                      r.Status != DiplomaticRequestStatus.Expired) ||
                     (r.Status == DiplomaticRequestStatus.Captured &&
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.None) ||
                     (r.Status == DiplomaticRequestStatus.Expired &&
                      (r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.LogicalExpiry ||
                       (long)r.UpdatedDay - r.IssuedDay <
                          DiplomaticRequestHistory.NeutralTrackingWindowDays))))
                    throw new InvalidDataException("Unattributed request cannot become actionable");

                if (r.Origin == DiplomaticRequestOrigin.RefusedUnattributedNativeGoods &&
                    (r.Status != DiplomaticRequestStatus.Refused ||
                     r.Decision != DiplomaticRequestDecision.Refused ||
                     r.DecisionEvidence != DiplomaticRequestDecisionEvidence.ModPolicy ||
                     r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.ModPolicyRefusal ||
                     r.OriginalApprovalObserved || r.DeliveredQuantity != 0 || r.Deliveries.Count != 0))
                    throw new InvalidDataException("Unattributed native request must remain refused");

                int delivered = 0;
                foreach (DiplomaticRequestDelivery entry in r.Deliveries)
                {
                    if (entry == null || entry.ReceiptId == 0 ||
                        entry.Amount <= 0 || entry.ReceiptId > history.HighestGoodsReceiptId ||
                        !receipts.Add(entry.ReceiptId) ||
                        r.Kind != DiplomaticRequestKind.Goods)
                        throw new InvalidDataException("Duplicate or unowned native request receipt");
                    delivered = checked(delivered + entry.Amount);
                }

                // Mod-authored goods never acquire an original reply.
                if (r.Kind == DiplomaticRequestKind.Goods &&
                    r.Origin == DiplomaticRequestOrigin.ModAuthored &&
                    r.OriginalApprovalObserved)
                    throw new InvalidDataException("Mod goods cannot claim original approval");

                if (delivered != r.DeliveredQuantity ||
                    ((r.DeliveredQuantity > 0) != (r.Deliveries.Count > 0)) ||
                    (r.Status != DiplomaticRequestStatus.Fulfilled && r.DeliveredQuantity == r.Quantity &&
                     r.DeliveredQuantity > 0))
                    throw new InvalidDataException("Request delivery accounting does not match receipts");

                bool admitted = r.Decision == DiplomaticRequestDecision.Allowed;
                if ((r.Status == DiplomaticRequestStatus.Captured &&
                     (r.Decision != DiplomaticRequestDecision.Undecided ||
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.None)) ||
                    (r.Status == DiplomaticRequestStatus.Approved &&
                     (!admitted || r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.None)) ||
                    (r.Status == DiplomaticRequestStatus.Forwarded &&
                     (!admitted ||
                      (r.Origin == DiplomaticRequestOrigin.ModAuthored ?
                       (r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.ModSubmitted &&
                        r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.OriginalSubmitted) :
                       r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.OriginalSubmitted))) ||
                    (r.Status == DiplomaticRequestStatus.PartiallyFulfilled &&
                     (!admitted || r.Kind != DiplomaticRequestKind.Goods ||
                      r.DeliveredQuantity < 1 || r.DeliveredQuantity >= r.Quantity ||
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.VerifiedNativeTwoCell)) ||
                    (r.Status == DiplomaticRequestStatus.Fulfilled &&
                     (!admitted || r.Kind != DiplomaticRequestKind.Goods ||
                      r.DeliveredQuantity != r.Quantity || r.Deliveries.Count == 0 ||
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.VerifiedNativeTwoCell)) ||
                    (r.Status == DiplomaticRequestStatus.Refused &&
                     (r.Decision != DiplomaticRequestDecision.Refused ||
                      (r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.ModPolicyRefusal &&
                       r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.OriginalRefusal) ||
                      (r.OutcomeEvidence == DiplomaticRequestOutcomeEvidence.ModPolicyRefusal &&
                       r.DecisionEvidence != DiplomaticRequestDecisionEvidence.ModPolicy) ||
                      (r.OutcomeEvidence == DiplomaticRequestOutcomeEvidence.OriginalRefusal &&
                       r.DecisionEvidence != DiplomaticRequestDecisionEvidence.ObservedOriginalReply))) ||
                    (r.Status == DiplomaticRequestStatus.Cancelled &&
                     (r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.ExplicitCancellation &&
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.ObservedNativeCancellation)) ||
                    (r.Status == DiplomaticRequestStatus.Expired &&
                     (r.UpdatedDay <= r.IssuedDay ||
                      r.OutcomeEvidence != DiplomaticRequestOutcomeEvidence.LogicalExpiry)))
                    throw new InvalidDataException("Request outcome has no matching proof category");

                if ((r.Status == DiplomaticRequestStatus.Captured ||
                     r.Status == DiplomaticRequestStatus.Approved ||
                     r.Status == DiplomaticRequestStatus.Forwarded) &&
                    r.Deliveries.Count != 0)
                    throw new InvalidDataException("Native deliveries cannot precede a receipt");
                previous = r.Id;
            }
        }

        internal static void Write(BinaryWriter writer, DiplomaticRequestHistory history,
            string map, string roster, int day, IReadOnlyDictionary<int, int> players, int local)
        {
            Validate(history, map, roster, day, players, local);
            writer.Write(history != null);
            if (history == null)
                return;
            writer.Write(history.NextId);
            writer.Write(history.HighestGoodsReceiptId);
            writer.Write(history.Records.Count);
            foreach (DiplomaticRequestRecord r in history.Records)
            {
                writer.Write(r.Id);
                writer.Write((byte)r.Requester);
                writer.Write((byte)r.Counterparty);
                writer.Write((byte)r.Subject);
                writer.Write(r.GoodId);
                writer.Write(r.Quantity);
                writer.Write(r.Opcode);
                writer.Write(r.IssuedDay);
                writer.Write(r.UpdatedDay);
                writer.Write(r.DeliveredQuantity);
                writer.Write((byte)r.Kind);
                writer.Write((byte)r.Origin);
                writer.Write((byte)r.Decision);
                writer.Write((byte)r.DecisionEvidence);
                writer.Write((byte)r.Status);
                writer.Write((byte)r.OutcomeEvidence);
                writer.Write(r.OriginalApprovalObserved);
                writer.Write(r.Deliveries.Count);
                foreach (DiplomaticRequestDelivery receipt in r.Deliveries)
                {
                    writer.Write(receipt.ReceiptId);
                    writer.Write(receipt.Amount);
                }
            }
        }

        internal static DiplomaticRequestHistory Read(BinaryReader reader, string map, string roster,
            int day, IReadOnlyDictionary<int, int> players, int local)
        {
            byte present = reader.ReadByte();
            if (present > 1)
                throw new InvalidDataException("Invalid request-history presence flag");
            if (present == 0)
                return new DiplomaticRequestHistory(map, roster);

            var result = new DiplomaticRequestHistory(map, roster)
            {
                NextId = reader.ReadUInt64(),
                HighestGoodsReceiptId = reader.ReadUInt64(),
            };
            int count = reader.ReadInt32();
            if (count < 0 || count > DiplomaticRequestHistory.MaximumRecords)
                throw new InvalidDataException("Invalid request-history record count");
            for (int i = 0; i < count; i++)
            {
                var r = new DiplomaticRequestRecord
                {
                    Id = reader.ReadUInt64(),
                    Requester = reader.ReadByte(),
                    Counterparty = reader.ReadByte(),
                    Subject = reader.ReadByte(),
                    GoodId = reader.ReadInt32(),
                    Quantity = reader.ReadInt32(),
                    Opcode = reader.ReadInt32(),
                    IssuedDay = reader.ReadInt32(),
                    UpdatedDay = reader.ReadInt32(),
                    DeliveredQuantity = reader.ReadInt32(),
                    Kind = (DiplomaticRequestKind)reader.ReadByte(),
                    Origin = (DiplomaticRequestOrigin)reader.ReadByte(),
                    Decision = (DiplomaticRequestDecision)reader.ReadByte(),
                    DecisionEvidence = (DiplomaticRequestDecisionEvidence)reader.ReadByte(),
                    Status = (DiplomaticRequestStatus)reader.ReadByte(),
                    OutcomeEvidence = (DiplomaticRequestOutcomeEvidence)reader.ReadByte(),
                };
                byte originalApproval = reader.ReadByte();
                if (originalApproval > 1)
                    throw new InvalidDataException("Invalid original approval evidence flag");
                r.OriginalApprovalObserved = originalApproval == 1;
                int deliveries = reader.ReadInt32();
                if (deliveries < 0 || deliveries > DiplomaticRequestHistory.MaximumDeliveriesPerRequest)
                    throw new InvalidDataException("Invalid request receipt count");
                for (int j = 0; j < deliveries; j++)
                    r.Deliveries.Add(new DiplomaticRequestDelivery
                    {
                        ReceiptId = reader.ReadUInt64(),
                        Amount = reader.ReadInt32(),
                    });
                result.Records.Add(r);
            }

            Validate(result, map, roster, day, players, local);
            return result;
        }
    }
}
