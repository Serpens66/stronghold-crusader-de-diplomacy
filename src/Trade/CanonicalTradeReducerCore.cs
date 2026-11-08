// CanonicalTradeReducer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal enum CanonicalTradeAction : byte
    {
        Propose = 0,
        AcceptCounter = 1,
        DeclineCounter = 2,
        ProposeHuman = 4,
        CounterHuman = 5,
        AcceptHuman = 6,
        DeclineHuman = 7,
        WithdrawHuman = 8
    }

    // Constructed only by the signed original Chore adapter.
    // Counter responses carry the exact displayed counter terms, not just its ID.
    internal sealed class OrderedHumanTrade
    {
        internal readonly ulong Sequence, Epoch, ClientNonce;
        internal readonly byte Requester, Recipient, Tier;
        internal readonly ushort WantedGood, OfferedGood;
        internal readonly int Amount, CounterOfferedAmount;
        // Frozen literal whole-unit human-human terms, NOT an AI pricing tier.
        internal readonly int HumanOfferedAmount;
        internal readonly ushort ReferenceOfferedGood, ReferenceRequestedGood;
        internal readonly int ReferenceRequestedAmount;
        internal readonly ulong CounterId, CounterRootId;
        internal readonly int CounterCreatedDay, CounterExpiresDay;
        internal readonly CanonicalTradeAction Action;
        internal OrderedHumanTrade(ulong sequence, ulong epoch, ulong clientNonce, byte requester, byte recipient, ushort wantedGood, ushort offeredGood, int amount, byte tier, CanonicalTradeAction action, ulong counterId = 0, ulong counterRootId = 0, int counterOfferedAmount = 0, int counterCreatedDay = 0, int counterExpiresDay = 0, int humanOfferedAmount = 0, ushort referenceOfferedGood = 0, ushort referenceRequestedGood = 0, int referenceRequestedAmount = 0)
        {
            Sequence = sequence;
            Epoch = epoch;
            ClientNonce = clientNonce;
            Requester = requester;
            Recipient = recipient;
            WantedGood = wantedGood;
            OfferedGood = offeredGood;
            Amount = amount;
            Tier = tier;
            Action = action;
            CounterId = counterId;
            CounterRootId = counterRootId;
            CounterOfferedAmount = counterOfferedAmount;
            CounterCreatedDay = counterCreatedDay;
            CounterExpiresDay = counterExpiresDay;
            HumanOfferedAmount = humanOfferedAmount;
            ReferenceOfferedGood = referenceOfferedGood;
            ReferenceRequestedGood = referenceRequestedGood;
            ReferenceRequestedAmount = referenceRequestedAmount;
        }
    }

    // Authentication happens outside the kernel. An activation must be a signed,
    // globally ordered session-activation Chore, never a UI request.
    internal sealed class SignedTradeActivation
    {
        internal readonly ulong Epoch;
        internal SignedTradeActivation(ulong epoch)
        {
            Epoch = epoch;
        }
    }

    internal sealed class AuthenticatedTradeTick
    {
        private readonly OrderedHumanTrade[] _requests;
        // Only the coordinator's Engine-owned sender-to-slot attestation
        // allows two human-controlled lords to bind contractual consent.
        private readonly CanonicalTrustedHumanInput[] _trusted;
        internal readonly SignedTradeActivation Activation;
        internal AuthenticatedTradeTick(SignedTradeActivation activation, IEnumerable<OrderedHumanTrade> globallyOrderedRequests)
        {
            Activation = activation;
            if (globallyOrderedRequests == null)
                throw new ArgumentNullException("globallyOrderedRequests");
            _requests = globallyOrderedRequests.ToArray();
            if (_requests.Length > 64 || _requests.Any(x => x == null))
                throw new ArgumentException("Invalid ordered tick batch");
        }

        internal AuthenticatedTradeTick(SignedTradeActivation activation, IEnumerable<CanonicalTrustedHumanInput> originalEngineAuthenticated)
        {
            Activation = activation;
            if (originalEngineAuthenticated == null)
                throw new ArgumentNullException("originalEngineAuthenticated");
            _trusted = originalEngineAuthenticated.ToArray();
            if (_trusted.Length > 64 || _trusted.Any(x => x == null || x.Command == null))
                throw new ArgumentException("Invalid original engine authenticated batch");
            _requests = _trusted.Select(x => x.Command).ToArray();
        }

        internal OrderedHumanTrade[] Requests()
        {
            return (OrderedHumanTrade[])_requests.Clone();
        }

        internal CanonicalTrustedHumanInput TrustedAt(int i)
        {
            return _trusted == null ? null : _trusted[i];
        }
    }

    internal sealed class CanonicalPlayer
    {
        internal readonly byte Slot;
        internal readonly bool Human, Alive;
        internal readonly ulong SteamId;
        internal CanonicalPlayer(byte slot, bool human, bool alive, ulong originalSteamId = 0)
        {
            if (slot < 1 || slot > 8)
                throw new ArgumentException("slot");
            Slot = slot;
            Human = human;
            Alive = alive;
            SteamId = originalSteamId;
            if (!human && originalSteamId != 0 || originalSteamId > 0 && originalSteamId <= 1000)
                throw new ArgumentException("Invalid original human Steam identity");
        }
    }

    // One directed observation for every ordered pair of roster members.
    // TradePact means an actual canonical pact, not an inferred relationship.
    internal sealed class CanonicalPairObservation
    {
        private readonly ObservedTradePairState _state;
        internal readonly bool RequesterAlliedToRecipient;
        internal readonly bool RecipientAlliedToRequester;
        internal readonly bool CanonicalTradePact;
        internal CanonicalPairObservation(ObservedTradePairState state, bool requesterAlliedToRecipient, bool recipientAlliedToRequester, bool canonicalTradePact)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            _state = state.Copy();
            RequesterAlliedToRecipient = requesterAlliedToRecipient;
            RecipientAlliedToRequester = recipientAlliedToRequester;
            CanonicalTradePact = canonicalTradePact;
        }

        internal ObservedTradePairState State()
        {
            return _state.Copy();
        }
    }

    internal interface ICanonicalTradeSnapshotSource
    {
        // Called under the parent's original engine monitor.
        // Must represent this exact shared tick, with all players observed.
        CanonicalTradeSnapshot Capture(long sharedTick);
    }

    internal sealed class CanonicalTradeSnapshot : IObservedTradePairProvider
    {
        private readonly CanonicalPlayer[] _players;
        private readonly Ruler[] _rulers;
        private readonly Good[] _prices;
        private readonly ManagedGoodsSnapshotPort _stocks;
        private readonly Dictionary<int, CanonicalPairObservation> _pairs;
        // Ephemeral, same-frame original AI evidence. Never a second saved ledger.
        private readonly Dictionary<byte, CanonicalOriginalAiEconomy> _originalAiEconomy;
        internal readonly long SharedTick, MapGeneration;
        // Non-Steam original solo game may have NO lobby Steam ID for the
        // local user; only the engine's exact local slot can be authorized.
        internal readonly byte LocalOfflineHumanSlot;
        internal readonly int Day;
        internal readonly string MapFingerprint, RosterFingerprint;
        internal CanonicalTradeSnapshot(long sharedTick, int day, string map, string roster, IEnumerable<CanonicalPlayer> players, ManagedGoodsSnapshotPort stocks, IEnumerable<Good> prices, IEnumerable<Ruler> rulers, IEnumerable<CanonicalPairObservation> pairs, long mapGeneration = 0, byte originalLocalSoloSlot = 0, IEnumerable<CanonicalOriginalAiEconomy> originalAiEconomy = null)
        {
            if (sharedTick < 0 || day < 1 || day > int.MaxValue - 32)
                throw new ArgumentException("Invalid tick/day");
            CheckText(map);
            CheckText(roster);
            if (players == null || stocks == null || prices == null || rulers == null || pairs == null)
                throw new ArgumentNullException();
            if (mapGeneration < 0)
                throw new ArgumentException("Negative map generation");
            SharedTick = sharedTick;
            MapGeneration = mapGeneration;
            Day = day;
            MapFingerprint = map;
            RosterFingerprint = roster;
            _players = players.OrderBy(x => x == null ? 0 : x.Slot).ToArray();
            if (_players.Length < 2 || _players.Length > 8 || _players.Any(x => x == null) || _players.Select(x => x.Slot).Distinct().Count() != _players.Length || _players.Where(x => x.Human && x.SteamId > 1000).GroupBy(x => x.SteamId).Any(x => x.Count() != 1))
                throw new ArgumentException("Invalid all-player roster");
            if (originalLocalSoloSlot != 0 && (_players.Count(p => p.Human) != 1 || !_players.Any(p => p.Slot == originalLocalSoloSlot && p.Human && p.Alive && p.SteamId == 0)))
                throw new ArgumentException("Offline local slot not unique and original");
            LocalOfflineHumanSlot = originalLocalSoloSlot;
            _rulers = rulers.Select(x => x == null ? null : x.Copy()).OrderBy(x => x == null ? 0 : x.PlayerId).ToArray();
            if (_rulers.Length != _players.Length || _rulers.Any(x => x == null || x.Name == null || x.Name.Length > 128 || !Percent(x.Trust) || !Percent(x.Greed) || !Percent(x.Caution) || !Percent(x.Commerce)) || !_rulers.Select(x => x.PlayerId).SequenceEqual(_players.Select(x => (int)x.Slot)))
                throw new ArgumentException("Incomplete ruler snapshot");
            var market = new ObservedTradeMarketSnapshot(prices);
            _prices = GoodCatalog.All.Select(g =>
            {
                Good p = market.Find(g.Id);
                // Bound arithmetic before the supplied economic code uses it.
                if (p.MarketSell > 20000000m || p.MarketBuy > 20000000m)
                    throw new ArgumentException("Market price outside native bound");
                return new Good(p.Id, g.Name, p.MarketSell, p.MarketBuy);
            }).ToArray();
            _stocks = new ManagedGoodsSnapshotPort();
            foreach (CanonicalPlayer player in _players)
                foreach (Good good in GoodCatalog.All)
                {
                    StockCell cell = stocks.Read(player.Slot, good.Id);
                    if (cell == null)
                        throw new ArgumentException("Missing exact stock cell");
                    cell.Validate();
                    _stocks.Set(player.Slot, good.Id, cell);
                }

            _pairs = new Dictionary<int, CanonicalPairObservation>();
            foreach (CanonicalPairObservation observation in pairs)
            {
                if (observation == null)
                    throw new ArgumentException("Null pair");
                ObservedTradePairState state = observation.State();
                if (state.Requester == state.Recipient || Player(state.Requester) == null || Player(state.Recipient) == null || state.Day != Day || state.RequesterAlive != Player(state.Requester).Alive || state.RecipientAlive != Player(state.Recipient).Alive)
                    throw new ArgumentException("Pair identity/life mismatch");
                int key = Directed(state.Requester, state.Recipient);
                if (_pairs.ContainsKey(key))
                    throw new ArgumentException("Duplicate pair");
                _pairs.Add(key, observation);
            }

            if (_pairs.Count != _players.Length * (_players.Length - 1))
                throw new ArgumentException("Incomplete directed diplomacy snapshot");
            // Hostility, alliance and direct vassal identity must agree in both
            // directions. Trust/grudge/threat may legitimately be directed.
            foreach (CanonicalPairObservation observation in _pairs.Values)
            {
                ObservedTradePairState a = observation.State();
                CanonicalPairObservation reverse = _pairs[Directed(a.Recipient, a.Requester)];
                ObservedTradePairState b = reverse.State();
                if (a.Hostile != b.Hostile || a.Authoritative != b.Authoritative || a.Vassal != b.Vassal || a.Overlord != b.Overlord || observation.CanonicalTradePact != reverse.CanonicalTradePact || observation.RequesterAlliedToRecipient != reverse.RecipientAlliedToRequester || observation.RecipientAlliedToRequester != reverse.RequesterAlliedToRecipient)
                    throw new ArgumentException("Inconsistent reciprocal observation");
            }

            _originalAiEconomy = new Dictionary<byte, CanonicalOriginalAiEconomy>();
            if (originalAiEconomy != null)
            {
                foreach (CanonicalOriginalAiEconomy original in originalAiEconomy)
                {
                    if (original == null || _originalAiEconomy.ContainsKey(original.PlayerSlot))
                        throw new ArgumentException("Duplicate/null original AI economic frame");
                    CanonicalPlayer player = Player(original.PlayerSlot);
                    if (player == null || player.Human || !player.Alive)
                        throw new ArgumentException("Original AI economy belongs to a non-live AI");
                    _originalAiEconomy.Add(original.PlayerSlot, original.Copy());
                }
                if (_originalAiEconomy.Count != _players.Count(p => !p.Human && p.Alive))
                    throw new ArgumentException("Incomplete current-frame AI identities and purchase data");
            }
        }

        private static bool Percent(int n)
        {
            return n >= 0 && n <= 100;
        }

        private static int Directed(byte a, byte b)
        {
            return (a << 8) | b;
        }

        private static void CheckText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 256)
                throw new ArgumentException("Invalid scope fingerprint");
        }

        internal CanonicalPlayer Player(byte slot)
        {
            return _players.FirstOrDefault(x => x.Slot == slot);
        }

        internal CanonicalPlayer[] Players()
        {
            return (CanonicalPlayer[])_players.Clone();
        }

        // Absent economic evidence is an explicit DENIAL for new autonomous
        // diplomacy. Existing human commands and saved counter resolution do
        // not need AI market-scheduling permission.
        internal bool TryOriginalAiEconomy(byte slot, out CanonicalOriginalAiEconomy economy)
        {
            CanonicalOriginalAiEconomy original;
            bool observed = _originalAiEconomy.TryGetValue(slot, out original);
            economy = observed ? original.Copy() : null;
            return observed;
        }

        internal Ruler[] Rulers(bool aiOnly)
        {
            return _rulers.Where(r => !aiOnly || !Player((byte)r.PlayerId).Human).Select(r => r.Copy()).ToArray();
        }

        internal Good[] Prices()
        {
            return _prices.Select(g => new Good(g.Id, g.Name, g.MarketSell, g.MarketBuy)).ToArray();
        }

        internal ManagedGoodsSnapshotPort CloneStocks()
        {
            var copy = new ManagedGoodsSnapshotPort();
            foreach (CanonicalPlayer player in _players)
                foreach (Good good in GoodCatalog.All)
                    copy.Set(player.Slot, good.Id, _stocks.Read(player.Slot, good.Id));
            return copy;
        }

        internal StockCell Read(byte player, ushort good)
        {
            return _stocks.Read(player, good);
        }

#if DIPLOMACY_PRIVATE_TESTS
        // already captured directed pair only; never query native/game.
        internal bool TryCopyPrivateRawPair(
            byte requester, byte recipient, out ObservedTradePairState state,
            out bool requesterAllied, out bool recipientAllied, out bool tradePact)
        {
            state = null;
            requesterAllied = recipientAllied = tradePact = false;
            CanonicalPairObservation captured;
            if (!_pairs.TryGetValue(Directed(requester, recipient), out captured))
                return false;
            state = captured.State();
            requesterAllied = captured.RequesterAlliedToRecipient;
            recipientAllied = captured.RecipientAlliedToRequester;
            tradePact = captured.CanonicalTradePact;
            return true;
        }
#endif

        public bool TryGetPair(byte requester, byte recipient, int day, out ObservedTradePairState pair)
        {
            pair = null;
            CanonicalPairObservation observation;
            if (day != Day || !_pairs.TryGetValue(Directed(requester, recipient), out observation))
                return false;
            ObservedTradePairState current = observation.State();
            // Never trust a generic HasTradeRelation observation.
            current.HasTradeRelation = observation.CanonicalTradePact || (observation.RequesterAlliedToRecipient && observation.RecipientAlliedToRequester);
            string reason;
            if (!TradePairEligibilityRules.CanTrade(current, requester, recipient, day, out reason))
                return false;
            pair = current;
            return true;
        }
    }

    internal sealed class ExactTradeCell
    {
        private readonly StockCell _before;
        internal readonly byte Player;
        internal readonly ushort Good;
        internal readonly int BeforeQuantity, AfterQuantity;
        internal ExactTradeCell(byte player, ushort good, StockCell before, int afterQuantity)
        {
            if (before == null)
                throw new ArgumentNullException("before");
            before.Validate();
            if (afterQuantity < 0 || afterQuantity > before.Capacity)
                throw new ArgumentException("afterQuantity");
            Player = player;
            Good = good;
            _before = before.Copy();
            BeforeQuantity = before.Quantity;
            AfterQuantity = afterQuantity;
        }

        internal StockCell BeforeCell()
        {
            return _before.Copy();
        }
    }

    // Runtime implementation owns the native monitor, writes and rollback.
    // These methods are legitimate adapters, not implemented native calls.
    internal interface ICanonicalTradeNativePort
    {
        StockCell Read(byte player, ushort good);
        void WriteAbsolute(byte player, ushort good, int quantity);
    }

    internal sealed class CanonicalModel
    {
        internal long Revision, LastSharedTick;
        internal int Day, WorldSeed;
        internal Guid MatchScope;
        internal ulong MatchTerm, InputEpoch;
        internal bool Quarantined;
        internal byte RosterMask, HumanMask;
        internal ulong[] InputSequence = new ulong[9];
        internal ulong[] InputNonce = new ulong[9];
        internal TradeHistoryArchive Archive;
        internal TradeIntentMailbox Mailbox;
        // The only additionally persisted human-to-human consent ledger.
        // One authoritative  mailbox, one canonical ID source and one
        // prepared four-cell transaction slot are retained.
        internal List<CanonicalHumanOffer> HumanOffers = new List<CanonicalHumanOffer>();
        internal CanonicalModel Copy()
        {
            return new CanonicalModel
            {
                Revision = Revision,
                LastSharedTick = LastSharedTick,
                Day = Day,
                WorldSeed = WorldSeed,
                MatchScope = MatchScope,
                MatchTerm = MatchTerm,
                InputEpoch = InputEpoch,
                Quarantined = Quarantined,
                RosterMask = RosterMask,
                HumanMask = HumanMask,
                InputSequence = (ulong[])InputSequence.Clone(),
                InputNonce = (ulong[])InputNonce.Clone(),
                Archive = Archive.Copy(),
                Mailbox = Mailbox.Copy(),
                HumanOffers = HumanOffers.Select(x => x.Copy()).ToList()
            };
        }
    }

    internal sealed class PreparedCanonicalTransition
    {
        private readonly object _owner;
        private readonly CanonicalModel _before, _after, _failure, _quarantine;
        private readonly byte[] _beforeBytes, _afterBytes, _failureBytes, _quarantineBytes;
        private readonly ExactTradeCell[] _cells;
        private readonly BarterQuote _quote;
        internal readonly long SourceRevision, SharedTick;
        internal readonly ulong TradeId;
        internal PreparedCanonicalTransition(object owner, CanonicalModel before, CanonicalModel after, CanonicalModel failure, CanonicalModel quarantine, BarterQuote quote, ExactTradeCell[] cells)
        {
            _owner = owner;
            _before = before.Copy();
            _after = after.Copy();
            _failure = failure.Copy();
            _quarantine = quarantine.Copy();
            _quote = quote.Copy();
            _cells = (ExactTradeCell[])cells.Clone();
            SourceRevision = before.Revision;
            SharedTick = before.LastSharedTick;
            TradeId = quote.Id;
            // All allocations, validation and serialization precede native writes.
            _beforeBytes = CanonicalTradeCodec.Encode(_before);
            _afterBytes = CanonicalTradeCodec.Encode(_after);
            _failureBytes = CanonicalTradeCodec.Encode(_failure);
            _quarantineBytes = CanonicalTradeCodec.Encode(_quarantine);
        }

        internal bool OwnedBy(object owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal CanonicalModel BeforeModel()
        {
            return _before.Copy();
        }

        internal CanonicalModel AfterModel()
        {
            return _after.Copy();
        }

        internal CanonicalModel FailureModel(bool ambiguous)
        {
            return (ambiguous ? _quarantine : _failure).Copy();
        }

        internal byte[] BeforeState()
        {
            return (byte[])_beforeBytes.Clone();
        }

        internal byte[] AfterState()
        {
            return (byte[])_afterBytes.Clone();
        }

        internal byte[] FailureState()
        {
            return (byte[])_failureBytes.Clone();
        }

        internal byte[] QuarantineState()
        {
            return (byte[])_quarantineBytes.Clone();
        }

        internal ExactTradeCell[] Cells()
        {
            return (ExactTradeCell[])_cells.Clone();
        }

        internal BarterQuote Quote()
        {
            return _quote.Copy();
        }

        // Publishing uses prebuilt immutable objects; it does not rebuild state.
        internal CanonicalModel PrebuiltAfter
        {
            get
            {
                return _after;
            }
        }

        internal CanonicalModel PrebuiltFailure
        {
            get
            {
                return _failure;
            }
        }

        internal CanonicalModel PrebuiltQuarantine
        {
            get
            {
                return _quarantine;
            }
        }

        internal byte[] PrebuiltAfterBytes
        {
            get
            {
                return _afterBytes;
            }
        }

        internal byte[] PrebuiltFailureBytes
        {
            get
            {
                return _failureBytes;
            }
        }

        internal byte[] PrebuiltQuarantineBytes
        {
            get
            {
                return _quarantineBytes;
            }
        }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        private readonly object _identity = new object ();
        private CanonicalModel _model;
        private byte[] _save;
        private PreparedCanonicalTransition _prepared;
        internal CanonicalTradeReducer(CanonicalTradeSnapshot initial, Guid stableMatchScope, ulong matchTerm)
        {
            if (initial == null || stableMatchScope == Guid.Empty || matchTerm == 0)
                throw new ArgumentException("Missing stable match identity");
            byte roster = 0, humans = 0;
            foreach (CanonicalPlayer player in initial.Players())
            {
                roster |= Bit(player.Slot);
                if (player.Human)
                    humans |= Bit(player.Slot);
            }

            _model = new CanonicalModel
            {
                Revision = 1,
                LastSharedTick = initial.SharedTick,
                Day = initial.Day,
                MatchScope = stableMatchScope,
                MatchTerm = matchTerm,
                RosterMask = roster,
                HumanMask = humans,
                WorldSeed = Seed(stableMatchScope, matchTerm, initial.MapFingerprint, initial.RosterFingerprint),
                Archive = new TradeHistoryArchive
                {
                    MapFingerprint = initial.MapFingerprint,
                    RosterFingerprint = initial.RosterFingerprint
                },
                Mailbox = new TradeIntentMailbox
                {
                    MapFingerprint = initial.MapFingerprint,
                    RosterFingerprint = initial.RosterFingerprint
                }
            };
            _model.Mailbox.Rebase(initial.SharedTick);
            _save = CanonicalTradeCodec.Encode(_model);
        }

        private CanonicalTradeReducer(CanonicalModel restored)
        {
            _model = restored;
            _save = CanonicalTradeCodec.Encode(restored);
        }

        internal static CanonicalTradeReducer RestoreAtSharedTick(byte[] bytes, CanonicalTradeSnapshot restoredWorld, Guid expectedScope, ulong expectedTerm)
        {
            if (restoredWorld == null)
                throw new ArgumentNullException("restoredWorld");
            CanonicalModel model = CanonicalTradeCodec.Decode(bytes);
            if (model.MatchScope != expectedScope || model.MatchTerm != expectedTerm)
                throw new InvalidDataException("Wrong match identity");
            VerifyWorld(model, restoredWorld, false);
            if (model.Day != restoredWorld.Day)
                throw new InvalidDataException("Native/canonical save day mismatch");
            bool interrupted = model.Archive.PendingNativeId != 0 || model.Mailbox.Intents.Any(x => x.State == TradeIntentStatus.Processing) || model.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative);
            if (interrupted)
            {
                model.Quarantined = true;
                foreach (TradeRecord record in model.Archive.Records)
                    if (record.State == TradeDisposition.PreparingNative)
                        record.State = TradeDisposition.Quarantined;
                model.Archive.PendingNativeId = 0;
            }

            // Rebase only the engine cursor. Saved Clock/DueClock stay unchanged,
            // so pending requests retain their exact remaining simulation ticks.
            model.Mailbox.RestoreAfterLoad(restoredWorld.SharedTick);
            model.LastSharedTick = restoredWorld.SharedTick;
            model.Revision = checked(model.Revision + 1);
            return new CanonicalTradeReducer(model);
        }

        internal byte[] SaveBytes()
        {
            return (byte[])_save.Clone();
        }

        internal long Revision
        {
            get
            {
                return _model.Revision;
            }
        }

        internal bool Quarantined
        {
            get
            {
                return _model.Quarantined;
            }
        }

        internal TradeHistoryArchive ArchiveSnapshot()
        {
            return _model.Archive.Copy();
        }

        internal TradeIntentMailbox MailboxSnapshot()
        {
            return _model.Mailbox.Copy();
        }

        private static byte Bit(byte player)
        {
            return (byte)(1 << (player - 1));
        }

        private static bool Human(CanonicalModel m, byte player)
        {
            return player >= 1 && player <= 8 && (m.HumanMask & Bit(player)) != 0;
        }

        private static bool Member(CanonicalModel m, byte player)
        {
            return player >= 1 && player <= 8 && (m.RosterMask & Bit(player)) != 0;
        }

        internal static int Seed(Guid scope, ulong term, string map, string roster)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(scope.ToByteArray());
                writer.Write(term);
                writer.Write(map);
                writer.Write(roster);
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream.ToArray());
                    int seed = (hash[0] | hash[1] << 8 | hash[2] << 16 | hash[3] << 24) & int.MaxValue;
                    return seed == 0 ? 1 : seed;
                }
            }
        }

        private static void VerifyWorld(CanonicalModel model, CanonicalTradeSnapshot world, bool advancing)
        {
            if (world.MapFingerprint != model.Archive.MapFingerprint || world.RosterFingerprint != model.Archive.RosterFingerprint)
                throw new InvalidDataException("Changed world fingerprint");
            byte roster = 0, humans = 0;
            foreach (CanonicalPlayer p in world.Players())
            {
                roster |= Bit(p.Slot);
                if (p.Human)
                    humans |= Bit(p.Slot);
            }

            if (roster != model.RosterMask || humans != model.HumanMask)
                throw new InvalidDataException("Changed player identity/classification");
            if (advancing && (world.SharedTick <= model.LastSharedTick || world.Day < model.Day))
                throw new InvalidDataException("Nonmonotonic shared world");
        }

        // Called once per authoritative shared tick, under the engine monitor.
        // At most one due request settles per tick. This ensures every settlement
        // gets a fresh snapshot after all earlier native settlements.
        internal PreparedCanonicalTransition AdvanceSharedTick(long sharedTick, AuthenticatedTradeTick input, ICanonicalTradeSnapshotSource snapshots)
        {
            // Persisted HumanMask survives Cold-Load when the transient 
            // MP-session bit cannot; multi-human Native MUST stay closed.
            if (_pendingWorldPublication != null || _mpNoNativeSession || (_model.HumanMask & (_model.HumanMask - 1)) != 0)
                throw new InvalidOperationException(" multi-human state prohibits legacy native prepare");
            if (_prepared != null)
                throw new InvalidOperationException("Resolve prepared transition first");
            if (input == null || snapshots == null)
                throw new ArgumentNullException();
            CanonicalTradeSnapshot world = snapshots.Capture(sharedTick);
            if (world == null || world.SharedTick != sharedTick)
                throw new InvalidOperationException("Snapshot is not this shared tick");
            VerifyWorld(_model, world, true);
            CanonicalModel next = _model.Copy();
            next.LastSharedTick = sharedTick;
            next.Mailbox.Observe(sharedTick);
            next.Revision = checked(next.Revision + 1);
            AdvanceDay(next, world.Day);
            Activate(next, input.Activation);
            ExpireHumanOffers(next);
            CompactTerminalHumanOffers(next);
            ProcessAiConsentReplies(next, world);
            OrderedHumanTrade[] commands = input.Requests();
            for (int i = 0; i < commands.Length; i++)
                Receive(next, world, commands[i], input.TrustedAt(i));
            if (!next.Quarantined)
            {
                ScheduleAutonomous(next, world);
                TradeIntent due = next.Mailbox.Due().FirstOrDefault();
                if (due != null)
                {
                    PreparedCanonicalTransition transition = ProcessDue(next, world, due);
                    if (transition != null)
                    {
                        // PreparingNative/Processing is a prebuilt recoverable
                        // before-state, not a grant of successful goodwill.
                        _model = next;
                        _save = transition.BeforeState();
                        _prepared = transition;
                        return transition;
                    }
                }

                // One transition per globally ordered game tick. A signed
                // human acceptance and a due AI request compete for the SAME
                // native journal slot and original-engine monitor.
                PreparedCanonicalTransition human = PrepareAcceptedHuman(next, world);
                if (human != null)
                {
                    _model = next;
                    _save = human.BeforeState();
                    _prepared = human;
                    return human;
                }
            }

            Publish(next);
            return null;
        }

        private static void Activate(CanonicalModel model, SignedTradeActivation activation)
        {
            if (activation == null)
                return;
            if (activation.Epoch == 0 || activation.Epoch <= model.InputEpoch)
                throw new InvalidDataException("Stale signed activation");
            model.InputEpoch = activation.Epoch;
            Array.Clear(model.InputSequence, 0, model.InputSequence.Length);
            Array.Clear(model.InputNonce, 0, model.InputNonce.Length);
        }

        private static void AdvanceDay(CanonicalModel model, int day)
        {
            if (day < model.Day)
                throw new InvalidDataException("Day moved backwards");
            foreach (TradeRelationship relation in model.Archive.Relationships)
            {
                if ((long)day - relation.LastRejectedDay > 3)
                    relation.RejectionStreak = 0;
                // Equivalent to bounded daily recovery for skipped days.
                long first = Math.Max((long)model.Day + 1, (long)relation.LastRejectedDay + 6);
                long recovery = Math.Max(0L, (long)day - first + 1);
                if (relation.Goodwill < 50)
                    relation.Goodwill += (int)Math.Min(50 - relation.Goodwill, recovery);
            }

            model.Day = day;
            foreach (TradeRecord record in model.Archive.Records)
                if (record.State == TradeDisposition.PendingCounter && record.ExpiryDay < day)
                    record.State = TradeDisposition.Cancelled;
        }

        private static bool Busy(CanonicalModel model, byte a, byte b)
        {
            if (HumanPairBusy(model, a, b))
                return true;
            int pair = TradeHistoryArchive.PairKey(a, b);
            return model.Mailbox.HasPending(a, b) || HumanPairBusy(model, a, b) || model.Archive.Records.Any(x => x.State == TradeDisposition.PendingCounter && x.ExpiryDay >= model.Day && TradeHistoryArchive.PairKey(x.Requester, x.Recipient) == pair);
        }

        private static bool CanReserve(CanonicalModel model)
        {
            // Reserve room for two records per active request and future AI
            // counter responses. Never prune receipts needed by counter roots.
            int active = model.Mailbox.Intents.Count(x => x.State == TradeIntentStatus.Waiting || x.State == TradeIntentStatus.Processing);
            return model.Archive.NextId < ulong.MaxValue - 256 && model.Archive.Records.Count + 2 * (active + 1) <= TradeHistoryArchive.MaximumRecords && active < TradeIntentMailbox.MaximumPending;
        }

        private static ulong Reserve(CanonicalModel model)
        {
            ulong id = model.Archive.NextId;
            model.Archive.NextId = checked(id + 4);
            return id;
        }

        private static TradeIntent NewIntent(CanonicalModel model, byte requester, byte recipient, ushort offered, ushort wanted, int amount, byte tier, bool ai, TradeIntentAction action, CanonicalTradeSnapshot originalWorld)
        {
            ulong id = Reserve(model);
            long clock = model.Mailbox.Clock;
            return new TradeIntent
            {
                Id = id,
                Requester = requester,
                Recipient = recipient,
                OfferedGood = offered,
                RequestedGood = wanted,
                RequestedAmount = amount,
                Generosity = tier,
                IssuedDay = model.Day,
                AiToAi = ai,
                Action = action,
                State = TradeIntentStatus.Waiting,
                EnqueuedClock = clock,
                DueClock = checked(clock + CanonicalObservedReplyDelay(model, originalWorld, id, requester, recipient, amount, action)),
                ExpiryClock = checked(clock + TradeIntentMailbox.MaximumLifetimeTicks)
            };
        }

        private static void BindCounter(TradeIntent intent, TradeRecord c)
        {
            intent.CounterId = c.Id;
            intent.CounterRootId = c.RootId;
            intent.CounterOfferedAmount = c.OfferedAmount;
            intent.CounterCreatedDay = c.IssuedDay;
            intent.CounterExpiresDay = c.ExpiryDay;
        }

        private static void Receive(CanonicalModel model, CanonicalTradeSnapshot world, OrderedHumanTrade request, CanonicalTrustedHumanInput proof)
        {
            if (!Human(model, request.Requester) || model.InputEpoch == 0 || request.Epoch != model.InputEpoch || !VerifiedHumanActor(world, request, proof))
                return;
            byte player = request.Requester;
            if (model.InputSequence[player] == ulong.MaxValue || request.Sequence != model.InputSequence[player] + 1 || request.ClientNonce == 0 || request.ClientNonce <= model.InputNonce[player])
                return;
            // Authenticated ordered input is consumed even if its game request
            // is rejected. An invalid game request cannot be replayed later.
            model.InputSequence[player] = request.Sequence;
            model.InputNonce[player] = request.ClientNonce;
            if (request.Action >= CanonicalTradeAction.ProposeHuman && request.Action <= CanonicalTradeAction.WithdrawHuman)
            {
                ReceiveHumanConsent(model, world, request, proof);
                return;
            }

            if (model.Quarantined || !Member(model, request.Recipient) || Human(model, request.Recipient) || request.Requester == request.Recipient || !world.Player(request.Requester).Alive || !world.Player(request.Recipient).Alive || request.Tier > 2 || request.Amount < 1 || request.Amount > 100000 || !Enum.IsDefined(typeof(CanonicalTradeAction), request.Action) || request.OfferedGood != 0 && GoodCatalog.Find(request.OfferedGood) == null || request.WantedGood != 0 && GoodCatalog.Find(request.WantedGood) == null || request.OfferedGood != 0 && request.OfferedGood == request.WantedGood || !CanReserve(model))
                return;
            ObservedTradePairState pair;
            if (!world.TryGetPair(request.Requester, request.Recipient, model.Day, out pair))
                return;
            if (request.Action == CanonicalTradeAction.Propose)
            {
                if (request.CounterId != 0 || request.CounterRootId != 0 || request.CounterOfferedAmount != 0 || request.CounterCreatedDay != 0 || request.CounterExpiresDay != 0 || Busy(model, request.Requester, request.Recipient))
                    return;
                TradeIntent intent = NewIntent(model, request.Requester, request.Recipient, request.OfferedGood, request.WantedGood, request.Amount, request.Tier, false, TradeIntentAction.Propose, world);
                model.Mailbox.Enqueue(intent, model.Day);
                return;
            }

            if (model.Mailbox.HasPending(request.Requester, request.Recipient))
                return;
            TradeRecord counter = model.Archive.Record(request.CounterId);
            if (counter == null || counter.State != TradeDisposition.PendingCounter || counter.AiToAi || counter.Requester != request.Requester || counter.Recipient != request.Recipient || counter.ExpiryDay < model.Day)
                return;
            // Compare signed input terms before allocating an ID or copying
            // canonical terms into the delayed intent.
            var consent = new TradeIntent
            {
                Action = request.Action == CanonicalTradeAction.AcceptCounter ? TradeIntentAction.AcceptCounter : TradeIntentAction.DeclineCounter,
                Requester = request.Requester,
                Recipient = request.Recipient,
                OfferedGood = request.OfferedGood,
                RequestedGood = request.WantedGood,
                RequestedAmount = request.Amount,
                Generosity = request.Tier,
                CounterId = request.CounterId,
                CounterRootId = request.CounterRootId,
                CounterOfferedAmount = request.CounterOfferedAmount,
                CounterCreatedDay = request.CounterCreatedDay,
                CounterExpiresDay = request.CounterExpiresDay,
                AiToAi = false
            };
            if (!consent.MatchesCounter(counter))
                return;
            TradeIntent answer = NewIntent(model, counter.Requester, counter.Recipient, counter.OfferedGood, counter.RequestedGood, counter.RequestedAmount, counter.Generosity, false, consent.Action, world);
            BindCounter(answer, counter);
            model.Mailbox.Enqueue(answer, model.Day);
        }

        private static ManagedTradeRelationship ToNatural(TradeRelationship r)
        {
            return new ManagedTradeRelationship
            {
                A = r.A,
                B = r.B,
                Goodwill = r.Goodwill,
                ConsecutiveRejected = r.RejectionStreak,
                LastRejectedDay = r.LastRejectedDay,
                LastAttemptDay = r.LastAttemptDay,
                LastPenaltyDay = r.LastPenaltyDay,
                RejectedToday = r.RejectedToday,
                AcceptedTrades = r.AcceptedTrades,
                LastDealDay = r.LastDealDay
            };
        }

        private static void InitializePair(CanonicalModel model, CanonicalTradeSnapshot snapshot, byte a, byte b)
        {
            if (model.Archive.Relationships.Any(r => TradeHistoryArchive.PairKey(r.A, r.B) == TradeHistoryArchive.PairKey(a, b)))
                return;
            ObservedTradePairState pair;
            if (!snapshot.TryGetPair(a, b, model.Day, out pair))
                return;
            model.Archive.Relation(a, b).Goodwill = pair.Goodwill;
        }

        private static DiplomaticTradeNegotiator BuildWorld(CanonicalModel model, CanonicalTradeSnapshot snapshot, bool aiOnly, out ManagedGoodsSnapshotPort memory)
        {
            memory = snapshot.CloneStocks();
            var world = new DiplomaticTradeNegotiator(memory, new ObservedTradeMarketSnapshot(snapshot.Prices()), snapshot.Rulers(aiOnly), model.MatchScope, model.MatchTerm, model.Day, model.WorldSeed, model.Archive.NextId, snapshot);
            world.Relationships.Restore(model.Archive.Relationships.Where(r => !aiOnly || !Human(model, r.A) && !Human(model, r.B)).Select(ToNatural));
            return world;
        }

        private static void ScheduleAutonomous(CanonicalModel model, CanonicalTradeSnapshot snapshot)
        {
            if (model.Archive.LastAutonomousDay == model.Day)
                return;
            model.Archive.LastAutonomousDay = model.Day;
            // Read and amend ONE budget derived from the canonical saved owner.
            // A new Lord/Gold/Market/Pending proposal requires this same original
            // snapshot; neither route can bypass the other's daily allowance.
            CanonicalAiInitiativeBudget budget = SavedAiBudget(model,snapshot);
            ScheduleAiHumanOffers(model, snapshot,budget);
            byte[] ais = snapshot.Players().Where(p => !p.Human).Select(p => p.Slot).ToArray();
            if (ais.Length < 2)
                return;
            for (int i = 0; i < ais.Length; i++)
                for (int j = i + 1; j < ais.Length; j++)
                    InitializePair(model, snapshot, ais[i], ais[j]);
            ManagedGoodsSnapshotPort memory;
            DiplomaticTradeNegotiator world = BuildWorld(model, snapshot, false, out memory);
            // The merged roster-aware planner requires the complete real human
            // set and excludes all of them from both AI-to-AI trade parties.
            AutonomousTradeProposal[] plans = world.PlanAutonomousAiTrade(snapshot.Players().Where(p => p.Human).Select(p => p.Slot));
#if DIPLOMACY_PRIVATE_TESTS
            // inspect returned REAL plan and detached original data once.
            // No second PlanAutonomousAiTrade, IDs, writes or native reads.
            try
            {
                PrivateAutonomousBarterPlanDiagnostic.ObserveAfterOriginalPlan(
                    model, snapshot, memory, plans);
            }
            catch (Exception)
            {
                // Optional diagnostic can never affect enqueue/commit/rollback.
            }
#endif

            foreach (AutonomousTradeProposal plan in plans.OrderBy(x => TradeHistoryArchive.PairKey(x.Requester, x.Recipient)).ThenBy(x => x.Requester).ThenBy(x => x.RequestedGood).ThenBy(x => x.OfferedGood))
            {
                if (!Member(model, plan.Requester) || !Member(model, plan.Recipient) || Human(model, plan.Requester) || Human(model, plan.Recipient) || Busy(model, plan.Requester, plan.Recipient) || !CanReserve(model))
                    continue;
                ObservedTradePairState pair;
                if (!snapshot.TryGetPair(plan.Requester, plan.Recipient, model.Day, out pair))
                    continue;
                // The old AI planner supplies candidates. Original AI economic
                // observations decide whether to enqueue any of them; the
                // existing native settlement and AI consent remain untouched.
                int requested = Math.Min(plan.RequestedAmount,
                    AiMaximumDraftQuantity(snapshot,plan.Requester,plan.RequestedGood));
                if (requested < 1)
                    continue;
                int offered = world.QuoteOfferedAmount(plan.OfferedGood,
                    plan.RequestedGood,requested,plan.Generosity);
                if (offered < 1)
                    continue;
                var check = new BarterQuote
                {
                    MapSession = model.MatchScope, HostTerm = model.MatchTerm,
                    Revision = 1, Id = model.Archive.NextId,
                    Requester = plan.Requester, Recipient = plan.Recipient,
                    OfferedGood = plan.OfferedGood, RequestedGood = plan.RequestedGood,
                    OfferedAmount = offered, RequestedAmount = requested,
                    Day = model.Day, ExpiryDay = model.Day + 5
                };
                ExactTradeCell[] exact;
                if (!TryCells(snapshot,check,out exact) ||
                    !world.IsMutuallyBeneficial(check))
                    continue;
                AiProcurementDecision decision;
                if (!CanScheduleAiEconomicCandidate(model,snapshot,budget,
                    plan.Requester,plan.Recipient,plan.OfferedGood,
                    plan.RequestedGood,requested,offered,out decision))
                    continue;
                TradeIntent intent = NewIntent(model, plan.Requester, plan.Recipient,
                    plan.OfferedGood, plan.RequestedGood, requested,
                    (byte)plan.Generosity, true, TradeIntentAction.Propose, snapshot);
                model.Mailbox.Enqueue(intent, model.Day);
                budget.ObserveNewlySaved(new AiInitiativeFact(intent.Id,
                    plan.Requester,plan.Recipient,model.Day,requested,false));
            }
        }

        private static void Finish(TradeIntent intent, string outcome)
        {
            intent.State = TradeIntentStatus.Finished;
            intent.Outcome = outcome;
        }

        private static BarterQuote Quote(CanonicalModel model, TradeRecord record)
        {
            return new BarterQuote
            {
                MapSession = model.MatchScope,
                HostTerm = model.MatchTerm,
                Revision = 1,
                Id = record.Id,
                RootId = record.RootId,
                Requester = record.Requester,
                Recipient = record.Recipient,
                OfferedGood = record.OfferedGood,
                RequestedGood = record.RequestedGood,
                OfferedAmount = record.OfferedAmount,
                RequestedAmount = record.RequestedAmount,
                Day = record.IssuedDay,
                ExpiryDay = record.ExpiryDay
            };
        }

        private static TradeRecord Record(BarterQuote quote, TradeIntent intent, TradeDisposition state, int chance, int roll)
        {
            return new TradeRecord
            {
                Id = quote.Id,
                RootId = quote.RootId,
                Requester = quote.Requester,
                Recipient = quote.Recipient,
                OfferedGood = quote.OfferedGood,
                RequestedGood = quote.RequestedGood,
                OfferedAmount = quote.OfferedAmount,
                RequestedAmount = quote.RequestedAmount,
                IssuedDay = quote.Day,
                ExpiryDay = quote.ExpiryDay,
                Generosity = intent.Generosity,
                State = state,
                AiToAi = intent.AiToAi,
                ChanceBasisPoints = chance,
                RollBasisPoints = roll
            };
        }

        private PreparedCanonicalTransition ProcessDue(CanonicalModel model, CanonicalTradeSnapshot snapshot, TradeIntent intent)
        {
            ObservedTradePairState pair;
            if (intent.ExpiryClock < model.Mailbox.Clock || Human(model, intent.Recipient) || intent.AiToAi && Human(model, intent.Requester) || !intent.AiToAi && !Human(model, intent.Requester) || !snapshot.TryGetPair(intent.Requester, intent.Recipient, model.Day, out pair))
            {
                CancelBoundCounter(model, intent);
                Finish(intent, "expired_or_ineligible");
                return null;
            }

            InitializePair(model, snapshot, intent.Requester, intent.Recipient);
            TradeRelationship relation = model.Archive.Relation(intent.Requester, intent.Recipient);
            if (!TradeHistoryArchive.CanBookAnotherTrade(relation.AcceptedTrades))
            {
                CancelBoundCounter(model, intent);
                Finish(intent, "relationship_capacity");
                return null;
            }

            ManagedGoodsSnapshotPort memory;
            DiplomaticTradeNegotiator world = BuildWorld(model, snapshot, false, out memory);
            if (intent.Action != TradeIntentAction.Propose)
            {
                TradeRecord counter = model.Archive.Record(intent.CounterId);
                TradeRecord root = counter == null ? null : model.Archive.Record(counter.RootId);
                if (!intent.MatchesCounter(counter) || counter.State != TradeDisposition.PendingCounter || counter.ExpiryDay < model.Day || root == null || root.State != TradeDisposition.Superseded || counter.Id != counter.RootId + 1 || intent.Action == TradeIntentAction.EvaluateAiCounter && (!intent.AiToAi || Human(model, counter.Requester)) || intent.Action != TradeIntentAction.EvaluateAiCounter && (intent.AiToAi || !Human(model, counter.Requester)))
                {
                    CancelBoundCounter(model, intent);
                    Finish(intent, "counter_invalid");
                    return null;
                }

                if (intent.Action == TradeIntentAction.DeclineCounter)
                {
                    TradeRelationshipRules.DeclineCounter(relation, model.Day);
                    counter.State = TradeDisposition.Cancelled;
                    Finish(intent, "counter_declined");
                    return null;
                }

                BarterQuote quote = Quote(model, counter);
                // No dependency on NaturalBarter's private pending dictionary.
                bool fair = world.IsMutuallyBeneficial(quote);
                bool aiConsent = intent.Action != TradeIntentAction.EvaluateAiCounter || world.CanAcceptAiCounter(quote);
                if (!fair || !aiConsent)
                {
                    if (intent.AiToAi)
                        TradeRelationshipRules.DeclineCounter(relation, model.Day);
                    counter.State = TradeDisposition.Cancelled;
                    Finish(intent, "counter_no_longer_acceptable");
                    return null;
                }

                return Prepare(model, snapshot, intent, quote, counter);
            }

            NegotiatedTradeState result = world.Propose(intent.Requester, intent.Recipient, intent.OfferedGood, intent.RequestedGood, intent.RequestedAmount, (TradeOfferGenerosity)intent.Generosity, intent.Id, 0, intent.AiToAi);
            if (result.Status == DiplomaticTradeOutcome.Accepted && result.Quote != null)
            {
                // The simulation has already credited its own relationship.
                // Deliberately do not adopt it.
                TradeRecord receipt = Record(result.Quote, intent, TradeDisposition.PreparingNative, result.ChanceBasisPoints, result.RollBasisPoints);
                model.Archive.Add(receipt);
                return Prepare(model, snapshot, intent, result.Quote, model.Archive.Record(receipt.Id));
            }

            if (result.Status == DiplomaticTradeOutcome.CounterOffer && result.Quote != null && result.CounterOffer != null)
            {
                if (result.Quote.Id != intent.Id || result.CounterOffer.RootId != intent.Id || result.CounterOffer.Id != intent.Id + 1)
                    throw new InvalidDataException("Noncanonical counter identity");
                model.Archive.Add(Record(result.Quote, intent, TradeDisposition.Superseded, result.ChanceBasisPoints, result.RollBasisPoints));
                TradeRecord counter = Record(result.CounterOffer, intent, TradeDisposition.PendingCounter, result.ChanceBasisPoints, result.RollBasisPoints);
                model.Archive.Add(counter);
                relation.LastAttemptDay = model.Day;
                Finish(intent, "counter_issued");
                if (intent.AiToAi && CanReserve(model))
                {
                    TradeIntent response = NewIntent(model, counter.Requester, counter.Recipient, counter.OfferedGood, counter.RequestedGood, counter.RequestedAmount, counter.Generosity, true, TradeIntentAction.EvaluateAiCounter, snapshot);
                    BindCounter(response, counter);
                    model.Mailbox.Enqueue(response, model.Day);
                }

                return null;
            }

            if (result.Status == DiplomaticTradeOutcome.Rejected)
            {
                // Apply canonical  effects; never copy simulation goodwill.
                TradeRelationshipRules.DeclineCounter(relation, model.Day);
                if (result.Quote != null)
                    model.Archive.Add(Record(result.Quote, intent, TradeDisposition.Rejected, result.ChanceBasisPoints, result.RollBasisPoints));
                Finish(intent, "rejected");
            }
            else
            {
                relation.LastAttemptDay = model.Day;
                Finish(intent, "blocked");
            }

            return null;
        }

        private static void CancelBoundCounter(CanonicalModel model, TradeIntent intent)
        {
            if (intent.Action == TradeIntentAction.Propose)
                return;
            TradeRecord counter = model.Archive.Record(intent.CounterId);
            if (intent.MatchesCounter(counter) && counter.State == TradeDisposition.PendingCounter)
                counter.State = TradeDisposition.Cancelled;
        }

        private PreparedCanonicalTransition Prepare(CanonicalModel model, CanonicalTradeSnapshot snapshot, TradeIntent intent, BarterQuote quote, TradeRecord record)
        {
            ExactTradeCell[] cells;
            if (!TryCells(snapshot, quote, out cells))
            {
                record.State = TradeDisposition.Cancelled;
                Finish(intent, "stock_or_capacity_changed");
                return null;
            }

            record.State = TradeDisposition.PreparingNative;
            model.Archive.PendingNativeId = quote.Id;
            intent.State = TradeIntentStatus.Processing;
            intent.Outcome = "prepared_native";
            CanonicalModel success = model.Copy();
            success.Revision = checked(success.Revision + 1);
            success.Archive.PendingNativeId = 0;
            success.Archive.Record(quote.Id).State = TradeDisposition.Committed;
            TradeRelationshipRules.CommitSuccessfulTrade(success.Archive.Relation(quote.Requester, quote.Recipient), success.Day);
            Finish(success.Mailbox.Intents.Single(x => x.Id == intent.Id), "committed");
            CanonicalModel failure = model.Copy();
            failure.Revision = checked(failure.Revision + 1);
            failure.Archive.PendingNativeId = 0;
            failure.Archive.Record(quote.Id).State = TradeDisposition.Cancelled;
            Finish(failure.Mailbox.Intents.Single(x => x.Id == intent.Id), "native_rejected_or_exactly_rolled_back");
            CanonicalModel quarantine = failure.Copy();
            quarantine.Quarantined = true;
            quarantine.Archive.Record(quote.Id).State = TradeDisposition.Quarantined;
            Finish(quarantine.Mailbox.Intents.Single(x => x.Id == intent.Id), "ambiguous_native_quarantine");
            return new PreparedCanonicalTransition(_identity, model, success, failure, quarantine, quote, cells);
        }

        private static bool TryCells(CanonicalTradeSnapshot snapshot, BarterQuote quote, out ExactTradeCell[] cells)
        {
            cells = null;
            if (quote.Requester == quote.Recipient || quote.OfferedGood == quote.RequestedGood || quote.OfferedAmount < 1 || quote.OfferedAmount > 100000 || quote.RequestedAmount < 1 || quote.RequestedAmount > 100000)
                return false;
            byte[] players =
            {
                quote.Requester,
                quote.Recipient,
                quote.Recipient,
                quote.Requester
            };
            ushort[] goods =
            {
                quote.OfferedGood,
                quote.OfferedGood,
                quote.RequestedGood,
                quote.RequestedGood
            };
            int[] delta =
            {
                -quote.OfferedAmount,
                quote.OfferedAmount,
                -quote.RequestedAmount,
                quote.RequestedAmount
            };
            var result = new ExactTradeCell[4];
            var unique = new HashSet<long>();
            for (int i = 0; i < 4; i++)
            {
                if (!unique.Add(ManagedGoodsSnapshotPort.Key(players[i], goods[i])))
                    return false;
                StockCell old = snapshot.Read(players[i], goods[i]);
                if (old == null)
                    return false;
                old.Validate();
                long after = (long)old.Quantity + delta[i];
                if (after < 0 || after > old.Capacity || delta[i] < 0 && after < old.Reserve())
                    return false;
                result[i] = new ExactTradeCell(players[i], goods[i], old, (int)after);
            }

            cells = result;
            return true;
        }

        private void Guard(PreparedCanonicalTransition transition, long sharedTick)
        {
            if (transition == null || !ReferenceEquals(_prepared, transition) || !transition.OwnedBy(_identity) || transition.SourceRevision != _model.Revision || transition.SharedTick != sharedTick || _model.LastSharedTick != sharedTick || _model.Archive.PendingNativeId != transition.TradeId)
                throw new InvalidOperationException("Stale/repeated/foreign transition");
        }

        // Only the runtime adapter calls these methods, while retaining the
        // original engine monitor. Kernel and adapter belong to the same assembly.
        internal void CommitPrepared(PreparedCanonicalTransition transition, long sharedTick, ICanonicalTradeNativePort native)
        {
            Guard(transition, sharedTick);
            if (native == null)
                throw new ArgumentNullException("native");
            foreach (ExactTradeCell cell in transition.Cells())
            {
                StockCell observed = native.Read(cell.Player, cell.Good);
                if (observed == null || observed.Quantity != cell.AfterQuantity)
                    throw new InvalidOperationException("Exact four-cell readback failed");
                observed.Validate();
            }

            _model = transition.PrebuiltAfter;
            _save = transition.PrebuiltAfterBytes;
            _prepared = null;
        }

        internal void RejectPrepared(PreparedCanonicalTransition transition, long sharedTick, ICanonicalTradeNativePort native, bool rollbackAmbiguous)
        {
            Guard(transition, sharedTick);
            bool ambiguous = rollbackAmbiguous;
            if (!ambiguous)
            {
                try
                {
                    if (native == null)
                        ambiguous = true;
                    else
                        foreach (ExactTradeCell cell in transition.Cells())
                        {
                            StockCell observed = native.Read(cell.Player, cell.Good);
                            if (observed == null || observed.Quantity != cell.BeforeQuantity)
                            {
                                ambiguous = true;
                                break;
                            }

                            observed.Validate();
                        }
                }
                catch
                {
                    ambiguous = true;
                }
            }

            _model = ambiguous ? transition.PrebuiltQuarantine : transition.PrebuiltFailure;
            _save = ambiguous ? transition.PrebuiltQuarantineBytes : transition.PrebuiltFailureBytes;
            _prepared = null;
        }

        private void Publish(CanonicalModel next)
        {
            byte[] bytes = CanonicalTradeCodec.Encode(next);
            _model = next;
            _save = bytes;
        }
    }

    internal static partial class CanonicalTradeCodec
    {
        private const uint Magic = 0x43544941; // AITC
        private const int Version = 3;
        private const int MaximumBytes = 512 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static void Validate(CanonicalModel model)
        {
            if (model == null || model.Revision < 1 || model.LastSharedTick < 0 || model.Day < 1 || model.Day > int.MaxValue - 32 || model.MatchScope == Guid.Empty || model.MatchTerm == 0 || model.WorldSeed == 0 || model.Archive == null || model.Mailbox == null || model.RosterMask == 0 || (model.HumanMask & ~model.RosterMask) != 0 || model.InputSequence == null || model.InputSequence.Length != 9 || model.InputNonce == null || model.InputNonce.Length != 9 || model.InputSequence[0] != 0 || model.InputNonce[0] != 0)
                throw new InvalidDataException("Invalid canonical header");
            if (model.WorldSeed != CanonicalTradeReducer.Seed(model.MatchScope, model.MatchTerm, model.Archive.MapFingerprint, model.Archive.RosterFingerprint))
                throw new InvalidDataException("Changed stable world seed");
            for (byte player = 1; player <= 8; player++)
            {
                bool human = (model.HumanMask & (1 << (player - 1))) != 0;
                if ((!human || model.InputEpoch == 0) && (model.InputSequence[player] != 0 || model.InputNonce[player] != 0) || (model.InputSequence[player] == 0) != (model.InputNonce[player] == 0))
                    throw new InvalidDataException("Invalid human input high-water");
            }

            model.Archive.Validate(model.Archive.MapFingerprint, model.Archive.RosterFingerprint, model.Day);
            model.Mailbox.Validate(model.Archive.MapFingerprint, model.Archive.RosterFingerprint, model.Day, model.Archive);
            foreach (TradeRelationship relation in model.Archive.Relationships)
                RequireMembers(model, relation.A, relation.B);
            ValidateHumanOffers(model);
            foreach (TradeRecord record in model.Archive.Records)
            {
                RequireMembers(model, record.Requester, record.Recipient);
                bool requesterHuman = IsHuman(model, record.Requester);
                if (IsCanonicalHumanReceipt(model, record))
                    continue;
                if (IsHuman(model, record.Recipient))
                {
                    throw new InvalidDataException("Unapproved human recipient receipt");
                }

                if (record.AiToAi == requesterHuman || record.RootId != 0 && record.Id != record.RootId + 1)
                    throw new InvalidDataException("Invalid AI trade authority");
            }

            foreach (TradeIntent intent in model.Mailbox.Intents)
            {
                RequireMembers(model, intent.Requester, intent.Recipient);
                if (IsHuman(model, intent.Recipient) || intent.AiToAi == IsHuman(model, intent.Requester) || intent.Action == TradeIntentAction.EvaluateAiCounter && !intent.AiToAi || intent.Action != TradeIntentAction.Propose && intent.Action != TradeIntentAction.EvaluateAiCounter && intent.AiToAi || intent.State == TradeIntentStatus.Waiting && intent.DueClock - intent.EnqueuedClock < NegotiationTiming.MinimumTicks || intent.State == TradeIntentStatus.Waiting && intent.DueClock - intent.EnqueuedClock > NegotiationTiming.MaximumTicks)
                    throw new InvalidDataException("Invalid intent authority/delay");
            }

            // A saved active counter blocks the same unordered pair even when
            // there is currently no mailbox intent for it.
            var counters = new HashSet<int>();
            foreach (TradeRecord counter in model.Archive.Records.Where(x => x.State == TradeDisposition.PendingCounter && x.ExpiryDay >= model.Day))
                if (!counters.Add(TradeHistoryArchive.PairKey(counter.Requester, counter.Recipient)))
                    throw new InvalidDataException("Duplicate pending counter pair");
        }

        private static bool IsHuman(CanonicalModel m, byte p)
        {
            return (m.HumanMask & (1 << (p - 1))) != 0;
        }

        private static void RequireMembers(CanonicalModel m, byte a, byte b)
        {
            if (a < 1 || a > 8 || b < 1 || b > 8 || a == b || (m.RosterMask & (1 << (a - 1))) == 0 || (m.RosterMask & (1 << (b - 1))) == 0)
                throw new InvalidDataException("Trade outside canonical roster");
        }

        internal static byte[] Encode(CanonicalModel model)
        {
            Validate(model);
            byte[] payload;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(model.Revision);
                writer.Write(model.LastSharedTick);
                writer.Write(model.Day);
                writer.Write(model.WorldSeed);
                writer.Write(model.MatchScope.ToByteArray());
                writer.Write(model.MatchTerm);
                writer.Write(model.InputEpoch);
                writer.Write(model.Quarantined);
                writer.Write(model.RosterMask);
                writer.Write(model.HumanMask);
                for (int p = 1; p <= 8; p++)
                {
                    writer.Write(model.InputSequence[p]);
                    writer.Write(model.InputNonce[p]);
                }

                WriteArchive(writer, model.Archive);
                TradeIntentMailboxCodec.Write(writer, model.Mailbox, model.Archive.MapFingerprint, model.Archive.RosterFingerprint, model.Day, model.Archive);
                WriteHumanOffers(writer, model.HumanOffers);
                writer.Flush();
                payload = stream.ToArray();
            }

            if (payload.Length > MaximumBytes - 32)
                throw new InvalidDataException("Canonical save too large");
            byte[] result = new byte[payload.Length + 32];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] checksum = sha.ComputeHash(payload);
                Buffer.BlockCopy(checksum, 0, result, payload.Length, 32);
            }

            return result;
        }

        internal static CanonicalModel Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 64 || bytes.Length > MaximumBytes)
                throw new InvalidDataException("Invalid canonical save size");
            int length = bytes.Length - 32;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] checksum = sha.ComputeHash(bytes, 0, length);
                int difference = 0;
                for (int i = 0; i < 32; i++)
                    difference |= checksum[i] ^ bytes[length + i];
                if (difference != 0)
                    throw new InvalidDataException("Canonical checksum mismatch");
            }

            using (var stream = new MemoryStream(bytes, 0, length, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("Unsupported canonical magic");
                int oldVersion = reader.ReadInt32();
                if (oldVersion < 1 || oldVersion > Version)
                    throw new InvalidDataException("Unsupported canonical schema");
                var model = new CanonicalModel
                {
                    Revision = reader.ReadInt64(),
                    LastSharedTick = reader.ReadInt64(),
                    Day = reader.ReadInt32(),
                    WorldSeed = reader.ReadInt32(),
                    MatchScope = new Guid(ReadExact(reader, 16)),
                    MatchTerm = reader.ReadUInt64(),
                    InputEpoch = reader.ReadUInt64(),
                    Quarantined = ReadBool(reader),
                    RosterMask = reader.ReadByte(),
                    HumanMask = reader.ReadByte()
                };
                for (int p = 1; p <= 8; p++)
                {
                    model.InputSequence[p] = reader.ReadUInt64();
                    model.InputNonce[p] = reader.ReadUInt64();
                }

                model.Archive = ReadArchive(reader, model.Day);
                model.Mailbox = TradeIntentMailboxCodec.ReadExtended(reader, model.Archive.MapFingerprint, model.Archive.RosterFingerprint, model.Day, model.Archive, true);
                // legacy had only  archive +  mailbox; upgrading it creates
                // an empty human-consent ledger WITHOUT a second mailbox.
                if (oldVersion >= 2)
                    ReadHumanOffers(reader, model, oldVersion);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Trailing canonical payload");
                Validate(model);
                return model;
            }
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new EndOfStreamException();
            return bytes;
        }

        private static bool ReadBool(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1)
                throw new InvalidDataException("Invalid Boolean");
            return value == 1;
        }

        private static string Text(BinaryReader reader, int maximum)
        {
            // Strict bound before allocation, including the fifth prefix byte.
            uint length = 0;
            for (int i = 0; i < 5; i++)
            {
                byte value = reader.ReadByte();
                if (i == 4 && (value & 0xF8) != 0)
                    throw new InvalidDataException("Text prefix overflow");
                length |= (uint)(value & 127) << (7 * i);
                if ((value & 128) == 0)
                {
                    if (length > maximum * 4U)
                        throw new InvalidDataException("Oversized text");
                    string text = Utf8.GetString(ReadExact(reader, (int)length));
                    if (text.Length > maximum)
                        throw new InvalidDataException("Oversized text");
                    return text;
                }
            }

            throw new InvalidDataException("Invalid text prefix");
        }

        private static int Count(BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum)
                throw new InvalidDataException("Invalid collection count");
            return count;
        }

        // Same  field representation, with bounded scope reads.
        private static void WriteArchive(BinaryWriter writer, TradeHistoryArchive archive)
        {
            TradeSaveCodec.Write(writer, archive);
        }

        private static TradeHistoryArchive ReadArchive(BinaryReader reader, int day)
        {
            if (!ReadBool(reader))
                throw new InvalidDataException("Missing archive");
            var archive = new TradeHistoryArchive
            {
                MapFingerprint = Text(reader, 256),
                RosterFingerprint = Text(reader, 256),
                NextId = reader.ReadUInt64(),
                LastAutonomousDay = reader.ReadInt32(),
                PendingNativeId = reader.ReadUInt64()
            };
            int count = Count(reader, TradeHistoryArchive.MaximumRelations);
            for (int i = 0; i < count; i++)
                archive.Relationships.Add(new TradeRelationship { A = reader.ReadByte(), B = reader.ReadByte(), Goodwill = reader.ReadInt32(), RejectionStreak = reader.ReadInt32(), LastRejectedDay = reader.ReadInt32(), LastAttemptDay = reader.ReadInt32(), LastPenaltyDay = reader.ReadInt32(), RejectedToday = reader.ReadInt32(), AcceptedTrades = reader.ReadInt32(), LastDealDay = reader.ReadInt32() });
            count = Count(reader, TradeHistoryArchive.MaximumRecords);
            for (int i = 0; i < count; i++)
                archive.Records.Add(new TradeRecord { Id = reader.ReadUInt64(), RootId = reader.ReadUInt64(), Requester = reader.ReadByte(), Recipient = reader.ReadByte(), OfferedGood = reader.ReadUInt16(), RequestedGood = reader.ReadUInt16(), OfferedAmount = reader.ReadInt32(), RequestedAmount = reader.ReadInt32(), IssuedDay = reader.ReadInt32(), ExpiryDay = reader.ReadInt32(), Generosity = reader.ReadByte(), State = (TradeDisposition)reader.ReadByte(), AiToAi = ReadBool(reader), ChanceBasisPoints = reader.ReadInt32(), RollBasisPoints = reader.ReadInt32() });
            archive.Validate(null, null, day);
            return archive;
        }
    }
}
