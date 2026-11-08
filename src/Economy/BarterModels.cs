//  - isolated singleplayer barter research implementation; no Steam, Noesis or native plugin hooks.
// BUILD AS AN ISOLATED DLL ONLY. Never install into a version-mismatched game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StrongholdEuropeDE.TradeModels
{
    public enum BarterStatus
    {
        Accepted,
        CounterOffer,
        Rejected,
        AlreadyConsumed,
        Invalid
    }

    public sealed class Good
    {
        public readonly ushort Id;
        public readonly string Name;
        public readonly decimal MarketSell, MarketBuy;
        // All prices are positive and Buy > Sell. Summing two individually
        // valid decimals can overflow; the positive spread cannot.
        public decimal Mid
        {
            get
            {
                return MarketSell + (MarketBuy - MarketSell) / 2m;
            }
        }

        public Good(ushort id, string name, decimal sell, decimal buy)
        {
            if (id == 0 || sell <= 0 || buy <= sell)
                throw new ArgumentException("Price spread invalid");
            Id = id;
            Name = name;
            MarketSell = sell;
            MarketBuy = buy;
        }
    }

    public static class GoodCatalog
    {
        // Actual allowed eGoods: forbidden 3 (raw hops), 15 (gold), etc.
        private static readonly Good[] Values =
        {
            new Good(2, "Holzbretter", 3.4m, 5.2m),
            new Good(4, "Steinblöcke", 6m, 10m),
            new Good(6, "Eisenbarren", 9m, 15m),
            new Good(7, "Rohpech", 6.1m, 9.2m),
            new Good(9, "Weizen", 2.6m, 4.3m),
            new Good(10, "Brot", 3.4m, 5.1m),
            new Good(11, "Käse", 3.7m, 5.4m),
            new Good(12, "Fleisch", 4.6m, 6.8m),
            new Good(13, "Obst", 2.5m, 4m),
            new Good(14, "Bier", 6.8m, 10.5m),
            new Good(16, "Mehl", 3.6m, 5.4m),
            new Good(17, "Bögen", 16m, 24m),
            new Good(18, "Armbrüste", 20m, 30m),
            new Good(19, "Speere", 11m, 17m),
            new Good(20, "Piken", 13m, 20m),
            new Good(21, "Streitkolben", 14m, 21m),
            new Good(22, "Schwerter", 22m, 33m),
            new Good(23, "Lederrüstung", 14m, 21m),
            new Good(24, "Metallrüstung", 26m, 38m)
        };
        public static IList<Good> All
        {
            get
            {
                return Array.AsReadOnly(Values);
            }
        }

        public static Good Find(ushort id)
        {
            return Values.FirstOrDefault(x => x.Id == id);
        }
    }

    // A price snapshot is bound to one verified market observation, NOT to UI input.
    // The actual ScriptExtender market source is supplied by NativeReadOnlyBridge.cs.
    public interface IBarterPriceSource
    {
        Good Find(ushort goodId);
    }

    public sealed class ObservedTradeMarketSnapshot : IBarterPriceSource
    {
        private readonly Dictionary<ushort, Good> _items = new Dictionary<ushort, Good>();
        public ObservedTradeMarketSnapshot(IEnumerable<Good> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            foreach (Good g in source)
            {
                if (g == null || GoodCatalog.Find(g.Id) == null || _items.ContainsKey(g.Id))
                    throw new ArgumentException("Unsupported/duplicate market good");
                _items.Add(g.Id, g);
            }

            if (_items.Count != GoodCatalog.All.Count)
                throw new ArgumentException("All 19 permitted goods must have prices");
        }

        public Good Find(ushort id)
        {
            if (GoodCatalog.Find(id) == null)
                return null;
            Good result;
            return _items.TryGetValue(id, out result) ? result : null;
        }

        // Real PackedGoodPrice fields are prices of FIVE units in SE2.14.1.
        // Only the spread is needed for bilateral gain; sorting avoids
        // incorrectly assuming from names which side is the market buyer.
        public static Good FromOriginalPacked(ushort id, string name, int packedBuy, int packedSell)
        {
            if (GoodCatalog.Find(id) == null || packedBuy <= 0 || packedSell <= 0 || packedBuy == packedSell || packedBuy > 100000000 || packedSell > 100000000)
                throw new ArgumentException("Invalid or unsupported original market price");
            return new Good(id, name, Math.Min(packedBuy, packedSell) / 5m, Math.Max(packedBuy, packedSell) / 5m);
        }
    }

    public sealed class StockCell
    {
        public int Quantity, Capacity, CoreReserve, DailyConsumption, DailyProduction, Buildings, Military;
        public StockCell Copy()
        {
            return (StockCell)MemberwiseClone();
        }

        public int Reserve(int horizon = 4)
        {
            if (horizon < 0 || horizon > 30)
                throw new ArgumentOutOfRangeException("horizon");
            long projected = Math.Max(0L, (long)Math.Ceiling(horizon * (decimal)(DailyConsumption - 0.6m * DailyProduction)));
            long buffer = Math.Max(0L, (long)CoreReserve + Buildings + Military + projected);
            return (int)Math.Min(Capacity, buffer);
        }

        public int Surplus
        {
            get
            {
                return Math.Max(0, Quantity - Reserve());
            }
        }

        public int Space
        {
            get
            {
                return Math.Max(0, Capacity - Quantity);
            }
        }

        public void Validate()
        {
            // a genuinely empty, zero-space warehouse cell is valid.
            // This is NOT an authority to create storage: the empty cell must
            // contain no supply, demand, reserves or production at all.
            // Original engine may report no eligible store for this good.
            if (Quantity < 0 || Capacity < 0 || Quantity > Capacity || CoreReserve < 0 || DailyConsumption < 0 || DailyProduction < 0 || Buildings < 0 || Military < 0 || (Capacity == 0 && (Quantity != 0 || CoreReserve != 0 || DailyConsumption != 0 || DailyProduction != 0 || Buildings != 0 || Military != 0)))
                throw new InvalidOperationException("Invalid stock cell");
        }
    }

    public sealed class Ruler
    {
        public int PlayerId, Trust, Greed, Caution, Commerce;
        public bool AtWar;
        public string Name;
        public Ruler Copy()
        {
            return (Ruler)MemberwiseClone();
        }
    }

    public sealed class BarterQuote
    {
        public Guid MapSession;
        public ulong HostTerm, Id;
        public int Revision, Day, ExpiryDay;
        public byte Requester, Recipient;
        public ushort OfferedGood, RequestedGood;
        public int OfferedAmount, RequestedAmount;
        public ulong RootId;
        public BarterQuote Copy()
        {
            return (BarterQuote)MemberwiseClone();
        }
    }

    public sealed class BarterOutcome
    {
        public BarterStatus Status;
        public string Message;
        public BarterQuote Counter;
        public decimal SaleValue, BuyValue, OfferValue, RecipientPremium, RequesterSaving;
        public int AcceptanceProbabilityBasisPoints, DeterministicRollBasisPoints;
    }

    public interface IGoodsSnapshotPort
    {
        StockCell Read(byte player, ushort good);
    }

    public interface IAuthorizedSinglePlayerPort : IGoodsSnapshotPort
    {
        // Offline simulator only. Real game must replace with properly attested tick/game save authority.
        bool HasExclusiveSinglePlayerLease(Guid session, ulong hostTerm);
        void WriteAbsolute(byte player, ushort good, int amount);
    }

    public sealed class ManagedGoodsSnapshotPort : IAuthorizedSinglePlayerPort
    {
        readonly Dictionary<long, StockCell> _map = new Dictionary<long, StockCell>();
        public bool AllowWrites = true;
        public int WriteCount;
        public static long Key(byte p, ushort g)
        {
            return ((long)p << 32) | g;
        }

        public void Set(byte p, ushort g, StockCell cell)
        {
            cell.Validate();
            _map[Key(p, g)] = cell.Copy();
        }

        public StockCell Read(byte p, ushort g)
        {
            StockCell c;
            return _map.TryGetValue(Key(p, g), out c) ? c.Copy() : null;
        }

        public bool HasExclusiveSinglePlayerLease(Guid session, ulong term)
        {
            return AllowWrites && session != Guid.Empty && term > 0;
        }

        public void WriteAbsolute(byte p, ushort g, int n)
        {
            if (!AllowWrites)
                throw new NotSupportedException("Denied");
            StockCell c;
            if (!_map.TryGetValue(Key(p, g), out c) || n < 0 || n > c.Capacity)
                throw new InvalidOperationException("Invalid write");
            c.Quantity = n;
            WriteCount++;
        }
    }

    public sealed class GamePortDenied : IAuthorizedSinglePlayerPort
    {
        // Intentional release barrier for the incompatible installed Native DLL.
        public StockCell Read(byte p, ushort g)
        {
            throw new NotSupportedException("Live Native inventory not available");
        }

        public bool HasExclusiveSinglePlayerLease(Guid session, ulong term)
        {
            return false;
        }

        public void WriteAbsolute(byte p, ushort g, int amount)
        {
            throw new NotSupportedException("Live Native writes BLOCKED");
        }
    }

    public sealed class BarterEngine
    {
        private readonly IAuthorizedSinglePlayerPort _port;
        private readonly Guid _session;
        private readonly ulong _term;
        private readonly HashSet<ulong> _consumed = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> _approved = new Dictionary<ulong, string>();
        private bool _quarantined;
        private readonly object _sync = new object ();
        private readonly int _worldSeed;
        private readonly IBarterPriceSource _prices;
        private sealed class CachedDecision
        {
            internal string ExactTerms;
            internal BarterOutcome Outcome;
        }

        private readonly Dictionary<ulong, CachedDecision> _decisions = new Dictionary<ulong, CachedDecision>();
        private readonly Dictionary<ulong, string> _pendingCounters = new Dictionary<ulong, string>();
        private Good PriceFor(ushort id)
        {
            if (GoodCatalog.Find(id) == null)
                return null;
            return _prices == null ? GoodCatalog.Find(id) : _prices.Find(id);
        }

        private static BarterOutcome CopyOutcome(BarterOutcome x)
        {
            return new BarterOutcome
            {
                Status = x.Status,
                Message = x.Message,
                Counter = x.Counter == null ? null : x.Counter.Copy(),
                SaleValue = x.SaleValue,
                BuyValue = x.BuyValue,
                OfferValue = x.OfferValue,
                RecipientPremium = x.RecipientPremium,
                RequesterSaving = x.RequesterSaving,
                AcceptanceProbabilityBasisPoints = x.AcceptanceProbabilityBasisPoints,
                DeterministicRollBasisPoints = x.DeterministicRollBasisPoints
            };
        }

        private BarterOutcome Finish(BarterQuote q, BarterOutcome answer)
        {
            if (q == null)
                return CopyOutcome(answer);
            lock (_sync)
            {
                CachedDecision old;
                if (_decisions.TryGetValue(q.Id, out old))
                {
                    return old.ExactTerms == Terms(q) ? CopyOutcome(old.Outcome) : new BarterOutcome
                    {
                        Status = BarterStatus.Rejected,
                        Message = "Trade-ID mit anderen Angebotsdaten wiederverwendet"
                    };
                }

                _decisions.Add(q.Id, new CachedDecision { ExactTerms = Terms(q), Outcome = CopyOutcome(answer) });
                return CopyOutcome(answer);
            }
        }

        // A counter-offer is not authorization to debit the requester's goods.
        // Explicit confirmation must be bound to the original requester and exact terms.
        public bool ConfirmCounterOffer(BarterQuote q, byte trustedRequester)
        {
            if (q == null)
                return false;
            lock (_sync)
            {
                if (q.Requester != trustedRequester || q.MapSession != _session || q.HostTerm != _term || q.Revision != Revision || q.Day > Day || q.ExpiryDay < Day || _consumed.Contains(q.Id) || _consumed.Contains(q.RootId))
                    return false;
                string original;
                if (!_pendingCounters.TryGetValue(q.Id, out original) || original != Terms(q))
                    return false;
                decimal rg, sg;
                if (!EconomicAdvantage(q, out rg, out sg))
                    return false;
                _pendingCounters.Remove(q.Id);
                _approved[q.Id] = original;
                return true;
            }
        }

        public int Revision { get; private set; }
        public int Day { get; private set; }

        public BarterEngine(IAuthorizedSinglePlayerPort port, Guid session, ulong term, int revision, int day) : this(port, session, term, revision, day, 0x5EED, null)
        {
        }

        public BarterEngine(IAuthorizedSinglePlayerPort port, Guid session, ulong term, int revision, int day, int hostSuppliedWorldSeed, IBarterPriceSource fixedPrices)
        {
            if (port == null || session == Guid.Empty || term == 0 || revision < 1 || day < 1 || hostSuppliedWorldSeed == 0)
                throw new ArgumentException("Invalid session or world seed");
            _port = port;
            _session = session;
            _term = term;
            Revision = revision;
            Day = day;
            _worldSeed = hostSuppliedWorldSeed;
            _prices = fixedPrices;
        }

        public static decimal MidValue(ushort good, int amount)
        {
            Good g = GoodCatalog.Find(good);
            if (g == null || amount <= 0)
                throw new ArgumentException("Bad goods");
            return g.Mid * amount;
        }

        private static string Terms(BarterQuote q)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}", q.MapSession, q.HostTerm, q.Id, q.RootId, q.Revision, q.Day, q.ExpiryDay, q.Requester, q.Recipient, q.OfferedGood, q.RequestedGood, q.OfferedAmount, q.RequestedAmount);
        }

        public BarterQuote Make(byte requester, byte recipient, ushort offered, ushort requested, int reqAmount, int offerAmount, ulong id)
        {
            if (requester < 1 || requester > 8 || recipient < 1 || recipient > 8 || requester == recipient || id == 0 || PriceFor(offered) == null || PriceFor(requested) == null || offered == requested || reqAmount <= 0 || offerAmount <= 0 || reqAmount > 100000 || offerAmount > 100000 || id >= ulong.MaxValue - 4)
                throw new ArgumentException("Invalid quote");
            return new BarterQuote
            {
                MapSession = _session,
                HostTerm = _term,
                Id = id,
                Requester = requester,
                Recipient = recipient,
                OfferedGood = offered,
                RequestedGood = requested,
                RequestedAmount = reqAmount,
                OfferedAmount = offerAmount,
                Revision = Revision,
                Day = Day,
                ExpiryDay = Day + 3
            };
        }

        public bool EconomicAdvantage(BarterQuote q, out decimal recipientGain, out decimal requesterGain)
        {
            recipientGain = 0m;
            requesterGain = 0m;
            if (q == null)
                return false;
            Good offered = PriceFor(q.OfferedGood), requested = PriceFor(q.RequestedGood);
            if (offered == null || requested == null || q.RequestedAmount <= 0 || q.OfferedAmount <= 0 || q.RequestedGood == q.OfferedGood)
                return false;
            try
            {
                decimal offer = offered.Mid * q.OfferedAmount;
                decimal sell = requested.MarketSell * q.RequestedAmount;
                decimal buy = requested.MarketBuy * q.RequestedAmount;
                decimal gain = offer - sell;
                decimal saving = buy - offer;
                if (gain <= saving || saving <= 0m)
                    return false;
                recipientGain = gain;
                requesterGain = saving;
                return true;
            }
            catch (OverflowException)
            {
                // A representable market unit price is not necessarily a
                // representable 100000-unit exchange. The full trade is
                // rejected without leaving one of its gain outputs populated.
                recipientGain = 0m;
                requesterGain = 0m;
                return false;
            }
        }

        public BarterOutcome Suggest(byte requester, byte recipient, ushort offerGood, ushort reqGood, int amount, ulong id)
        {
            BarterOutcome error = new BarterOutcome
            {
                Status = BarterStatus.Rejected,
                Message = "Kein beidseitiger Vorteil"
            };
            if (PriceFor(offerGood) == null || PriceFor(reqGood) == null || offerGood == reqGood || amount <= 0 || amount > 100000)
                return error;
            StockCell sender = _port.Read(requester, offerGood), give = _port.Read(recipient, reqGood), rReceive = _port.Read(requester, reqGood), aiReceive = _port.Read(recipient, offerGood);
            if (sender == null || give == null || rReceive == null || aiReceive == null)
                return error;
            if (give.Surplus < amount)
                return new BarterOutcome
                {
                    Status = BarterStatus.Rejected,
                    Message = "Kein tatsächlicher Überschuss"
                };
            if (rReceive.Space < amount)
                return new BarterOutcome
                {
                    Status = BarterStatus.Rejected,
                    Message = "Empfänger hat keinen Lagerplatz"
                };
            Good g = PriceFor(offerGood), t = PriceFor(reqGood);
            // Price snapshots can be native-authenticated yet numerically
            // expensive. Never cast unbounded decimal ratios to int BEFORE
            // checking the savedata/nativeside quantity limit (100000).
            int min, max, optimal;
            decimal bottom, top;
            try
            {
                bottom = t.MarketSell * amount;
                top = t.MarketBuy * amount;
                decimal mid = g.Mid;
                // Strict gain > saving requires a payment ABOVE the
                // center of the target good's buy/sell spread. Computing
                // only from sell may choose an economically invalid amount
                // even though a valid integer amount exists just above it.
                decimal threshold = bottom + (top - bottom) / 2m;
                decimal floor = decimal.Floor(threshold / mid) + 1m;
                decimal ceiling = decimal.Ceiling(top / mid) - 1m;
                if (floor > ceiling || floor > 100000m || ceiling < 1m)
                    return error;
                min = (int)Math.Max(1m, floor);
                max = (int)Math.Min(100000m, ceiling);
                decimal suggested = decimal.Round((bottom + 0.68m * (top - bottom)) / mid, 0, MidpointRounding.AwayFromZero);
                optimal = (int)Math.Max((decimal)min, Math.Min((decimal)max, suggested));
            }
            catch (OverflowException)
            {
                // Invalid extreme exchange rate: safely refuse a quote.
                return error;
            }

            if (optimal > sender.Surplus || optimal > aiReceive.Space)
                return new BarterOutcome
                {
                    Status = BarterStatus.Rejected,
                    Message = "Sicherheitsreserve oder Lagergrenze"
                };
            var q = Make(requester, recipient, offerGood, reqGood, amount, optimal, id);
            decimal rg, sg;
            if (!EconomicAdvantage(q, out rg, out sg))
                return error;
            return new BarterOutcome
            {
                Status = BarterStatus.CounterOffer,
                Message = "Fairer Kurs",
                Counter = q,
                OfferValue = g.Mid * optimal,
                SaleValue = bottom,
                BuyValue = top,
                RecipientPremium = rg,
                RequesterSaving = sg
            };
        }

        public BarterOutcome EvaluateAI(BarterQuote q, Ruler ai)
        {
            return EvaluateAICore(q, ai, -1);
        }

        // Optional diplomacy policy changes willingness only. This path shares
        // every price, reserve, capacity, exact-terms, replay and lease check
        // with the historical evaluator; it does not grant native authority.
        public BarterOutcome EvaluateAIWithPolicyProbability(BarterQuote q, Ruler ai, int probabilityBasisPoints)
        {
            if (probabilityBasisPoints < 0 || probabilityBasisPoints > 10000)
                return new BarterOutcome
                {
                    Status = BarterStatus.Rejected,
                    Message = "Ungültige diplomatische Entscheidungswahrscheinlichkeit"
                };
            return EvaluateAICore(q, ai, probabilityBasisPoints);
        }

        private BarterOutcome EvaluateAICore(BarterQuote q, Ruler ai, int policyProbability)
        {
            BarterOutcome result = new BarterOutcome
            {
                Status = BarterStatus.Rejected,
                Message = "Abgelehnt"
            };
            if (q == null)
                return Finish(q, result);
            lock (_sync)
            {
                CachedDecision previous;
                if (_decisions.TryGetValue(q.Id, out previous))
                    return previous.ExactTerms == Terms(q) ? CopyOutcome(previous.Outcome) : new BarterOutcome
                    {
                        Status = BarterStatus.Rejected,
                        Message = "Trade-ID bereits benutzt"
                    };
                if (_consumed.Contains(q.Id) || q.RootId != 0 && _consumed.Contains(q.RootId))
                    return new BarterOutcome
                    {
                        Status = BarterStatus.AlreadyConsumed,
                        Message = "Bereits verwendet"
                    };
            }

            if (ai == null || q == null || ai.PlayerId != q.Recipient || q.MapSession != _session || q.HostTerm != _term || q.Revision != Revision || q.Day > Day || q.ExpiryDay < Day)
                return Finish(q, result);
            decimal benefit, buyerGain;
            if (!EconomicAdvantage(q, out benefit, out buyerGain))
                return Finish(q, result);
            var requester = _port.Read(q.Requester, q.OfferedGood);
            var give = _port.Read(q.Recipient, q.RequestedGood);
            var receive = _port.Read(q.Recipient, q.OfferedGood);
            var buyerReceive = _port.Read(q.Requester, q.RequestedGood);
            if (requester == null || give == null || receive == null || buyerReceive == null)
                return Finish(q, result);
            if (requester.Surplus < q.OfferedAmount || give.Surplus < q.RequestedAmount || receive.Space < q.OfferedAmount || buyerReceive.Space < q.RequestedAmount)
            {
                result.Message = "Sicherheitsreserve oder Lagerplatz";
                return Finish(q, result);
            }

            decimal surplusFactor = Math.Min(1m, Math.Max(0m, (decimal)(give.Surplus - q.RequestedAmount) / Math.Max(1, 2 * q.RequestedAmount)));
            decimal shortage = Math.Min(1m, Math.Max(0m, ((decimal)receive.Reserve() + 20m - receive.Quantity) / Math.Max(1m, (decimal)receive.Reserve())));
            decimal edge = Math.Max(0m, benefit - buyerGain);
            decimal advantage = Math.Min(18m, edge / Math.Max(1m, q.RequestedAmount * PriceFor(q.RequestedGood).MarketSell) * 30m);
            decimal raw = 36m + 0.26m * (ai.Trust - 50) + 15m * surplusFactor + 22m * shortage + advantage - 0.35m * ai.Greed - 0.12m * ai.Caution + (ai.AtWar ? -24m : 0m) + 0.08m * ai.Commerce;
            int probability = (int)(100m * Math.Min(96m, Math.Max(4m, raw)));
            if (policyProbability >= 0)
                probability = policyProbability;
            int roll = DeterministicRoll(q, _worldSeed);
            result.AcceptanceProbabilityBasisPoints = probability;
            result.DeterministicRollBasisPoints = roll;
            result.SaleValue = q.RequestedAmount * PriceFor(q.RequestedGood).MarketSell;
            result.BuyValue = q.RequestedAmount * PriceFor(q.RequestedGood).MarketBuy;
            result.OfferValue = q.OfferedAmount * PriceFor(q.OfferedGood).Mid;
            result.RecipientPremium = benefit;
            result.RequesterSaving = buyerGain;
            result.Status = roll < probability ? BarterStatus.Accepted : BarterStatus.CounterOffer;
            result.Message = result.Status == BarterStatus.Accepted ? "KI akzeptiert" : "KI fordert Nachbesserung";
            if (result.Status == BarterStatus.CounterOffer)
            {
                // The counter must be a strictly better but STILL SAVABLE
                // quote.  raised an already 100000-unit offer to 100001
                // here, silently creating an invalid persisted trade.
                var suggested = Suggest(q.Requester, q.Recipient, q.OfferedGood, q.RequestedGood, q.RequestedAmount, q.Id);
                if (suggested.Counter != null)
                {
                    long increased = Math.Max((long)q.OfferedAmount + 1L, (long)suggested.Counter.OfferedAmount);
                    if (increased <= 100000L && requester.Surplus >= increased && receive.Space >= increased)
                    {
                        BarterQuote proposed = suggested.Counter.Copy();
                        proposed.OfferedAmount = (int)increased;
                        proposed.RootId = q.Id;
                        proposed.Id = q.Id + 1;
                        decimal rg, sg;
                        if (EconomicAdvantage(proposed, out rg, out sg))
                        {
                            lock (_sync)
                            {
                                if (!_decisions.ContainsKey(proposed.Id) && !_pendingCounters.ContainsKey(proposed.Id))
                                {
                                    _pendingCounters[proposed.Id] = Terms(proposed);
                                    result.Counter = proposed;
                                }
                            }
                        }
                    }
                }

                if (result.Counter == null)
                {
                    // No larger reserve-safe and market-safe counter exists.
                    // The proper outcome is rejection, not a phantom prompt.
                    result.Status = BarterStatus.Rejected;
                    result.Message = "Kein gültiges Gegenangebot innerhalb der Waren- und Lagergrenzen";
                }
            }
            else
            {
                lock (_sync)
                {
                    _approved[q.Id] = Terms(q);
                }
            }

            return Finish(q, result);
        }

        private static int DeterministicRoll(BarterQuote q, int seed)
        {
            string text = string.Format(CultureInfo.InvariantCulture, "{0:N}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}", q.MapSession, q.HostTerm, q.Id, q.Requester, q.Recipient, q.RequestedGood, q.OfferedGood, q.RequestedAmount, q.OfferedAmount, seed);
            using (SHA256 h = SHA256.Create())
            {
                var a = h.ComputeHash(Encoding.UTF8.GetBytes(text));
                uint word = (uint)a[0] | ((uint)a[1] << 8) | ((uint)a[2] << 16) | ((uint)a[3] << 24);
                return (int)(word % 10000);
            }
        }

        public bool TrySettle(BarterQuote q, out string reason)
        {
            lock (_sync)
            {
                reason = "Unbekannt";
                if (_quarantined)
                {
                    reason = "Quarantäne wegen unvollständiger Rücknahme";
                    return false;
                }

                if (q == null || q.Id == 0 || q.MapSession != _session || q.HostTerm != _term || q.Revision != Revision || q.Day > Day || q.ExpiryDay < Day)
                {
                    reason = "Sitzungsdaten veraltet";
                    return false;
                }

                if (_consumed.Contains(q.Id) || q.RootId != 0 && _consumed.Contains(q.RootId))
                {
                    reason = "Bereits verbraucht";
                    return false;
                }

                string approved;
                if (!_approved.TryGetValue(q.Id, out approved) || approved != Terms(q))
                {
                    reason = "KI-Zustimmung zu diesen exakten Terms fehlt";
                    return false;
                }

                if (!_port.HasExclusiveSinglePlayerLease(_session, _term))
                {
                    reason = "Kein exklusiver Simulations-Lock";
                    return false;
                }

                decimal recipientGain, requesterSaving;
                if (!EconomicAdvantage(q, out recipientGain, out requesterSaving))
                {
                    reason = "Unfairer Kurs";
                    return false;
                }

                var parts = new[]
                {
                    Tuple.Create(q.Requester, q.OfferedGood, -q.OfferedAmount),
                    Tuple.Create(q.Recipient, q.OfferedGood, +q.OfferedAmount),
                    Tuple.Create(q.Recipient, q.RequestedGood, -q.RequestedAmount),
                    Tuple.Create(q.Requester, q.RequestedGood, +q.RequestedAmount)
                };
                var old = new StockCell[4];
                var after = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    for (int k = 0; k < i; k++)
                        if (parts[i].Item1 == parts[k].Item1 && parts[i].Item2 == parts[k].Item2)
                        {
                            reason = "Überlappende Warenzellen";
                            return false;
                        }

                    old[i] = _port.Read(parts[i].Item1, parts[i].Item2);
                    if (old[i] == null)
                    {
                        reason = "Warenbestand fehlt";
                        return false;
                    }

                    long n = (long)old[i].Quantity + parts[i].Item3;
                    if (n < 0 || n > old[i].Capacity || parts[i].Item3 < 0 && n < old[i].Reserve())
                    {
                        reason = "Reserve oder Kapazität verletzt";
                        return false;
                    }

                    after[i] = (int)n;
                }

                // A real game requires durable 4-cell engine SaveData approval.
                // This path is ONLY an isolated in-memory singleplayer simulator.
                // WriteAbsolute is allowed to mutate a cell and THEN throw.
                // Therefore mark each attempted cell BEFORE invoking the port.
                if (Revision == int.MaxValue)
                {
                    reason = "Simulationsrevision erschöpft";
                    return false;
                }

                int touched = 0, revisionBefore = Revision;
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        StockCell latest = _port.Read(parts[i].Item1, parts[i].Item2);
                        if (!_port.HasExclusiveSinglePlayerLease(_session, _term) || latest == null || latest.Quantity != old[i].Quantity)
                            throw new InvalidOperationException("Lease/stock changed");
                        touched = i + 1;
                        _port.WriteAbsolute(parts[i].Item1, parts[i].Item2, after[i]);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        StockCell readback = _port.Read(parts[i].Item1, parts[i].Item2);
                        if (readback == null || readback.Quantity != after[i])
                            throw new InvalidOperationException("Simulation readback changed");
                    }

                    _consumed.Add(q.Id);
                    if (q.RootId != 0)
                        _consumed.Add(q.RootId);
                    _approved.Clear();
                    _pendingCounters.Clear();
                    _decisions.Clear();
                    Revision++;
                    reason = "Simulatorgebucht";
                    return true;
                }
                catch (Exception ex)
                {
                    // Attempt ALL possibly touched cells in reverse order even
                    // if one rollback write itself throws. Verify the full
                    // four-cell pre-state before ever reporting "rolled back".
                    bool restored = true;
                    for (int j = touched - 1; j >= 0; j--)
                    {
                        try
                        {
                            _port.WriteAbsolute(parts[j].Item1, parts[j].Item2, old[j].Quantity);
                        }
                        catch (Exception)
                        {
                            restored = false;
                        }
                    }

                    for (int j = 0; j < 4; j++)
                    {
                        try
                        {
                            StockCell readback = _port.Read(parts[j].Item1, parts[j].Item2);
                            if (readback == null || readback.Quantity != old[j].Quantity)
                                restored = false;
                        }
                        catch (Exception)
                        {
                            restored = false;
                        }
                    }

                    if (restored)
                    {
                        try
                        {
                            // Restore an ID in case a managed bookkeeping
                            // failure arose AFTER all four cell readbacks.
                            _consumed.Remove(q.Id);
                            if (q.RootId != 0)
                                _consumed.Remove(q.RootId);
                            Revision = revisionBefore;
                        }
                        catch (Exception)
                        {
                            restored = false;
                        }
                    }

                    if (restored)
                        reason = "Zurückgerollt: " + ex.GetType().Name;
                    else
                    {
                        _quarantined = true;
                        reason = "Quarantäne: Native-ähnliche Simulationsrücknahme nicht bestätigt";
                    }

                    return false;
                }
            }
        }

        public void NextDay()
        {
            lock (_sync)
            {
                Day++;
                Revision++;
                _approved.Clear();
                _pendingCounters.Clear();
                _decisions.Clear();
            }
        }

        public void Retire(ulong id)
        {
            lock (_sync)
            {
                _consumed.Add(id);
                _approved.Clear();
                _pendingCounters.Clear();
                _decisions.Clear();
                Revision++;
            }
        }
    }
}
