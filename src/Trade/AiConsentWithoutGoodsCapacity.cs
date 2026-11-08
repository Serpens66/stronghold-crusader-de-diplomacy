// Eight actions share the selected  stream. AI economic decisions wait
// explicitly for real MP capacity; declining a frozen AI counter needs no stock.
using System;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        private static void ReceiveAiWithoutCapacity(CanonicalModel model, OriginalMultiplayerConsentFrame world, OrderedHumanTrade x)
        {
            string why;
            var recipient = world.Players().FirstOrDefault(p => p.Slot == x.Recipient);
            if (recipient == null || recipient.Human || !world.TryConsentPair(x.Requester, x.Recipient, out why) || !CanReserve(model) || model.Mailbox.HasPending(x.Requester, x.Recipient))
                return;
            TradeRecord counter = null;
            var action = (TradeIntentAction)(byte)x.Action;
            if (x.Action == CanonicalTradeAction.Propose)
            {
                if (Busy(model, x.Requester, x.Recipient))
                    return;
            }
            else
            {
                counter = model.Archive.Record(x.CounterId);
                if (counter == null || counter.State != TradeDisposition.PendingCounter || counter.AiToAi || counter.ExpiryDay < model.Day)
                    return;
                var exact = new TradeIntent
                {
                    Requester = x.Requester,
                    Recipient = x.Recipient,
                    Action = action,
                    OfferedGood = x.OfferedGood,
                    RequestedGood = x.WantedGood,
                    RequestedAmount = x.Amount,
                    Generosity = x.Tier,
                    CounterId = x.CounterId,
                    CounterRootId = x.CounterRootId,
                    CounterOfferedAmount = x.CounterOfferedAmount,
                    CounterCreatedDay = x.CounterCreatedDay,
                    CounterExpiresDay = x.CounterExpiresDay
                };
                if (!exact.MatchesCounter(counter))
                    return;
            }

            long clock = model.Mailbox.Clock;
            var intent = new TradeIntent
            {
                Id = Reserve(model),
                Requester = x.Requester,
                Recipient = x.Recipient,
                OfferedGood = x.OfferedGood,
                RequestedGood = x.WantedGood,
                RequestedAmount = x.Amount,
                Generosity = x.Tier,
                IssuedDay = model.Day,
                Action = action,
                State = TradeIntentStatus.Waiting,
                EnqueuedClock = clock,
                DueClock = checked(clock + 1),
                ExpiryClock = checked(clock + TradeIntentMailbox.MaximumLifetimeTicks),
                Outcome = "mp_original_capacity_unknown_waiting"
            };
            if (counter != null)
                BindCounter(intent, counter);
            model.Mailbox.Enqueue(intent, model.Day);
            if (action == TradeIntentAction.DeclineCounter)
            {
                var saved = model.Mailbox.Intents.Single(z => z.Id == intent.Id);
                CancelBoundCounter(model, saved);
                Finish(saved, "declined_by_authenticated_human");
            }
        }
    }
}
