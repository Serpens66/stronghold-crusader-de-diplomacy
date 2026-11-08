// Real selected Format3 human-human consent semantics WITHOUT native stock.
// Frozen counter lineage, actor decision attestations and global NextId
// are exactly the existing controller's ONE ledger, not an alternate owner.
using System;
using System.Linq;
using System.IO;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        private static void ReceiveConsentWithoutStock(CanonicalModel m, OriginalMultiplayerConsentFrame world, OrderedHumanTrade x)
        {
            if (x == null || m.InputEpoch == 0 || x.Epoch != m.InputEpoch || m.Quarantined)
                return;
            string reason;
            if (!world.TryConsentPair(x.Requester, x.Recipient, out reason))
                return;
            // Only verified human-human pairs can create capacity-free quotes.
            // Human-AI stock/economic decisions remain unavailable.
            var recipient = world.Players().FirstOrDefault(z => z.Slot == x.Recipient);
            if (recipient == null || !recipient.Human)
                return;
            if (x.Action == CanonicalTradeAction.ProposeHuman)
            {
                if (HumanPairBusy(m, x.Requester, x.Recipient) || Busy(m, x.Requester, x.Recipient) || m.HumanOffers.Count >= MaximumCanonicalHumanOffers || x.CounterId != 0 || x.CounterRootId != 0 || x.CounterOfferedAmount != 0 || x.CounterCreatedDay != 0 || x.CounterExpiresDay != 0 || x.HumanOfferedAmount < 1 || x.HumanOfferedAmount > 100000 || x.Amount < 1 || x.Amount > 100000 || x.OfferedGood == x.WantedGood || GoodCatalog.Find(x.OfferedGood) == null || GoodCatalog.Find(x.WantedGood) == null || x.Tier > 2 || !CanReserve(m))
                    return;
                ulong id = Reserve(m);
                m.HumanOffers.Add(new CanonicalHumanOffer { Id = id, RootId = 0, ParentId = 0, From = x.Requester, To = x.Recipient, Tier = x.Tier, Depth = 0, OfferedGood = x.OfferedGood, RequestedGood = x.WantedGood, OfferedAmount = x.HumanOfferedAmount, RequestedAmount = x.Amount, IssuedDay = m.Day, ExpiresDay = Math.Min(int.MaxValue - 32, m.Day + HumanOfferLifetimeDays), IssuedClock = m.Mailbox.Clock, ExpiresClock = checked(m.Mailbox.Clock + HumanOfferLifetimeTicks), State = CanonicalHumanConsentState.Pending });
                return;
            }

            CanonicalHumanOffer current = m.HumanOffers.FirstOrDefault(z => z.Id == x.CounterId);
            if (current == null || current.State != CanonicalHumanConsentState.Pending || m.Day > current.ExpiresDay || m.Mailbox.Clock > current.ExpiresClock || !current.MatchesHumanTerms(x))
                return;
            if (x.Action == CanonicalTradeAction.WithdrawHuman)
            {
                if (current.From != x.Requester || current.To != x.Recipient || !current.MatchesIdenticalConsent(x))
                    return;
                MarkDecision(m, current, x, CanonicalHumanConsentState.Withdrawn);
                return;
            }

            if (current.From != x.Recipient || current.To != x.Requester)
                return;
            if (x.Action == CanonicalTradeAction.DeclineHuman || x.Action == CanonicalTradeAction.AcceptHuman)
            {
                if (!current.MatchesIdenticalConsent(x))
                    return;
                MarkDecision(m, current, x, x.Action == CanonicalTradeAction.AcceptHuman ? CanonicalHumanConsentState.Accepted : CanonicalHumanConsentState.Declined);
                // Accepted is saved USER CONSENT only. NO PreparingNative.
                return;
            }

            if (x.Action != CanonicalTradeAction.CounterHuman || m.HumanOffers.Count >= MaximumCanonicalHumanOffers || current.Depth >= MaximumHumanCounterDepth || current.ExpiresClock <= m.Mailbox.Clock || x.HumanOfferedAmount < 1 || x.HumanOfferedAmount > 100000 || x.Amount < 1 || x.Amount > 100000 || x.OfferedGood == x.WantedGood || GoodCatalog.Find(x.OfferedGood) == null || GoodCatalog.Find(x.WantedGood) == null || x.Tier > 2 || !CanReserve(m))
                return;
            ulong counterId = Reserve(m);
            MarkDecision(m, current, x, CanonicalHumanConsentState.Superseded);
            m.HumanOffers.Add(new CanonicalHumanOffer { Id = counterId, RootId = current.OriginalId, ParentId = current.Id, From = x.Requester, To = x.Recipient, Tier = x.Tier, Depth = (byte)(current.Depth + 1), OfferedGood = x.OfferedGood, RequestedGood = x.WantedGood, OfferedAmount = x.HumanOfferedAmount, RequestedAmount = x.Amount, IssuedDay = m.Day, ExpiresDay = Math.Min(current.ExpiresDay, Math.Min(int.MaxValue - 32, m.Day + HumanOfferLifetimeDays)), IssuedClock = m.Mailbox.Clock, ExpiresClock = Math.Min(current.ExpiresClock, checked(m.Mailbox.Clock + HumanOfferLifetimeTicks)), State = CanonicalHumanConsentState.Pending });
        }

        private static void MarkDecision(CanonicalModel m, CanonicalHumanOffer current, OrderedHumanTrade x, CanonicalHumanConsentState result)
        {
            current.DecisionSlot = x.Requester;
            current.DecisionSequence = x.Sequence;
            current.DecisionNonce = x.ClientNonce;
            current.DecisionClock = m.Mailbox.Clock;
            current.State = result;
        }
    }
}
