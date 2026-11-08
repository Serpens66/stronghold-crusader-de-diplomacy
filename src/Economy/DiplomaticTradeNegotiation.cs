//  natural barter extension. Offline sandbox ONLY – depends on the SHA-pinned 
// research core (not an installed mod). No network/Steam/native/game SaveData writes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.TradeNegotiation
{
    public enum TradeOfferGenerosity
    {
        Grosszuegig = 0,
        Fair = 1,
        Knausrig = 2
    }

    public enum DiplomaticTradeOutcome
    {
        Accepted,
        CounterOffer,
        Rejected,
        Blocked,
        Duplicate
    }

    public sealed class ManagedTradeRelationship
    {
        public byte A, B;
        public int Goodwill = 50;
        public int ConsecutiveRejected;
        public int LastRejectedDay = -100;
        public int LastAttemptDay = -100;
        public int RejectedToday, LastPenaltyDay = -100;
        public int AcceptedTrades;
        public int LastDealDay = -100;
        public ManagedTradeRelationship Copy()
        {
            return (ManagedTradeRelationship)MemberwiseClone();
        }
    }

    public sealed class NegotiatedTradeState
    {
        public DiplomaticTradeOutcome Status;
        public string Reason;
        public BarterQuote Quote, CounterOffer;
        public TradeOfferGenerosity Generosity;
        public byte Requester, Recipient;
        public ushort ActualRequestedGood, ActualOfferedGood;
        public int RelationBefore, RelationAfter;
        public int ChanceBasisPoints, RollBasisPoints;
        public bool IsAiToAi;
        public bool SimulatorGoodsChanged;
        public decimal AiGain, RequesterSaving;
        public NegotiatedTradeState Copy()
        {
            var c = (NegotiatedTradeState)MemberwiseClone();
            c.Quote = Quote == null ? null : Quote.Copy();
            c.CounterOffer = CounterOffer == null ? null : CounterOffer.Copy();
            return c;
        }
    }

    // Explicitly observable, bounded relationship effects; no live diplomatic
    // state is mutated. The product must bind these fields to its own save model.
    public sealed class ManagedTradeRelationshipBook
    {
        readonly Dictionary<int, ManagedTradeRelationship> _relations = new Dictionary<int, ManagedTradeRelationship>();
        private static int Key(byte a, byte b)
        {
            if (a == b || a < 1 || a > 8 || b < 1 || b > 8)
                throw new ArgumentException("different player slots");
            return (Math.Min(a, b) << 8) | Math.Max(a, b);
        }

        public ManagedTradeRelationship Get(byte a, byte b)
        {
            int key = Key(a, b);
            ManagedTradeRelationship state;
            if (!_relations.TryGetValue(key, out state))
            {
                state = new ManagedTradeRelationship
                {
                    A = (byte)Math.Min(a, b),
                    B = (byte)Math.Max(a, b)
                };
                _relations.Add(key, state);
            }

            return state;
        }

        public ManagedTradeRelationship Observe(byte a, byte b)
        {
            return Get(a, b).Copy();
        }

        public ManagedTradeRelationship Peek(byte a, byte b)
        {
            int key = Key(a, b);
            ManagedTradeRelationship value;
            return _relations.TryGetValue(key, out value) ? value.Copy() : new ManagedTradeRelationship
            {
                A = (byte)Math.Min(a, b),
                B = (byte)Math.Max(a, b)
            };
        }

        public void Initialize(byte a, byte b, int genuineDiplomacyGoodwill)
        {
            if (genuineDiplomacyGoodwill < 0 || genuineDiplomacyGoodwill > 100)
                throw new ArgumentOutOfRangeException("genuineDiplomacyGoodwill");
            ManagedTradeRelationship state = Get(a, b);
            if (state.LastAttemptDay != -100 || state.AcceptedTrades > 0)
                throw new InvalidOperationException("Cannot overwrite already trading relationship");
            state.Goodwill = genuineDiplomacyGoodwill;
        }

        public void Rejection(byte a, byte b, int day)
        {
            var x = Get(a, b);
            if (day < 1)
                throw new ArgumentOutOfRangeException("day");
            if (x.LastRejectedDay >= 1 && day < x.LastRejectedDay)
                throw new ArgumentException("Cannot record a rejection before previous rejection day");
            if ((long)day - x.LastRejectedDay <= 2)
                x.ConsecutiveRejected = Math.Min(5000, x.ConsecutiveRejected + 1);
            else
                x.ConsecutiveRejected = 1;
            if (x.LastPenaltyDay != day)
            {
                x.LastPenaltyDay = day;
                x.RejectedToday = 0;
            }

            int penalty = Math.Min(14, 2 + (x.ConsecutiveRejected - 1) * 4);
            int allowance = Math.Max(0, 24 - x.RejectedToday);
            int applied = Math.Min(allowance, penalty);
            x.RejectedToday += applied;
            x.Goodwill = Math.Max(0, x.Goodwill - applied);
            x.LastRejectedDay = day;
            x.LastAttemptDay = day;
        }

        public void CounterDeclined(byte a, byte b, int day)
        {
            Rejection(a, b, day);
        }

        public void Positive(byte a, byte b, int day, bool wasCounter)
        {
            if (day < 1)
                throw new ArgumentOutOfRangeException("day");
            var x = Get(a, b);
            if (x.AcceptedTrades >= int.MaxValue - 1)
                throw new InvalidOperationException("Accepted-trade counter overflow");
            // All refusal checks happen BEFORE modifying a relationship.
            x.Goodwill = Math.Min(100, x.Goodwill + (wasCounter ? 4 : 6));
            x.ConsecutiveRejected = 0;
            x.LastRejectedDay = -100;
            x.LastDealDay = day;
            x.LastAttemptDay = day;
            x.AcceptedTrades++;
        }

        public void PassageOfDay(int day)
        {
            if (day < 1)
                throw new ArgumentOutOfRangeException("day");
            foreach (var x in _relations.Values)
            {
                if ((long)day - x.LastRejectedDay > 3 && x.ConsecutiveRejected > 0)
                    x.ConsecutiveRejected = 0;
                if ((long)day - x.LastRejectedDay > 5 && x.Goodwill < 50)
                    x.Goodwill++;
            }
        }

        private static bool ValidRestoredDay(int day)
        {
            return day == -100 || day >= 1;
        }

        public void Restore(IEnumerable<ManagedTradeRelationship> saved)
        {
            if (saved == null)
                throw new ArgumentNullException("saved");
            // Build and validate a COMPLETE detached replacement. Never
            // clear live goodwill when a duplicate, null or corrupted
            // record appears halfway through a loaded trade archive.
            var next = new Dictionary<int, ManagedTradeRelationship>();
            foreach (var incoming in saved)
            {
                if (incoming == null)
                    throw new InvalidOperationException("Null relationship record");
                ManagedTradeRelationship item = incoming.Copy();
                if (item.Goodwill < 0 || item.Goodwill > 100 || item.ConsecutiveRejected < 0 || item.ConsecutiveRejected > 5000 || item.RejectedToday < 0 || item.RejectedToday > 24 || item.AcceptedTrades < 0 || item.AcceptedTrades >= int.MaxValue || !ValidRestoredDay(item.LastRejectedDay) || !ValidRestoredDay(item.LastAttemptDay) || !ValidRestoredDay(item.LastPenaltyDay) || !ValidRestoredDay(item.LastDealDay))
                    throw new InvalidOperationException("Damaged relationship record");
                int pair = Key(item.A, item.B);
                if (next.ContainsKey(pair))
                    throw new InvalidOperationException("Duplicate trading relation");
                next.Add(pair, item);
                if (next.Count > 28)
                    throw new InvalidOperationException("Too many trading relationships");
            }

            // All validations succeeded: replace only now.
            _relations.Clear();
            foreach (var kv in next)
                _relations.Add(kv.Key, kv.Value);
        }

        public ManagedTradeRelationship[] Snapshot()
        {
            return _relations.Values.Select(x => x.Copy()).OrderBy(x => x.A).ThenBy(x => x.B).ToArray();
        }
    }

    public sealed class DiplomaticTradeNegotiator
    {
        readonly object _sync = new object ();
        readonly IAuthorizedSinglePlayerPort _port;
        readonly IBarterPriceSource _prices;
        readonly IObservedTradePairProvider _pairPolicy;
        readonly Dictionary<byte, Ruler> _lords = new Dictionary<byte, Ruler>();
        readonly Dictionary<ulong, NegotiatedTradeState> _requests = new Dictionary<ulong, NegotiatedTradeState>();
        readonly Dictionary<ulong, string> _originalTerms = new Dictionary<ulong, string>();
        readonly Dictionary<ulong, NegotiatedTradeState> _pending = new Dictionary<ulong, NegotiatedTradeState>();
        readonly HashSet<ulong> _terminal = new HashSet<ulong>();
        readonly Guid _map;
        readonly ulong _term;
        readonly int _worldSeed;
        ulong _nextAutoId = 50000;
        public ulong NextAutonomousId
        {
            get
            {
                lock (_sync)
                    return _nextAutoId;
            }
        }

        BarterEngine _engine;
        public readonly ManagedTradeRelationshipBook Relationships = new ManagedTradeRelationshipBook();
        public int Day
        {
            get
            {
                return _engine.Day;
            }
        }

        public int Revision
        {
            get
            {
                return _engine.Revision;
            }
        }

        public readonly List<string> Chronicle = new List<string>();
        public bool SandboxOnly
        {
            get
            {
                return true;
            }
        }

        public DiplomaticTradeNegotiator(IAuthorizedSinglePlayerPort port, IBarterPriceSource prices, IEnumerable<Ruler> participants, Guid map, ulong term, int day, int worldSeed, ulong nextAutonomousId = 50000) : this(port, prices, participants, map, term, day, worldSeed, nextAutonomousId, null)
        {
        }

        // Preserve the old constructor's CLR signature for compiled regression
        // harnesses. Supplying the final policy opts into strict live rules.
        public DiplomaticTradeNegotiator(IAuthorizedSinglePlayerPort port, IBarterPriceSource prices, IEnumerable<Ruler> participants, Guid map, ulong term, int day, int worldSeed, ulong nextAutonomousId, IObservedTradePairProvider pairPolicy)
        {
            if (port == null || participants == null || map == Guid.Empty || term == 0 || day < 1 || worldSeed == 0)
                throw new ArgumentException("Missing verified offline world context");
            if (nextAutonomousId == 0 || nextAutonomousId >= ulong.MaxValue - 256)
                throw new ArgumentException("Invalid monotonic AI trade id");
            _nextAutoId = nextAutonomousId;
            _port = port;
            _prices = prices;
            _map = map;
            _term = term;
            _worldSeed = worldSeed;
            _pairPolicy = pairPolicy;
            foreach (Ruler ruler in participants)
            {
                if (ruler == null || ruler.PlayerId < 1 || ruler.PlayerId > 8 || ruler.Name == null || _lords.ContainsKey((byte)ruler.PlayerId))
                    throw new ArgumentException("Duplicate/missing ruler identity");
                _lords.Add((byte)ruler.PlayerId, ruler.Copy());
            }

            if (_lords.Count < 2)
                throw new ArgumentException("Need at least two lords");
            _engine = new BarterEngine(port, map, term, 1, day, worldSeed, prices);
        }

        Good Price(ushort g)
        {
            return _prices == null ? GoodCatalog.Find(g) : _prices.Find(g);
        }

        StockCell Read(byte owner, ushort g)
        {
            return _port.Read(owner, g);
        }

        static int Clamp(int n, int min = 0, int max = 100)
        {
            return Math.Min(max, Math.Max(min, n));
        }

        public bool IsValidPartner(byte player)
        {
            return _lords.ContainsKey(player);
        }

        public Ruler Lord(byte id)
        {
            Ruler result;
            return _lords.TryGetValue(id, out result) ? result.Copy() : null;
        }

        private bool TryCurrentPair(byte requester, byte recipient, out ObservedTradePairState pair, out string reason)
        {
            pair = null;
            reason = null;
            // Compatibility is deliberate and limited to the old offline
            // simulator. The live adapter must always pass a closed policy.
            if (_pairPolicy == null)
                return true;
            try
            {
                ObservedTradePairState observed;
                if (!_pairPolicy.TryGetPair(requester, recipient, Day, out observed) || observed == null)
                {
                    reason = "Keine bestätigte Handelsbeziehung";
                    return false;
                }

                pair = observed.Copy();
                return TradePairEligibilityRules.CanTrade(pair, requester, recipient, Day, out reason);
            }
            catch (Exception)
            {
                pair = null;
                reason = "Diplomatische Lage konnte nicht sicher gelesen werden";
                return false;
            }
        }

        // Larger number = stronger need. "?" on the offered side means:
        // choose the material the addressee needs MOST from the requester's surplus.
        public int NeedScore(byte player, ushort good)
        {
            // No missing ruler or corrupt detached stock cell may crash the
            // entire day's AI planning pass. A genuinely unknown need is NOT
            // fabricated as zero/adequate observed production.
            if (!IsValidPartner(player) || GoodCatalog.Find(good) == null)
                return int.MinValue;
            try
            {
                var s = Read(player, good);
                if (s == null || Price(good) == null)
                    return int.MinValue;
                s.Validate();
                int deficit = Math.Max(0, s.Reserve() - s.Quantity);
                int consumption = Math.Max(0, s.DailyConsumption - s.DailyProduction);
                int military = (good >= 17 && _lords[player].AtWar) ? 16 : 0;
                int priority = (good == 10 || good == 11 || good == 12 || good == 13) ? 8 : 0;
                long weighted = (long)deficit * 12L + (long)consumption * 7L + Math.Max(0L, 30L - s.Quantity) / 3L + military + priority;
                return (int)Math.Min(100000000L, weighted);
            }
            catch (InvalidOperationException)
            {
                return int.MinValue;
            }
            catch (OverflowException)
            {
                return int.MinValue;
            }
        }

        public ushort ChooseRequestedFromPartner(byte requester, byte recipient, ushort exclude = 0)
        {
            if (!IsValidPartner(requester) || !IsValidPartner(recipient))
                return 0;
            // "?" on requested side: select partner's greatest ACTUAL surplus,
            // with requester's need as tie-breaker, and verify both capacities.
            return GoodCatalog.All.Where(g => g.Id != exclude).Where(g =>
            {
                var give = Read(recipient, g.Id);
                var receive = Read(requester, g.Id);
                return give != null && receive != null && give.Surplus > 0 && receive.Space > 0;
            }).OrderByDescending(g => Read(recipient, g.Id).Surplus).ThenByDescending(g => NeedScore(requester, g.Id)).ThenBy(g => g.Id).Select(g => g.Id).DefaultIfEmpty((ushort)0).First();
        }

        public ushort ChooseOfferedForPartner(byte requester, byte recipient, ushort exclude = 0)
        {
            if (!IsValidPartner(requester) || !IsValidPartner(recipient))
                return 0;
            return GoodCatalog.All.Where(g => g.Id != exclude).Where(g =>
            {
                var src = Read(requester, g.Id);
                var target = Read(recipient, g.Id);
                return src != null && target != null && src.Surplus > 0 && target.Space > 0;
            }).OrderByDescending(g => NeedScore(recipient, g.Id)).ThenByDescending(g => Read(requester, g.Id).Surplus).ThenBy(g => g.Id).Select(g => g.Id).DefaultIfEmpty((ushort)0).First();
        }

        public bool ResolveQuestionMarks(byte requester, byte recipient, ushort offeredOrZero, ushort requestedOrZero, out ushort offered, out ushort requested)
        {
            requested = requestedOrZero != 0 ? requestedOrZero : ChooseRequestedFromPartner(requester, recipient, offeredOrZero);
            offered = offeredOrZero != 0 ? offeredOrZero : ChooseOfferedForPartner(requester, recipient, requested);
            // If both were "?", prefer partner's biggest surplus, then requester's
            // most-needed transferable good to complete two DIFFERENT goods.
            return requested != 0 && offered != 0 && requested != offered && Price(requested) != null && Price(offered) != null;
        }

        // '?' is a request to find an executable and economically
        // balanced trade, not merely the largest raw surplus in the world.
        // The original 'requested' selector ignores the requested quantity
        // and receiver's remaining warehouse space. This production-model
        // filter considers FULL quote terms BEFORE the first AI decision.
        // Pure offline stock reads: no native authority is acquired here.
        private bool TryResolveFeasibleAutoTrade(byte requester, byte recipient, ushort offeredChoice, ushort requestedChoice, int requestedQty, TradeOfferGenerosity tone, int offeredOverride, out ushort offered, out ushort requested, out int offeredAmount)
        {
            offered = 0;
            requested = 0;
            offeredAmount = 0;
            if (requestedQty < 1 || requestedQty > 100000)
                return false;
            var requestedGoods = GoodCatalog.All.Where(g => requestedChoice == 0 || g.Id == requestedChoice).OrderByDescending(g =>
            {
                var s = Read(recipient, g.Id);
                return s == null ? 0 : s.Surplus;
            }).ThenByDescending(g => NeedScore(requester, g.Id)).ThenBy(g => g.Id);
            foreach (Good want in requestedGoods)
            {
                var supplier = Read(recipient, want.Id);
                var destination = Read(requester, want.Id);
                var wantPrice = Price(want.Id);
                if (supplier == null || destination == null || wantPrice == null || supplier.Surplus < requestedQty || destination.Space < requestedQty)
                    continue;
                var offeredGoods = GoodCatalog.All.Where(g => (offeredChoice == 0 || g.Id == offeredChoice) && g.Id != want.Id).OrderByDescending(g => NeedScore(recipient, g.Id)).ThenByDescending(g =>
                {
                    var s = Read(requester, g.Id);
                    return s == null ? 0 : s.Surplus;
                }).ThenBy(g => g.Id);
                foreach (Good give in offeredGoods)
                {
                    var givePrice = Price(give.Id);
                    if (givePrice == null)
                        continue;
                    int amount = offeredOverride > 0 ? offeredOverride : QuoteOfferedAmount(give.Id, want.Id, requestedQty, tone);
                    if (!PhysicallyPossible(requester, recipient, give.Id, want.Id, amount, requestedQty))
                        continue;
                    // One anomalous or numerically unrepresentable market
                    // candidate must not abort the complete AI planner or
                    // falsely quarantine native inventory. Skip that pair.
                    try
                    {
                        decimal payment = givePrice.Mid * amount;
                        decimal recipientGain = payment - wantPrice.MarketSell * requestedQty;
                        decimal requesterSaving = wantPrice.MarketBuy * requestedQty - payment;
                        if (!(recipientGain > requesterSaving && requesterSaving > 0m))
                            continue;
                    }
                    catch (OverflowException)
                    {
                        continue;
                    }

                    offered = give.Id;
                    requested = want.Id;
                    offeredAmount = amount;
                    return true;
                }
            }

            return false;
        }

        public static decimal RecipientSpreadShare(TradeOfferGenerosity generosity)
        {
            if (generosity == TradeOfferGenerosity.Grosszuegig)
                return 0.84m;
            if (generosity == TradeOfferGenerosity.Fair)
                return 0.68m;
            if (generosity == TradeOfferGenerosity.Knausrig)
                return 0.54m;
            throw new ArgumentOutOfRangeException("generosity");
        }

        public int QuoteOfferedAmount(ushort give, ushort get, int requestedQty, TradeOfferGenerosity tone)
        {
            Good a = Price(give), b = Price(get);
            if (a == null || b == null || give == get || requestedQty <= 0 || requestedQty > 100000)
                return 0;
            // Preserve the explicit invalid-tier exception and avoid silently
            // treating an unsupported tier as a fair/generous price.
            decimal share = RecipientSpreadShare(tone);
            try
            {
                // Strict economic condition:
                //       sell + (buy-sell)/2 < offered.Mid*n < buy.
                // Solve the WHOLE valid integer interval analytically, rather
                // than searching +/-12 around an unclamped ideal quantity.
                // A valid amount <=100000 must not be lost near either bound.
                decimal mid = a.Mid;
                if (mid <= 0m || b.MarketSell <= 0m || b.MarketBuy <= b.MarketSell)
                    return 0;
                decimal sell = b.MarketSell * requestedQty;
                decimal buy = b.MarketBuy * requestedQty;
                decimal spread = buy - sell;
                decimal minExclusive = sell + spread / 2m;
                decimal lo = Math.Max(1m, decimal.Floor(minExclusive / mid) + 1m);
                decimal hi = Math.Min(100000m, decimal.Ceiling(buy / mid) - 1m);
                if (lo > hi)
                    return 0;
                decimal desired = sell + share * spread;
                decimal nearest = decimal.Round(desired / mid, 0, MidpointRounding.AwayFromZero);
                int candidate = (int)Math.Min(hi, Math.Max(lo, nearest));
                decimal payment = mid * candidate;
                decimal recipientGain = payment - sell;
                decimal requesterSaving = buy - payment;
                return recipientGain > requesterSaving && requesterSaving > 0m ? candidate : 0;
            }
            catch (OverflowException)
            {
                // A numerically unrepresentable detached market is not
                // permission to invent a quote or quarantine native goods.
                return 0;
            }
        }

        public bool IsMutuallyBeneficial(BarterQuote q)
        {
            decimal recipient, requester;
            return q != null && _engine.EconomicAdvantage(q, out recipient, out requester);
        }

        private bool PhysicallyPossible(byte requester, byte recipient, ushort offer, ushort requested, int offeredAmount, int requestedAmount)
        {
            if (requester == recipient || offeredAmount <= 0 || requestedAmount <= 0 || offeredAmount > 100000 || requestedAmount > 100000 || offer == requested)
                return false;
            var mine = Read(requester, offer);
            var partner = Read(recipient, requested);
            var incomingPartner = Read(recipient, offer);
            var incomingMine = Read(requester, requested);
            return mine != null && partner != null && incomingPartner != null && incomingMine != null && mine.Surplus >= offeredAmount && partner.Surplus >= requestedAmount && incomingPartner.Space >= offeredAmount && incomingMine.Space >= requestedAmount;
        }

        private NegotiatedTradeState Rejected(byte requester, byte recipient, ushort offer, ushort request, TradeOfferGenerosity tone, ulong id, string message, bool aiToAi)
        {
            var relation = Relationships.Get(requester, recipient);
            int before = relation.Goodwill;
            Relationships.Rejection(requester, recipient, Day);
            var denied = new NegotiatedTradeState
            {
                Status = DiplomaticTradeOutcome.Rejected,
                Reason = message,
                Requester = requester,
                Recipient = recipient,
                ActualRequestedGood = request,
                ActualOfferedGood = offer,
                Generosity = tone,
                RelationBefore = before,
                RelationAfter = relation.Goodwill,
                IsAiToAi = aiToAi
            };
            _requests[id] = denied.Copy();
            Chronicle.Add("Tag " + Day + ": " + _lords[recipient].Name + " lehnt " + _lords[requester].Name + " ab (" + message + "). Beziehung " + before + "→" + relation.Goodwill);
            return denied;
        }

        public NegotiatedTradeState Propose(byte requester, byte recipient, ushort offeredOrQuestionMark, ushort requestedOrQuestionMark, int requestedQty, TradeOfferGenerosity tone, ulong id, int offeredQtyOverride = 0, bool aiToAi = false)
        {
            lock (_sync)
            {
                if (!IsValidPartner(requester) || !IsValidPartner(recipient) || requester == recipient || id == 0 || id >= ulong.MaxValue - 4 || requestedQty <= 0 || requestedQty > 100000 || offeredQtyOverride < 0 || offeredQtyOverride > 100000 || !Enum.IsDefined(typeof(TradeOfferGenerosity), tone))
                    return new NegotiatedTradeState
                    {
                        Status = DiplomaticTradeOutcome.Blocked,
                        Reason = "Ungültige Partner, Menge oder Angebotsstufe"
                    };
                // Quote identity is bound to the ORIGINAL request, not to
                // whichever goods a later '?' lookup might select.
                string entry = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}", requester, recipient, offeredOrQuestionMark, requestedOrQuestionMark, requestedQty, tone, offeredQtyOverride, aiToAi, _map, _term, Day);
                NegotiatedTradeState seen;
                if (_requests.TryGetValue(id, out seen))
                {
                    string old;
                    if (!_originalTerms.TryGetValue(id, out old) || old != entry)
                        return new NegotiatedTradeState
                        {
                            Status = DiplomaticTradeOutcome.Blocked,
                            Reason = "Dieselbe Angebots-ID darf keine anderen Terms tragen"
                        };
                    return seen.Copy(); // one decision per exact offer ID+terms
                }

                if (_pending.ContainsKey(id))
                    return new NegotiatedTradeState
                    {
                        Status = DiplomaticTradeOutcome.Blocked,
                        Reason = "ID ist für ein offenes Gegenangebot reserviert"
                    };
                ObservedTradePairState pair;
                string pairReason;
                if (!TryCurrentPair(requester, recipient, out pair, out pairReason))
                    return new NegotiatedTradeState
                    {
                        Status = DiplomaticTradeOutcome.Blocked,
                        Requester = requester,
                        Recipient = recipient,
                        Reason = pairReason
                    };
                _originalTerms[id] = entry;
                if (_terminal.Contains(id))
                    return new NegotiatedTradeState
                    {
                        Status = DiplomaticTradeOutcome.Duplicate,
                        Reason = "Angebot bereits abgeschlossen"
                    };
                ushort offered, requested;
                int amount = 0;
                bool resolved;
                // Preserve explicitly chosen player goods exactly; only '?'
                // may search alternate price-, reserve- and space-safe terms.
                if (offeredOrQuestionMark == 0 || requestedOrQuestionMark == 0)
                {
                    resolved = TryResolveFeasibleAutoTrade(requester, recipient, offeredOrQuestionMark, requestedOrQuestionMark, requestedQty, tone, offeredQtyOverride, out offered, out requested, out amount);
                }
                else
                {
                    resolved = ResolveQuestionMarks(requester, recipient, offeredOrQuestionMark, requestedOrQuestionMark, out offered, out requested);
                    if (resolved)
                        amount = offeredQtyOverride > 0 ? offeredQtyOverride : QuoteOfferedAmount(offered, requested, requestedQty, tone);
                }

                if (!resolved)
                {
                    return Rejected(requester, recipient, offeredOrQuestionMark, requestedOrQuestionMark, tone, id, "Keine mengen- und lagergeeigneten Handelswaren", aiToAi);
                }

                var relationship = Relationships.Get(requester, recipient);
                if (relationship.Goodwill < 12 && !TradePairEligibilityRules.RecipientIsVassalOfRequester(pair))
                    return Rejected(requester, recipient, offered, requested, tone, id, "Der Fürst ist nach den wiederholten Zurückweisungen verstimmt", aiToAi);
                if (relationship.RejectedToday >= 3 && relationship.LastPenaltyDay == Day)
                    return Rejected(requester, recipient, offered, requested, tone, id, "Heute keine weiteren Handelsanfragen", aiToAi);
                if (!PhysicallyPossible(requester, recipient, offered, requested, amount, requestedQty))
                    return Rejected(requester, recipient, offered, requested, tone, id, "Kein echter Überschuss oder kein Lagerplatz", aiToAi);
                var q = _engine.Make(requester, recipient, offered, requested, requestedQty, amount, id);
                decimal recipientGain, requesterSaving;
                if (!_engine.EconomicAdvantage(q, out recipientGain, out requesterSaving))
                    return Rejected(requester, recipient, offered, requested, tone, id, "Der Tausch wäre für eine Seite schlechter als der Markt", aiToAi);
                // No effect until after the mandatory reserve and economic checks.
                Ruler recipientAI = _lords[recipient].Copy();
                // Do not let a caller claim "generous" while overriding the
                // amount to a stingy number for an unearned probability bonus.
                decimal sell = Price(requested).MarketSell * requestedQty;
                decimal spread = (Price(requested).MarketBuy - Price(requested).MarketSell) * requestedQty;
                decimal actualShare = spread <= 0 ? 0 : (Price(offered).Mid * amount - sell) / spread;
                int bonus = actualShare >= 0.78m ? 12 : actualShare >= 0.60m ? 0 : -10;
                recipientAI.Trust = Clamp(recipientAI.Trust + (relationship.Goodwill - 50) * 3 / 2 + bonus);
                BarterOutcome outcome;
                if (_pairPolicy == null)
                    outcome = _engine.EvaluateAI(q, recipientAI);
                else
                {
                    TradeAcceptanceAssessment willingness = TradePairEligibilityRules.EvaluateAcceptance(pair, recipientAI, q, Read(recipient, requested), Read(recipient, offered), recipientGain, requesterSaving);
                    if (!willingness.Valid)
                        return new NegotiatedTradeState
                        {
                            Status = DiplomaticTradeOutcome.Blocked,
                            Requester = requester,
                            Recipient = recipient,
                            Reason = willingness.Reason
                        };
                    outcome = _engine.EvaluateAIWithPolicyProbability(q, recipientAI, willingness.ProbabilityBasisPoints);
                }

                var answer = new NegotiatedTradeState
                {
                    Requester = requester,
                    Recipient = recipient,
                    ActualOfferedGood = offered,
                    ActualRequestedGood = requested,
                    Quote = q.Copy(),
                    Generosity = tone,
                    RelationBefore = relationship.Goodwill,
                    RelationAfter = relationship.Goodwill,
                    ChanceBasisPoints = outcome.AcceptanceProbabilityBasisPoints,
                    RollBasisPoints = outcome.DeterministicRollBasisPoints,
                    AiGain = recipientGain,
                    RequesterSaving = requesterSaving,
                    IsAiToAi = aiToAi
                };
                if (outcome.Status == BarterStatus.Accepted)
                {
                    string reason;
                    ObservedTradePairState current;
                    bool currentPairValid = TryCurrentPair(requester, recipient, out current, out reason);
                    if (!currentPairValid || (_pairPolicy != null && !TradePairEligibilityRules.SameDecisionContext(pair, current)))
                    {
                        answer.Status = DiplomaticTradeOutcome.Blocked;
                        answer.Reason = currentPairValid ? "Diplomatische Lage hat sich verändert" : reason;
                    }
                    else if (!_engine.TrySettle(q, out reason))
                    {
                        answer.Status = DiplomaticTradeOutcome.Blocked;
                        answer.Reason = "Simulierter Abschluss nicht möglich: " + reason;
                    }
                    else
                    {
                        answer.Status = DiplomaticTradeOutcome.Accepted;
                        answer.SimulatorGoodsChanged = true;
                        Relationships.Positive(requester, recipient, Day, false);
                        answer.RelationAfter = relationship.Goodwill;
                        answer.Reason = _lords[recipient].Name + " akzeptiert den Tausch";
                        Chronicle.Add("Tag " + Day + ": " + _lords[requester].Name + " ↔ " + _lords[recipient].Name + ": Handel abgeschlossen. Beziehung " + answer.RelationBefore + "→" + answer.RelationAfter);
                        _terminal.Add(q.Id);
                    }
                }
                else if (outcome.Status == BarterStatus.CounterOffer && outcome.Counter != null)
                {
                    answer.Status = DiplomaticTradeOutcome.CounterOffer;
                    answer.CounterOffer = outcome.Counter.Copy();
                    answer.Reason = _lords[recipient].Name + " schlägt einen höheren Gegenwert vor";
                    _pending[answer.CounterOffer.Id] = answer.Copy();
                    Chronicle.Add("Tag " + Day + ": " + _lords[recipient].Name + " macht ein Gegenangebot an " + _lords[requester].Name);
                }
                else
                {
                    return Rejected(requester, recipient, offered, requested, tone, id, "Angebot überzeugt den Fürsten nicht", aiToAi);
                }

                _requests[id] = answer.Copy();
                return answer.Copy();
            }
        }

        // Bind every terminal counter response to the ORIGINAL request ID.
        // Otherwise replaying Propose(originalId) returns a stale CounterOffer
        // even though the counter was already accepted, declined or consumed.
        // Use the existing monitor lock, preserving exact-once goods and relation
        // semantics and a terminal receipt for replay.
        private NegotiatedTradeState FinishCounter(ulong counterId, NegotiatedTradeState original, NegotiatedTradeState outcome)
        {
            if (original == null || original.Quote == null || original.Quote.Id == 0)
                throw new InvalidOperationException("Counter has no original quote identity");
            _terminal.Add(counterId);
            _terminal.Add(original.Quote.Id);
            _requests[original.Quote.Id] = outcome.Copy();
            return outcome.Copy();
        }

        public NegotiatedTradeState RespondCounter(ulong counterId, byte trustedRequester, bool accept)
        {
            lock (_sync)
            {
                NegotiatedTradeState initial;
                if (!_pending.TryGetValue(counterId, out initial) || initial.CounterOffer == null || initial.Quote == null || initial.Requester != trustedRequester)
                    return new NegotiatedTradeState
                    {
                        Status = DiplomaticTradeOutcome.Blocked,
                        Reason = "Kein offenes, zu diesem Spieler passendes Gegenangebot"
                    };
                _pending.Remove(counterId);
                var offer = initial.CounterOffer.Copy();
                var relation = Relationships.Get(initial.Requester, initial.Recipient);
                var result = initial.Copy();
                result.CounterOffer = offer;
                result.RelationBefore = relation.Goodwill;
                ObservedTradePairState pair;
                string pairReason;
                if (!TryCurrentPair(initial.Requester, initial.Recipient, out pair, out pairReason))
                {
                    result.Status = DiplomaticTradeOutcome.Blocked;
                    result.Reason = pairReason;
                    return FinishCounter(counterId, initial, result);
                }

                if (!accept)
                {
                    Relationships.CounterDeclined(initial.Requester, initial.Recipient, Day);
                    result.Status = DiplomaticTradeOutcome.Rejected;
                    result.Reason = "Gegenangebot wurde abgelehnt";
                    result.RelationAfter = relation.Goodwill;
                    Chronicle.Add("Tag " + Day + ": Gegenangebot abgelehnt. Beziehung " + result.RelationBefore + "→" + result.RelationAfter);
                    return FinishCounter(counterId, initial, result);
                }

                string why;
                if (!_engine.ConfirmCounterOffer(offer, trustedRequester) || !_engine.TrySettle(offer, out why))
                {
                    result.Status = DiplomaticTradeOutcome.Blocked;
                    result.Reason = "Gegenangebot ungültig oder Lagerbestände verändert";
                    return FinishCounter(counterId, initial, result);
                }

                Relationships.Positive(initial.Requester, initial.Recipient, Day, true);
                result.Status = DiplomaticTradeOutcome.Accepted;
                result.Reason = "Gegenangebot ausdrücklich angenommen";
                result.RelationAfter = relation.Goodwill;
                result.SimulatorGoodsChanged = true;
                Chronicle.Add("Tag " + Day + ": Gegenangebot von " + _lords[initial.Recipient].Name + " angenommen. Beziehung " + result.RelationBefore + "→" + result.RelationAfter);
                return FinishCounter(counterId, initial, result);
            }
        }

        private int DeterministicDayRoll(int day, byte a, byte b)
        {
            string data = string.Format(CultureInfo.InvariantCulture, "{0:N}|{1}|{2}|{3}|{4}|{5}|natural_trading", _map, _term, _worldSeed, day, Math.Min(a, b), Math.Max(a, b));
            using (var h = SHA256.Create())
            {
                byte[] x = h.ComputeHash(Encoding.UTF8.GetBytes(data));
                uint word = (uint)x[0] | ((uint)x[1] << 8) | ((uint)x[2] << 16) | ((uint)x[3] << 24);
                return (int)(word % 10000);
            }
        }

        public bool CanAcceptAiCounter(BarterQuote q)
        {
            lock (_sync)
            {
                if (q == null || q.MapSession != _map || q.HostTerm != _term || q.Day < 1 || q.Day > Day || q.ExpiryDay < Day || q.ExpiryDay < q.Day || q.Revision < 1 || q.Id == 0 || q.Id >= ulong.MaxValue - 4 || q.RootId == 0 || q.RootId >= ulong.MaxValue - 4 || q.Id != q.RootId + 1 || q.OfferedAmount < 1 || q.OfferedAmount > 100000 || q.RequestedAmount < 1 || q.RequestedAmount > 100000 || !IsValidPartner(q.Requester) || !IsValidPartner(q.Recipient) || q.Requester == q.Recipient)
                    return false;
                ObservedTradePairState pair;
                string reason;
                if (!TryCurrentPair(q.Requester, q.Recipient, out pair, out reason))
                    return false;
                decimal recipientGain, requesterSaving;
                Good price = Price(q.RequestedGood);
                return price != null && _engine.EconomicAdvantage(q, out recipientGain, out requesterSaving) && requesterSaving > q.RequestedAmount * price.MarketSell * 0.07m && PhysicallyPossible(q.Requester, q.Recipient, q.OfferedGood, q.RequestedGood, q.OfferedAmount, q.RequestedAmount);
            }
        }

        // Live scheduling uses detached plans, not RunAutonomousAiTrade's
        // historical immediate simulator settlement. No IDs, attempts, goods,
        // goodwill or chronicle entries are consumed by planning. The host
        // reserves a saved ID and queues an answer for a later simulation tick.
        // Compatibility entry point for explicitly single-human offline tests.
        public AutonomousTradeProposal[] PlanAutonomousAiTrade(byte humanPlayer = 1)
        {
            return PlanAutonomousAiTrade(new byte[] { humanPlayer });
        }

        // The game must pass ALL independently confirmed human slots.
        // Never silently infer that every nonlocal player is an AI.
        public AutonomousTradeProposal[] PlanAutonomousAiTrade(IEnumerable<byte> humanPlayers)
        {
            if (humanPlayers == null)
                throw new ArgumentNullException("humanPlayers");
            byte[] humans = humanPlayers.ToArray();
            if (humans.Length < 1 || humans.Length > 8 || humans.Any(p => p < 1 || p > 8) || humans.Distinct().Count() != humans.Length)
                throw new ArgumentException("Untrusted human player roster");
            lock (_sync)
            {
                if (humans.Any(p => !_lords.ContainsKey(p)))
                    throw new ArgumentException("Human participant absent from world");
                var humanSet = new HashSet<byte>(humans);
                var plans = new List<AutonomousTradeProposal>();
                var ais = _lords.Keys.Where(p => !humanSet.Contains(p)).OrderBy(p => p).ToArray();
                for (int i = 0; i < ais.Length; i++)
                    for (int j = i + 1; j < ais.Length; j++)
                    {
                        byte a = ais[i], b = ais[j];
                        ManagedTradeRelationship relation = Relationships.Peek(a, b);
                        if (relation.LastAttemptDay == Day)
                            continue;
                        TradeOfferGenerosity tier = relation.Goodwill >= 70 ? TradeOfferGenerosity.Grosszuegig : relation.Goodwill < 38 ? TradeOfferGenerosity.Knausrig : TradeOfferGenerosity.Fair;
                        var candidates = new List<Tuple<int, AutonomousTradeProposal>>();
                        foreach (var direction in new[]
                        {
                            Tuple.Create(a, b),
                            Tuple.Create(b, a)
                        }

                        )
                        {
                            byte requester = direction.Item1, recipient = direction.Item2;
                            ObservedTradePairState pair;
                            string why;
                            if (!TryCurrentPair(requester, recipient, out pair, out why))
                                continue;
                            if (relation.Goodwill < 18 && !TradePairEligibilityRules.RecipientIsVassalOfRequester(pair))
                                continue;
                            foreach (Good good in GoodCatalog.All)
                            {
                                StockCell supply = Read(recipient, good.Id);
                                StockCell destination = Read(requester, good.Id);
                                if (supply == null || destination == null || supply.Surplus < 1 || destination.Space < 1)
                                    continue;
                                int need = NeedScore(requester, good.Id);
                                if (need < 10)
                                    continue;
                                int amount = Math.Min(10, Math.Min(supply.Surplus, destination.Space));
                                ushort offered = 0, requested = 0;
                                int payment = 0;
                                bool feasible = false;
                                // A ten-unit demand may be unaffordable although
                                // the same two rulers can safely exchange five
                                // or one units. Bound the extra economic search
                                // to 3 quantities per commodity and no native
                                // mutations. The exact quantities are rechecked
                                // at the later authoritative simulation tick.
                                foreach (int target in new[]
                                {
                                    amount,
                                    Math.Max(1, amount / 2),
                                    1
                                }.Distinct())
                                {
                                    if (TryResolveFeasibleAutoTrade(requester, recipient, 0, good.Id, target, tier, 0, out offered, out requested, out payment))
                                    {
                                        amount = target;
                                        feasible = true;
                                        break;
                                    }
                                }

                                if (!feasible)
                                    continue;
                                int score = need + Math.Min(50, supply.Surplus) * 2 + relation.Goodwill / 3 + _lords[requester].Commerce / 5;
                                candidates.Add(Tuple.Create(score, new AutonomousTradeProposal { Requester = requester, Recipient = recipient, OfferedGood = offered, RequestedGood = requested, RequestedAmount = amount, Generosity = tier }));
                            }
                        }

                        if (candidates.Count == 0)
                            continue;
                        // Preserve strategic scoring but avoid permanently
                        // rewarding smaller player IDs on exact-score ties.
                        // This tie-break is deterministic for replays and changes
                        // only with observed game-day/scope, not process RNG.
                        int dailyRoll = DeterministicDayRoll(Day, a, b);
                        byte preferredRequester = (byte)((dailyRoll & 1) == 0 ? a : b);
                        var selected = candidates.OrderByDescending(x => x.Item1).ThenBy(x => x.Item2.Requester == preferredRequester ? 0 : 1).ThenBy(x => x.Item2.Requester).ThenBy(x => x.Item2.RequestedGood).ThenBy(x => x.Item2.OfferedGood).First();
                        int frequency = Clamp(12 + selected.Item1 / 8 + relation.Goodwill / 3 + (_lords[selected.Item2.Requester].AtWar ? -12 : 0), 3, 75);
                        if (dailyRoll >= frequency * 100)
                            continue;
                        plans.Add(selected.Item2);
                    }

                return plans.ToArray();
            }
        }

        // AI-AI requests are triggered by shortage/production/surplus and
        // relationship, with one opportunity per unordered pair per day.
        public NegotiatedTradeState[] RunAutonomousAiTrade(byte humanPlayer = 1)
        {
            lock (_sync)
            {
                var events = new List<NegotiatedTradeState>();
                var ais = _lords.Keys.Where(p => p != humanPlayer).OrderBy(p => p).ToArray();
                for (int i = 0; i < ais.Length; i++)
                    for (int j = i + 1; j < ais.Length; j++)
                    {
                        byte a = ais[i], b = ais[j];
                        var relation = Relationships.Get(a, b);
                        if (relation.LastAttemptDay == Day || relation.Goodwill < 18)
                            continue;
                        // Try both directions; rank useful, transferable goods.
                        var candidates = new List<Tuple<int, byte, byte, ushort, ushort, int>>();
                        foreach (var pair in new[]
                        {
                            Tuple.Create(a, b),
                            Tuple.Create(b, a)
                        }

                        )
                        {
                            byte asker = pair.Item1, supplier = pair.Item2;
                            ObservedTradePairState allowed;
                            string blocked;
                            if (!TryCurrentPair(asker, supplier, out allowed, out blocked))
                                continue;
                            foreach (Good g in GoodCatalog.All)
                            {
                                var supply = Read(supplier, g.Id);
                                var destination = Read(asker, g.Id);
                                if (supply == null || destination == null || supply.Surplus < 2 || destination.Space < 2)
                                    continue;
                                int need = NeedScore(asker, g.Id);
                                if (need < 10)
                                    continue;
                                ushort pay = ChooseOfferedForPartner(asker, supplier, g.Id);
                                if (pay == 0)
                                    continue;
                                int score = need + Math.Min(50, supply.Surplus) * 2 + relation.Goodwill / 3 + _lords[asker].Commerce / 5;
                                candidates.Add(Tuple.Create(score, asker, supplier, g.Id, pay, Math.Min(10, supply.Surplus)));
                            }
                        }

                        if (candidates.Count == 0)
                            continue;
                        var selected = candidates.OrderByDescending(x => x.Item1).ThenBy(x => x.Item2).ThenBy(x => x.Item4).First();
                        int frequency = Clamp(12 + selected.Item1 / 8 + relation.Goodwill / 3 + (_lords[selected.Item2].AtWar ? -12 : 0), 3, 75);
                        if (DeterministicDayRoll(Day, a, b) >= frequency * 100)
                            continue;
                        relation.LastAttemptDay = Day;
                        TradeOfferGenerosity tier = relation.Goodwill >= 70 ? TradeOfferGenerosity.Grosszuegig : relation.Goodwill < 38 ? TradeOfferGenerosity.Knausrig : TradeOfferGenerosity.Fair;
                        ulong ticket = _nextAutoId;
                        _nextAutoId += 4;
                        var res = Propose(selected.Item2, selected.Item3, selected.Item5, selected.Item4, selected.Item6, tier, ticket, 0, true);
                        // No free unilateral transfers: AI requester independently
                        // checks its own surplus / value before accepting counter.
                        if (res.Status == DiplomaticTradeOutcome.CounterOffer && res.CounterOffer != null)
                        {
                            var q = res.CounterOffer;
                            decimal aiGain, requesterSaving;
                            if (_engine.EconomicAdvantage(q, out aiGain, out requesterSaving) && requesterSaving > q.RequestedAmount * Price(q.RequestedGood).MarketSell * 0.07m && PhysicallyPossible(q.Requester, q.Recipient, q.OfferedGood, q.RequestedGood, q.OfferedAmount, q.RequestedAmount))
                            {
                                res = RespondCounter(q.Id, q.Requester, true);
                                res.IsAiToAi = true;
                            }
                            else
                            {
                                res = RespondCounter(q.Id, q.Requester, false);
                                res.IsAiToAi = true;
                            }
                        }

                        events.Add(res);
                    }

                return events.ToArray();
            }
        }

        public void AdvanceOfflineDay(bool simulateProduction = true)
        {
            lock (_sync)
            {
                _pending.Clear();
                _requests.Clear();
                _engine.NextDay();
                Relationships.PassageOfDay(Day);
                var memory = _port as ManagedGoodsSnapshotPort;
                if (simulateProduction && memory != null)
                {
                    foreach (byte id in _lords.Keys)
                        foreach (Good g in GoodCatalog.All)
                        {
                            var stock = memory.Read(id, g.Id);
                            if (stock == null)
                                continue;
                            // The native-like counters are signed Int32,
                            // but their intermediate DAILY change can exceed it.
                            // Do not allow valid high stock + production to wrap
                            // negative and erase an AI's entire simulated store.
                            long nextQuantity = (long)stock.Quantity + stock.DailyProduction - stock.DailyConsumption;
                            stock.Quantity = (int)Math.Max(0L, Math.Min((long)stock.Capacity, nextQuantity));
                            memory.Set(id, g.Id, stock);
                        }
                }

                Chronicle.Add("Tag " + Day + ": Warenproduktion und Verbrauch wurden aktualisiert");
            }
        }
    }
}
