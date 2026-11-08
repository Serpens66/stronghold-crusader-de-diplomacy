// Pure, deterministic reconstruction of AI diplomacy initiative budget from
// the ONE already-saved canonical mailbox / archive / human-offer ledger.
// No second state to serialize and no network / native / market side effects.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class AiInitiativeFact
    {
        internal readonly ulong Id;
        internal readonly byte From, To;
        internal readonly int Day, RequestedUnits;
        internal readonly bool ToHuman;
        internal AiInitiativeFact(ulong id, byte from, byte to, int day, int units, bool human)
        {
            if (id == 0 || from < 1 || from > 8 || to < 1 || to > 8 ||
                from == to || day < 1 || day > int.MaxValue - 32 ||
                units < 1 || units > 100000 || human && to == from)
                throw new ArgumentException("Invalid previously issued AI initiative");
            Id = id; From = from; To = to; Day = day; RequestedUnits = units; ToHuman = human;
        }
        internal bool Same(AiInitiativeFact other)
        {
            return other != null && Id == other.Id && From == other.From &&
                To == other.To && Day == other.Day &&
                RequestedUnits == other.RequestedUnits && ToHuman == other.ToHuman;
        }
    }

    internal sealed class AiInitiativeBudgetView
    {
        internal readonly int LastLordDay, LastPairDay, LastInboxDay;
        internal readonly int LordOffersToday, InboxOffersToday, RemainingUnits;
        internal AiInitiativeBudgetView(int lord, int pair, int inbox,
            int lordToday, int inboxToday, int units)
        {
            LastLordDay = lord; LastPairDay = pair; LastInboxDay = inbox;
            LordOffersToday = lordToday; InboxOffersToday = inboxToday;
            RemainingUnits = units;
        }
    }

    internal sealed class CanonicalAiInitiativeBudget
    {
        // Two-day or multi-day personality cooldown is enforced separately by
        // AiProcurementPolicy. These are HARD same-day safety limits.
        internal const int MaxLordUnitsPerDay = 24;
        internal const int MaxAllAiUnitsPerDay = 64;
        internal const int MaxHumanInboxOffersPerDay = 2;
        private readonly Dictionary<ulong,AiInitiativeFact> _entries = new Dictionary<ulong,AiInitiativeFact>();
        private readonly int _currentDay;
        // One bounded pass during original day scheduling / cold reconstruction,
        // then O(1) lookups per candidate (never scan the saved ledger again).
        private readonly int[] _lastLord = new int[9], _lastInbox = new int[9];
        private readonly int[] _lordOffers = new int[9], _inboxOffers = new int[9];
        private readonly int[,] _lastPair = new int[9,9];
        private readonly long[] _lordUnits = new long[9];
        private long _allAiUnits;

        internal CanonicalAiInitiativeBudget(int day, IEnumerable<AiInitiativeFact> evidence)
        {
            if (day < 1 || day > int.MaxValue - 32 || evidence == null)
                throw new ArgumentException("Invalid saved AI economic budget");
            _currentDay = day;
            for (int a = 0; a < 9; a++)
            {
                _lastLord[a] = _lastInbox[a] = -1;
                for (int b = 0; b < 9; b++)
                    _lastPair[a,b] = -1;
            }
            foreach (AiInitiativeFact fact in evidence)
                AdmitSaved(fact);
        }

        private void AdmitSaved(AiInitiativeFact fact)
        {
            if (fact == null || fact.Day > _currentDay)
                throw new InvalidOperationException("Future/invalid persisted AI initiative");
            AiInitiativeFact previous;
            if (_entries.TryGetValue(fact.Id, out previous))
            {
                if (!previous.Same(fact))
                    throw new InvalidOperationException("Conflicting saved AI initiative ID");
                return; // identical archive + mailbox evidence refers to one root
            }
            if (_entries.Count >= 15000)
                throw new InvalidOperationException("Unbounded archived AI initiative history");
            _entries.Add(fact.Id,fact);
            _lastLord[fact.From] = Math.Max(_lastLord[fact.From],fact.Day);
            _lastPair[fact.From,fact.To] = Math.Max(
                _lastPair[fact.From,fact.To],fact.Day);
            if (fact.ToHuman)
                _lastInbox[fact.To] = Math.Max(_lastInbox[fact.To],fact.Day);
            if (fact.Day == _currentDay)
            {
                _lordOffers[fact.From]++;
                _lordUnits[fact.From] += fact.RequestedUnits;
                _allAiUnits += fact.RequestedUnits;
                if (fact.ToHuman)
                    _inboxOffers[fact.To]++;
            }
        }

        internal void ObserveNewlySaved(AiInitiativeFact fact)
        {
            if (fact == null || fact.Day != _currentDay || _entries.ContainsKey(fact.Id))
                throw new InvalidOperationException("Invalid or replayed new AI initiative");
            AdmitSaved(fact); // only AFTER existing canonical owner has reserved an ID
        }

        internal AiInitiativeBudgetView View(byte requester, byte recipient)
        {
            if (requester < 1 || requester > 8 || recipient < 1 ||
                recipient > 8 || requester == recipient)
                throw new ArgumentException("Invalid actual requester/recipient");
            int free=(int)Math.Max(0L,Math.Min(
                MaxLordUnitsPerDay-_lordUnits[requester],
                MaxAllAiUnitsPerDay-_allAiUnits));
            return new AiInitiativeBudgetView(
                _lastLord[requester],_lastPair[requester,recipient],
                _lastInbox[recipient],_lordOffers[requester],
                _inboxOffers[recipient],free);
        }
    }
}
