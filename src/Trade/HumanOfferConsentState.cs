// Pure canonical  human-human consent state machine.
// No hook, API Shared, UI, Steam, or native direct code in this file.
// Requires the ONE CanonicalTradeReducer and ONE  mailbox inside its model.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal enum CanonicalHumanConsentState : byte
    {
        Pending = 1,
        Superseded = 2,
        Accepted = 3,
        PreparingNative = 4,
        Committed = 5,
        Declined = 6,
        Withdrawn = 7,
        Expired = 8,
        FailedCapacity = 9,
        Quarantined = 10
    }

    // Unique global quote identity. A counter is an entirely NEW quoted set
    // of terms from the current responder to the original offerer.
    // The current responder alone can accept/decline/counter the pending offer.
    internal sealed class CanonicalHumanOffer
    {
        internal ulong Id, RootId, ParentId;
        internal byte From, To, Tier, Depth, DecisionSlot;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount, IssuedDay, ExpiresDay;
        internal long IssuedClock, ExpiresClock, DecisionClock;
        internal ulong DecisionSequence, DecisionNonce;
        internal CanonicalHumanConsentState State;
        internal bool AiGenerated, DecisionIsAi;
        internal long AiDecisionDueClock;
        internal CanonicalHumanOffer Copy()
        {
            return (CanonicalHumanOffer)MemberwiseClone();
        }

        internal ulong OriginalId
        {
            get
            {
                return RootId == 0 ? Id : RootId;
            }
        }

        internal bool MatchesHumanTerms(OrderedHumanTrade input)
        {
            return input != null && input.CounterId == Id && input.CounterRootId == RootId && input.CounterOfferedAmount == OfferedAmount && input.CounterCreatedDay == IssuedDay && input.CounterExpiresDay == ExpiresDay && input.ReferenceOfferedGood == OfferedGood && input.ReferenceRequestedGood == RequestedGood && input.ReferenceRequestedAmount == RequestedAmount;
        }

        internal bool MatchesIdenticalConsent(OrderedHumanTrade input)
        {
            return MatchesHumanTerms(input) && input.OfferedGood == OfferedGood && input.WantedGood == RequestedGood && input.HumanOfferedAmount == OfferedAmount && input.Amount == RequestedAmount && input.Tier == Tier;
        }
    }

    // ONLY coordinator's original GameNetworkAPI + Chore verification may
    // construct this trusted provenance. It must use the real Steam identity
    // mapped to the human-controlled slot in the actual engine roster.
    // A participant's claimed source-slot inside a Steam payload is NOT proof.
    internal sealed class CanonicalTrustedHumanInput
    {
        internal readonly OrderedHumanTrade Command;
        internal readonly byte VerifiedControllerSlot;
        internal readonly ulong VerifiedSteamId;
        internal readonly long VerifiedMapGeneration;
        // Only a single original local-human offline skirmish can use this
        // provenance: the host engine itself supplied GetLocalPlayerId.
        internal readonly bool EngineVerifiedSoloLocal;
        internal CanonicalTrustedHumanInput(OrderedHumanTrade command, byte verifiedControllerSlot, ulong verifiedSteamId, long verifiedMapGeneration, bool originalEngineSoloLocal = false)
        {
            if (command == null || verifiedControllerSlot < 1 || verifiedControllerSlot > 8 || verifiedMapGeneration <= 0 || (originalEngineSoloLocal ? verifiedSteamId != 0 : verifiedSteamId <= 1000))
                throw new ArgumentException("Missing trusted original engine identity");
            Command = command;
            VerifiedControllerSlot = verifiedControllerSlot;
            VerifiedSteamId = verifiedSteamId;
            VerifiedMapGeneration = verifiedMapGeneration;
            EngineVerifiedSoloLocal = originalEngineSoloLocal;
        }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        private const int MaximumCanonicalHumanOffers = 2048;
        private const byte MaximumHumanCounterDepth = 8;
        private const int HumanOfferLifetimeDays = 5;
        private const long HumanOfferLifetimeTicks = 1200;
        // Every player-owned command, including existing human-to-AI bids,
        // must be bound to a real engine-verified Steam ID, player slot, map
        // generation and this exact globally ordered Chore payload.
        // UI and unsigned Steam senders cannot self-assert this identity.
        private static bool VerifiedHumanActor(CanonicalTradeSnapshot snapshot, OrderedHumanTrade request, CanonicalTrustedHumanInput originalEngine)
        {
            if (snapshot == null || request == null || originalEngine == null || !Object.ReferenceEquals(originalEngine.Command, request) || originalEngine.VerifiedControllerSlot != request.Requester || originalEngine.VerifiedMapGeneration < 1 || snapshot.MapGeneration != originalEngine.VerifiedMapGeneration)
                return false;
            CanonicalPlayer player = snapshot.Player(request.Requester);
            if (player == null || !player.Human || !player.Alive)
                return false;
            if (player.SteamId > 1000)
                return !originalEngine.EngineVerifiedSoloLocal && player.SteamId == originalEngine.VerifiedSteamId;
            // No fabricated Steam ID, no LAN fallback to insecure self.
            // The precise original local owner must be the SINGLE human.
            return player.SteamId == 0 && snapshot.LocalOfflineHumanSlot == request.Requester && originalEngine.EngineVerifiedSoloLocal && originalEngine.VerifiedSteamId == 0;
        }

        // Snapshot projection for the UI. Never return a mutable owned record.
        internal CanonicalHumanOffer[] HumanOffersFor(byte humanSlot)
        {
            if (humanSlot < 1 || humanSlot > 8)
                throw new ArgumentOutOfRangeException("humanSlot");
            return _model.HumanOffers.Where(x => x.From == humanSlot || x.To == humanSlot).OrderBy(x => x.Id).Select(x => x.Copy()).ToArray();
        }

        internal static void ExpireHumanOffers(CanonicalModel model)
        {
            foreach (var o in model.HumanOffers)
                if ((o.State == CanonicalHumanConsentState.Pending || o.State == CanonicalHumanConsentState.Accepted) && (model.Day > o.ExpiresDay || model.Mailbox.Clock > o.ExpiresClock))
                    o.State = CanonicalHumanConsentState.Expired;
        }

        private static bool HumanPairBusy(CanonicalModel model, byte a, byte b)
        {
            int pair = TradeHistoryArchive.PairKey(a, b);
            return model.HumanOffers.Any(x => (x.State == CanonicalHumanConsentState.Pending || x.State == CanonicalHumanConsentState.Accepted || x.State == CanonicalHumanConsentState.PreparingNative) && TradeHistoryArchive.PairKey(x.From, x.To) == pair);
        }

        private static void ReceiveHumanConsent(CanonicalModel model, CanonicalTradeSnapshot world, OrderedHumanTrade request, CanonicalTrustedHumanInput attested)
        {
            // An authentic host-signed Chore plus an original-engine slot/
            // Steam witness are BOTH required. Never trust user-owned IDs.
            CanonicalPlayer who = world.Player(request.Requester);
            CanonicalPlayer other = world.Player(request.Recipient);
            if (attested == null || attested.Command != request || who == null || other == null || !who.Human || !who.Alive || !other.Alive || attested.VerifiedControllerSlot != request.Requester || !VerifiedHumanActor(world, request, attested) || attested.VerifiedMapGeneration <= 0 || world.MapGeneration != attested.VerifiedMapGeneration || model.Quarantined || model.InputEpoch == 0 || model.InputEpoch != request.Epoch)
                return;
            ObservedTradePairState direct, reverse;
            if (!world.TryGetPair(request.Requester, request.Recipient, model.Day, out direct) || !world.TryGetPair(request.Recipient, request.Requester, model.Day, out reverse))
                return;
            if (HumanPairBusy(model, request.Requester, request.Recipient) && request.Action == CanonicalTradeAction.ProposeHuman)
                return;
            if (request.Action == CanonicalTradeAction.ProposeHuman)
            {
                if (!other.Human || model.HumanOffers.Count >= MaximumCanonicalHumanOffers || request.CounterId != 0 || request.CounterRootId != 0 || request.HumanOfferedAmount < 1 || request.HumanOfferedAmount > 100000 || request.Amount < 1 || request.Amount > 100000 || request.OfferedGood == request.WantedGood || GoodCatalog.Find(request.OfferedGood) == null || GoodCatalog.Find(request.WantedGood) == null || request.Tier > 2 || Busy(model, request.Requester, request.Recipient) || !CanReserve(model))
                    return;
                ulong id = Reserve(model);
                var offer = new CanonicalHumanOffer
                {
                    Id = id,
                    RootId = 0,
                    ParentId = 0,
                    From = request.Requester,
                    To = request.Recipient,
                    Tier = request.Tier,
                    Depth = 0,
                    OfferedGood = request.OfferedGood,
                    RequestedGood = request.WantedGood,
                    OfferedAmount = request.HumanOfferedAmount,
                    RequestedAmount = request.Amount,
                    IssuedDay = model.Day,
                    ExpiresDay = Math.Min(int.MaxValue - 32, model.Day + HumanOfferLifetimeDays),
                    IssuedClock = model.Mailbox.Clock,
                    ExpiresClock = checked(model.Mailbox.Clock + HumanOfferLifetimeTicks),
                    State = CanonicalHumanConsentState.Pending
                };
                model.HumanOffers.Add(offer);
                return;
            }

            CanonicalHumanOffer current = model.HumanOffers.FirstOrDefault(x => x.Id == request.CounterId);
            if (current == null || current.State != CanonicalHumanConsentState.Pending || model.Day > current.ExpiresDay || model.Mailbox.Clock > current.ExpiresClock || !current.MatchesHumanTerms(request))
                return;
            // Withdrawal is initiated by the ORIGINAL offerer, not the
            // recipient. It must bind exact current terms, expiry and root.
            if (request.Action == CanonicalTradeAction.WithdrawHuman)
            {
                if (current.From != request.Requester || current.To != request.Recipient || !current.MatchesIdenticalConsent(request))
                    return;
                current.DecisionSlot = request.Requester;
                current.DecisionSequence = request.Sequence;
                current.DecisionNonce = request.ClientNonce;
                current.DecisionClock = model.Mailbox.Clock;
                current.State = CanonicalHumanConsentState.Withdrawn;
                return;
            }

            // Every counter, accept or decline belongs to the currently
            // addressed actual HUMAN recipient, with an authenticated slot.
            if (current.From != request.Recipient || current.To != request.Requester)
                return;
            if (request.Action == CanonicalTradeAction.DeclineHuman || request.Action == CanonicalTradeAction.AcceptHuman)
            {
                if (!current.MatchesIdenticalConsent(request))
                    return;
                // mod ai offer: when this is a MOD-generated AI offer, its
                // human acceptance MUST use the original-engine-verified
                // local actor, same canonical ID, original AIC snapshot,
                // exact immutable terms and monotonic consumed input cursor.
                // A user-controlled payload cannot attest original CPU origin.
                if (request.Action == CanonicalTradeAction.AcceptHuman &&
                    !Human(model,current.From))
                {
                    if (!attested.EngineVerifiedSoloLocal)
                        return; // Real MP uses its separate signed lane.
                    var accepted = current.Copy();
                    accepted.State = CanonicalHumanConsentState.Accepted;
                    accepted.DecisionSlot = request.Requester;
                    accepted.DecisionSequence = request.Sequence;
                    accepted.DecisionNonce = request.ClientNonce;
                    accepted.DecisionClock = model.Mailbox.Clock;
                    string originReason;
                    if (!ModAiHumanOfferOrigin.TryClassifyAcceptedByOriginalLocalHuman(
                            ModAiOriginFacts(model,world,accepted),out originReason))
                        return;
                }
                current.DecisionSlot = request.Requester;
                current.DecisionSequence = request.Sequence;
                current.DecisionNonce = request.ClientNonce;
                current.DecisionClock = model.Mailbox.Clock;
                current.State = request.Action == CanonicalTradeAction.AcceptHuman ? CanonicalHumanConsentState.Accepted : CanonicalHumanConsentState.Declined;
                return;
            }

            if (request.Action != CanonicalTradeAction.CounterHuman || model.HumanOffers.Count >= MaximumCanonicalHumanOffers || request.HumanOfferedAmount < 1 || request.HumanOfferedAmount > 100000 || request.Amount < 1 || request.Amount > 100000 || request.OfferedGood == request.WantedGood || GoodCatalog.Find(request.OfferedGood) == null || GoodCatalog.Find(request.WantedGood) == null || request.Tier > 2 || current.Depth >= MaximumHumanCounterDepth || current.ExpiresClock <= model.Mailbox.Clock || !CanReserve(model))
                return;
            // Response by B is a fresh quote B->A, not accidental acceptance
            // of A's terms. Preserve root and parent for exact audit.
            ulong idForCounter = Reserve(model);
            current.DecisionSlot = request.Requester;
            current.DecisionSequence = request.Sequence;
            current.DecisionNonce = request.ClientNonce;
            current.DecisionClock = model.Mailbox.Clock;
            current.State = CanonicalHumanConsentState.Superseded;
            model.HumanOffers.Add(new CanonicalHumanOffer { Id = idForCounter, RootId = current.OriginalId, ParentId = current.Id, From = request.Requester, To = request.Recipient, Tier = request.Tier, Depth = (byte)(current.Depth + 1), OfferedGood = request.OfferedGood, RequestedGood = request.WantedGood, OfferedAmount = request.HumanOfferedAmount, RequestedAmount = request.Amount, IssuedDay = model.Day, ExpiresDay = Math.Min(current.ExpiresDay, Math.Min(int.MaxValue - 32, model.Day + HumanOfferLifetimeDays)), IssuedClock = model.Mailbox.Clock, ExpiresClock = Math.Min(current.ExpiresClock, checked(model.Mailbox.Clock + HumanOfferLifetimeTicks)), State = CanonicalHumanConsentState.Pending, AiDecisionDueClock = other.Human ? 0 : checked(model.Mailbox.Clock + CanonicalObservedReplyDelay(model, world, idForCounter, request.Requester, request.Recipient, request.Amount, TradeIntentAction.EvaluateAiCounter)) });
        }

        // Authoritative accepted human quote competes for the SAME ONE native
        // transition lane as delayed human-to-AI and AI-to-AI requests.
        // This method prepares a transition; no native inventory write here.
        private PreparedCanonicalTransition PrepareAcceptedHuman(CanonicalModel model, CanonicalTradeSnapshot snapshot)
        {
            CanonicalHumanOffer offer = model.HumanOffers.Where(x => x.State == CanonicalHumanConsentState.Accepted).OrderBy(x => x.DecisionClock).ThenBy(x => x.Id).FirstOrDefault();
            if (offer == null)
                return null;
            ObservedTradePairState pair, reverse;
            if (model.Mailbox.Clock > offer.ExpiresClock || model.Day > offer.ExpiresDay || !snapshot.TryGetPair(offer.From, offer.To, model.Day, out pair) || !snapshot.TryGetPair(offer.To, offer.From, model.Day, out reverse) || !snapshot.Player(offer.From).Alive || !snapshot.Player(offer.To).Alive)
            {
                offer.State = CanonicalHumanConsentState.Expired;
                return null;
            }

            // mod ai offer: revalidate an AI-initiated human ACCEPT under the same
            // current original-game snapshot immediately before preparing the
            // existing two-leg native journal. Persisted human consent alone
            // is not a native write permission or a fresh original AI command.
            if (!Human(model,offer.From))
            {
                string provenance;
                if (!ModAiHumanOfferOrigin.TryClassifyAcceptedByOriginalLocalHuman(
                        ModAiOriginFacts(model,snapshot,offer),out provenance))
                    throw new InvalidDataException(
                        "mod_ai_human_offer_origin_refused:" + provenance);
            }

            var quote = new BarterQuote
            {
                MapSession = model.MatchScope,
                HostTerm = model.MatchTerm,
                Revision = 1,
                Id = offer.Id,
                RootId = 0, // 's AI counter-root rule is separate.
                Requester = offer.From,
                Recipient = offer.To,
                OfferedGood = offer.OfferedGood,
                RequestedGood = offer.RequestedGood,
                OfferedAmount = offer.OfferedAmount,
                RequestedAmount = offer.RequestedAmount,
                Day = offer.IssuedDay,
                ExpiryDay = offer.ExpiresDay
            };
            ExactTradeCell[] cells;
            if (!TryCells(snapshot, quote, out cells))
            {
                offer.State = CanonicalHumanConsentState.FailedCapacity;
                return null;
            }

            TradeRelationship currentRelation = model.Archive.Relation(offer.From, offer.To);
            if (model.Archive.Records.Count >= TradeHistoryArchive.MaximumRecords || !TradeHistoryArchive.CanBookAnotherTrade(currentRelation.AcceptedTrades))
            {
                offer.State = CanonicalHumanConsentState.FailedCapacity;
                return null;
            }

            var record = new TradeRecord
            {
                Id = quote.Id,
                RootId = 0,
                Requester = quote.Requester,
                Recipient = quote.Recipient,
                OfferedGood = quote.OfferedGood,
                RequestedGood = quote.RequestedGood,
                OfferedAmount = quote.OfferedAmount,
                RequestedAmount = quote.RequestedAmount,
                IssuedDay = quote.Day,
                ExpiryDay = quote.ExpiryDay,
                Generosity = offer.Tier,
                AiToAi = false,
                ChanceBasisPoints = 10000,
                RollBasisPoints = 0,
                State = TradeDisposition.PreparingNative
            };
            model.Archive.Add(record);
            model.Archive.PendingNativeId = quote.Id;
            offer.State = CanonicalHumanConsentState.PreparingNative;
            CanonicalModel success = model.Copy();
            success.Revision = checked(success.Revision + 1);
            success.Archive.PendingNativeId = 0;
            success.Archive.Record(quote.Id).State = TradeDisposition.Committed;
            success.HumanOffers.Single(x => x.Id == offer.Id).State = CanonicalHumanConsentState.Committed;
            TradeRelationshipRules.CommitSuccessfulTrade(success.Archive.Relation(offer.From, offer.To), success.Day);
            CanonicalModel failure = model.Copy();
            failure.Revision = checked(failure.Revision + 1);
            failure.Archive.PendingNativeId = 0;
            failure.Archive.Record(quote.Id).State = TradeDisposition.Cancelled;
            failure.HumanOffers.Single(x => x.Id == offer.Id).State = CanonicalHumanConsentState.FailedCapacity;
            CanonicalModel quarantine = failure.Copy();
            quarantine.Quarantined = true;
            quarantine.Archive.Record(quote.Id).State = TradeDisposition.Quarantined;
            quarantine.HumanOffers.Single(x => x.Id == offer.Id).State = CanonicalHumanConsentState.Quarantined;
            return new PreparedCanonicalTransition(_identity, model, success, failure, quarantine, quote, cells);
        }

        // Exact previous schema17 pair/archive/mailbox is transferred into
        // ONE canonical model, never mirrored as a second active mailbox.
        // The coordinator must call this exactly once during schema18 import.
        internal static CanonicalTradeReducer MigrateLegacyAtSharedTick(CanonicalTradeSnapshot world, Guid stableMatchScope, ulong matchTerm, TradeHistoryArchive oldArchive, TradeIntentMailbox oldMailbox)
        {
            if (world == null || oldArchive == null || oldMailbox == null)
                throw new ArgumentNullException("legacy inputs");
            oldArchive.Validate(world.MapFingerprint, world.RosterFingerprint, world.Day);
            oldMailbox.Validate(world.MapFingerprint, world.RosterFingerprint, world.Day, oldArchive);
            var controller = new CanonicalTradeReducer(world, stableMatchScope, matchTerm);
            var model = controller._model.Copy();
            model.Archive = oldArchive.Copy();
            model.Mailbox = oldMailbox.Copy();
            model.Mailbox.RestoreAfterLoad(world.SharedTick);
            model.LastSharedTick = world.SharedTick;
            if (model.Archive.PendingNativeId != 0 || model.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative) || model.Mailbox.Intents.Any(x => x.State == TradeIntentStatus.Processing))
                model.Quarantined = true;
            model.Revision = checked(model.Revision + 1);
            controller.Publish(model);
            return controller;
        }
    }

    internal static partial class CanonicalTradeCodec
    {
        private static void WriteHumanOffers(BinaryWriter writer, IList<CanonicalHumanOffer> offers)
        {
            writer.Write(offers.Count);
            foreach (var x in offers.OrderBy(x => x.Id))
            {
                writer.Write(x.Id);
                writer.Write(x.RootId);
                writer.Write(x.ParentId);
                writer.Write(x.From);
                writer.Write(x.To);
                writer.Write(x.Tier);
                writer.Write(x.Depth);
                writer.Write(x.DecisionSlot);
                writer.Write(x.OfferedGood);
                writer.Write(x.RequestedGood);
                writer.Write(x.OfferedAmount);
                writer.Write(x.RequestedAmount);
                writer.Write(x.IssuedDay);
                writer.Write(x.ExpiresDay);
                writer.Write(x.IssuedClock);
                writer.Write(x.ExpiresClock);
                writer.Write(x.DecisionClock);
                writer.Write(x.DecisionSequence);
                writer.Write(x.DecisionNonce);
                writer.Write((byte)x.State);
                writer.Write(x.AiGenerated);
                writer.Write(x.DecisionIsAi);
                writer.Write(x.AiDecisionDueClock);
            }
        }

        private static void ReadHumanOffers(BinaryReader reader, CanonicalModel model, int version)
        {
            int count = Count(reader, 2048);
            for (int i = 0; i < count; i++)
                model.HumanOffers.Add(new CanonicalHumanOffer { Id = reader.ReadUInt64(), RootId = reader.ReadUInt64(), ParentId = reader.ReadUInt64(), From = reader.ReadByte(), To = reader.ReadByte(), Tier = reader.ReadByte(), Depth = reader.ReadByte(), DecisionSlot = reader.ReadByte(), OfferedGood = reader.ReadUInt16(), RequestedGood = reader.ReadUInt16(), OfferedAmount = reader.ReadInt32(), RequestedAmount = reader.ReadInt32(), IssuedDay = reader.ReadInt32(), ExpiresDay = reader.ReadInt32(), IssuedClock = reader.ReadInt64(), ExpiresClock = reader.ReadInt64(), DecisionClock = reader.ReadInt64(), DecisionSequence = reader.ReadUInt64(), DecisionNonce = reader.ReadUInt64(), State = (CanonicalHumanConsentState)reader.ReadByte(), AiGenerated = version >= 3 && ReadBool(reader), DecisionIsAi = version >= 3 && ReadBool(reader), AiDecisionDueClock = version >= 3 ? reader.ReadInt64() : 0 });
        }

        private static void ValidateHumanOffers(CanonicalModel model)
        {
            if (model.HumanOffers == null || model.HumanOffers.Count > 2048)
                throw new InvalidDataException("Human consent ledger overflow");
            if (model.HumanOffers.Any(x => x == null))
                throw new InvalidDataException("Null human offer");
            if (model.HumanOffers.Select(x => x.Id).Distinct().Count() != model.HumanOffers.Count)
                throw new InvalidDataException("Duplicate human quote identity");
            var offers = model.HumanOffers.ToDictionary(x => x.Id);
            var active = new HashSet<int>();
            foreach (var x in model.HumanOffers)
            {
                RequireMembers(model, x.From, x.To);
                if ((!IsHuman(model, x.From) && !IsHuman(model, x.To)) || (!IsHuman(model, x.From) && !x.AiGenerated) || x.AiGenerated && IsHuman(model, x.From) || !IsHuman(model, x.To) && x.ParentId == 0 || x.AiDecisionDueClock < 0 || !IsHuman(model, x.To) && x.AiDecisionDueClock < x.IssuedClock || x.Id < TradeHistoryArchive.MinimumTradeId || x.Id >= model.Archive.NextId || (x.Id - TradeHistoryArchive.MinimumTradeId) % 4 != 0 || model.Mailbox.Intents.Any(i => i.Id == x.Id) || x.Tier > 2 || x.Depth > 8 || x.OfferedGood == x.RequestedGood || GoodCatalog.Find(x.OfferedGood) == null || GoodCatalog.Find(x.RequestedGood) == null || x.OfferedAmount < 1 || x.OfferedAmount > 100000 || x.RequestedAmount < 1 || x.RequestedAmount > 100000 || x.IssuedDay < 1 || x.IssuedDay > model.Day || x.ExpiresDay < x.IssuedDay || x.ExpiresDay - x.IssuedDay > 15 || x.IssuedClock < 0 || x.ExpiresClock <= x.IssuedClock || x.ExpiresClock - x.IssuedClock > 2400 || !Enum.IsDefined(typeof(CanonicalHumanConsentState), x.State))
                    throw new InvalidDataException("Invalid saved human offer");
                if (x.ParentId == 0)
                {
                    if (x.RootId != 0 || x.Depth != 0)
                        throw new InvalidDataException("Invalid consent root");
                }
                else
                {
                    CanonicalHumanOffer previous;
                    if (!offers.TryGetValue(x.ParentId, out previous) || x.Id <= previous.Id || x.RootId != previous.OriginalId || x.From != previous.To || x.To != previous.From || x.Depth != previous.Depth + 1 || previous.State != CanonicalHumanConsentState.Superseded || x.ExpiresDay > previous.ExpiresDay || x.ExpiresClock > previous.ExpiresClock)
                        throw new InvalidDataException("Broken human counter lineage");
                }

                bool decided = x.State == CanonicalHumanConsentState.Accepted || x.State == CanonicalHumanConsentState.Declined || x.State == CanonicalHumanConsentState.Superseded || x.State == CanonicalHumanConsentState.Withdrawn || x.State == CanonicalHumanConsentState.PreparingNative || x.State == CanonicalHumanConsentState.Committed || x.State == CanonicalHumanConsentState.Quarantined;
                // FailedCapacity or Expired can happen before OR after a
                // real recipient's response; preserve the signed provenance
                // if present instead of inventing a different decision.
                bool hasSignedDecision = x.DecisionSequence != 0 || x.DecisionNonce != 0 || x.DecisionSlot != 0;
                if (x.DecisionIsAi && (IsHuman(model, x.To) || x.DecisionSlot != x.To || x.DecisionSequence != 0 || x.DecisionNonce != 0 || x.AiDecisionDueClock < 1 || x.DecisionClock < x.AiDecisionDueClock))
                    throw new InvalidDataException("Invalid saved AI consent");
                if (decided && !hasSignedDecision)
                    throw new InvalidDataException("Human action lost attestation");
                if (hasSignedDecision && (x.DecisionSlot != (x.State == CanonicalHumanConsentState.Withdrawn ? x.From : x.To) || !x.DecisionIsAi && (x.DecisionSequence == 0 || x.DecisionNonce == 0) || x.DecisionClock < x.IssuedClock || x.DecisionClock > model.Mailbox.Clock))
                    throw new InvalidDataException("Unattested saved human decision");
                if (!hasSignedDecision && x.DecisionClock != 0)
                    throw new InvalidDataException("Unowned human consent receipt");
                if (x.State == CanonicalHumanConsentState.Pending || x.State == CanonicalHumanConsentState.Accepted || x.State == CanonicalHumanConsentState.PreparingNative)
                {
                    if (!active.Add(TradeHistoryArchive.PairKey(x.From, x.To)))
                        throw new InvalidDataException("Conflicting active human offers");
                }

                TradeRecord receipt = model.Archive.Record(x.Id);
                bool nativeState = x.State == CanonicalHumanConsentState.PreparingNative || x.State == CanonicalHumanConsentState.Committed || x.State == CanonicalHumanConsentState.FailedCapacity || x.State == CanonicalHumanConsentState.Quarantined;
                if (nativeState && receipt == null && x.State != CanonicalHumanConsentState.FailedCapacity)
                    throw new InvalidDataException("Human native commit missing archive receipt");
                if (!IsHuman(model, x.To))
                {
                    CanonicalHumanOffer root;
                    if (!offers.TryGetValue(x.RootId, out root) || !root.AiGenerated || root.From != x.To || root.To != x.From)
                        throw new InvalidDataException("Unowned AI reply chain");
                }

                if (nativeState && receipt != null)
                {
                    var expectedState = x.State == CanonicalHumanConsentState.PreparingNative ? TradeDisposition.PreparingNative : x.State == CanonicalHumanConsentState.Committed ? TradeDisposition.Committed : x.State == CanonicalHumanConsentState.Quarantined ? TradeDisposition.Quarantined : TradeDisposition.Cancelled;
                    if (receipt.State != expectedState || receipt.RootId != 0 || receipt.Requester != x.From || receipt.Recipient != x.To || receipt.OfferedGood != x.OfferedGood || receipt.RequestedGood != x.RequestedGood || receipt.OfferedAmount != x.OfferedAmount || receipt.RequestedAmount != x.RequestedAmount || receipt.IssuedDay != x.IssuedDay || receipt.ExpiryDay != x.ExpiresDay)
                        throw new InvalidDataException("Human/native journal disagreement");
                }
                else if (receipt != null)
                    throw new InvalidDataException("Human quote reused as foreign trade id");
            }
        }

        private static bool IsCanonicalHumanReceipt(CanonicalModel model, TradeRecord rec)
        {
            var x = model.HumanOffers.FirstOrDefault(h => h.Id == rec.Id);
            if (x == null || x.From != rec.Requester || x.To != rec.Recipient || rec.RootId != 0 || rec.AiToAi)
                return false;
            return x.State == CanonicalHumanConsentState.PreparingNative || x.State == CanonicalHumanConsentState.Committed || x.State == CanonicalHumanConsentState.FailedCapacity || x.State == CanonicalHumanConsentState.Quarantined;
        }
    }
}
