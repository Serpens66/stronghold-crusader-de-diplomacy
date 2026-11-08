// Deterministic AI proposals address a human inbox; they never manufacture a
// human acceptance. All offers use the same canonical ID/clock/native slot.
using System;
using System.Collections.Generic;
using System.Linq;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        private static int CanonicalObservedReplyDelay(CanonicalModel m, CanonicalTradeSnapshot world, ulong id, byte a, byte b, int amount, TradeIntentAction action)
        {
            ObservedTradePairState pair;
            var ruler = world.Rulers(false).Single(x => x.PlayerId == b);
            if (!world.TryGetPair(a, b, m.Day, out pair))
                throw new InvalidOperationException("No observed response context");
            return NegotiationTiming.Delay(new ReplyContext { Id = id, Day = m.Day, Requester = a, Recipient = b, RequestedAmount = amount, Action = (int)action, Trust = pair.Trust, Grudge = pair.Grudge, Threat = pair.Threat, Urgency = Math.Max(pair.Threat, ruler.AtWar ? 75 : 0), Caution = ruler.Caution, Commerce = ruler.Commerce, RecipientAtWar = ruler.AtWar, PendingNegotiations = m.Mailbox.Intents.Count(x => x.State == TradeIntentStatus.Waiting || x.State == TradeIntentStatus.Processing) });
        }

        private static bool TerminalConsent(CanonicalHumanOffer x)
        {
            return x.State != CanonicalHumanConsentState.Pending && x.State != CanonicalHumanConsentState.Accepted && x.State != CanonicalHumanConsentState.PreparingNative;
        }

        private static void CompactTerminalHumanOffers(CanonicalModel model)
        {
            if (model.HumanOffers.Count < 1536)
                return;
            // Remove whole terminal chains only. Native records, pending intent terms
            // and all ancestor/root references retain their complete consent history.
            var referenced = new HashSet<ulong>(model.Archive.Records.Select(x => x.Id));
            foreach (var i in model.Mailbox.Intents)
            {
                referenced.Add(i.Id);
                referenced.Add(i.CounterId);
                referenced.Add(i.CounterRootId);
            }

            var groups = model.HumanOffers.GroupBy(x => x.OriginalId).Where(g => g.All(x => TerminalConsent(x) && !referenced.Contains(x.Id))).OrderBy(g => g.Max(x => x.ExpiresClock)).ThenBy(g => g.Key).ToArray();
            foreach (var group in groups)
            {
                if (model.HumanOffers.Count < 1536)
                    break;
                var ids = new HashSet<ulong>(group.Select(x => x.Id));
                model.HumanOffers.RemoveAll(x => ids.Contains(x.Id));
            }
        // Archive.NextId and consumed input sequences/nonces are never rewound.
        }

        private static BarterQuote HumanQuote(CanonicalModel m, CanonicalHumanOffer o)
        {
            return new BarterQuote
            {
                MapSession = m.MatchScope,
                HostTerm = m.MatchTerm,
                Revision = 1,
                Id = o.Id,
                RootId = 0,
                Requester = o.From,
                Recipient = o.To,
                OfferedGood = o.OfferedGood,
                RequestedGood = o.RequestedGood,
                OfferedAmount = o.OfferedAmount,
                RequestedAmount = o.RequestedAmount,
                Day = o.IssuedDay,
                ExpiryDay = o.ExpiresDay
            };
        }

        private static void ScheduleAiHumanOffers(CanonicalModel model, CanonicalTradeSnapshot snapshot,
            CanonicalAiInitiativeBudget budget)
        {
            if (model.InputEpoch == 0 || model.HumanOffers.Count >= MaximumCanonicalHumanOffers)
                return;
            ManagedGoodsSnapshotPort memory;
            var economy = BuildWorld(model, snapshot, false, out memory);
            foreach (var ai in snapshot.Players().Where(p => !p.Human && p.Alive).OrderBy(p => p.Slot))
            {
                bool created = false;
                foreach (var human in snapshot.Players().Where(p => p.Human && p.Alive).OrderBy(p => p.Slot))
                {
                    if (HumanPairBusy(model, ai.Slot, human.Slot) || Busy(model, ai.Slot, human.Slot) || Busy(model, human.Slot, ai.Slot) || !CanReserve(model))
                        continue;
                    ObservedTradePairState direct, reverse;
                    if (!snapshot.TryGetPair(ai.Slot, human.Slot, model.Day, out direct) || !snapshot.TryGetPair(human.Slot, ai.Slot, model.Day, out reverse))
                        continue;
                    var wanted = GoodCatalog.All.OrderByDescending(g => economy.NeedScore(ai.Slot, g.Id)).ThenBy(g => g.Id);
                    foreach (var want in wanted)
                    {
                        if (economy.NeedScore(ai.Slot, want.Id) <= 0)
                            continue;
                        var ownNeed = snapshot.Read(ai.Slot, want.Id);
                        if ((long)ownNeed.Quantity >= (long)ownNeed.Reserve() + 30L)
                            continue;
                        int originalDemandBound = AiMaximumDraftQuantity(snapshot,ai.Slot,want.Id);
                        int amount = Math.Min(originalDemandBound,Math.Min(25,
                            Math.Min(snapshot.Read(human.Slot, want.Id).Surplus,
                                snapshot.Read(ai.Slot, want.Id).Space)));
                        if (amount < 3)
                            continue;
                        foreach (var give in GoodCatalog.All.Where(g => g.Id != want.Id).OrderByDescending(g => economy.NeedScore(human.Slot, g.Id)).ThenBy(g => g.Id))
                        {
                            int offered = economy.QuoteOfferedAmount(give.Id, want.Id, amount, TradeOfferGenerosity.Fair);
                            if (offered < 1)
                                continue;
                            var candidate = new CanonicalHumanOffer
                            {
                                Id = model.Archive.NextId,
                                From = ai.Slot,
                                To = human.Slot,
                                OfferedGood = give.Id,
                                RequestedGood = want.Id,
                                OfferedAmount = offered,
                                RequestedAmount = amount,
                                Tier = (byte)TradeOfferGenerosity.Fair,
                                IssuedDay = model.Day,
                                ExpiresDay = model.Day + HumanOfferLifetimeDays,
                                IssuedClock = model.Mailbox.Clock,
                                ExpiresClock = checked(model.Mailbox.Clock + HumanOfferLifetimeTicks),
                                State = CanonicalHumanConsentState.Pending,
                                AiGenerated = true
                            };
                            var quote = HumanQuote(model, candidate);
                            ExactTradeCell[] cells;
                            if (!TryCells(snapshot, quote, out cells) || !economy.IsMutuallyBeneficial(quote))
                                continue;
                            AiProcurementDecision policyDecision;
                            if (!CanScheduleAiEconomicCandidate(model,snapshot,budget,
                                    ai.Slot,human.Slot,give.Id,want.Id,amount,offered,
                                    out policyDecision))
                                continue;
                            // mod ai offer: this is a MOD-authored diplomatic offer
                            // derived from original AIC numeric demand; it is
                            // NOT a vanilla original-CPU diplomatic Chore.
                            // Check the real same-frame original AIC identity,
                            // map/roster and unconsumed offer ID BEFORE issuing.
                            string originReason;
                            if (!ModAiHumanOfferOrigin.TryClassifyModAiDraft(
                                    ModAiOriginFacts(model,snapshot,candidate),
                                    out originReason))
                                continue;
                            candidate.Id = Reserve(model);
                            model.HumanOffers.Add(candidate);
                            budget.ObserveNewlySaved(new AiInitiativeFact(candidate.Id,
                                ai.Slot,human.Slot,model.Day,amount,true));
                            created = true;
                            break;
                        }

                        if (created)
                            break;
                    }

                    if (created)
                        break; // At most one actual initiative per AI and saved day.
                }

                if (model.HumanOffers.Count >= MaximumCanonicalHumanOffers)
                    break;
            }
        }

        private static void ProcessAiConsentReplies(CanonicalModel model, CanonicalTradeSnapshot snapshot)
        {
            if (model.Quarantined)
                return;
            foreach (var offer in model.HumanOffers.Where(x => x.State == CanonicalHumanConsentState.Pending && !Human(model, x.To) && x.AiDecisionDueClock > 0 && x.AiDecisionDueClock <= model.Mailbox.Clock).OrderBy(x => x.AiDecisionDueClock).ThenBy(x => x.Id).ToArray())
            {
                var root = model.HumanOffers.SingleOrDefault(x => x.Id == offer.RootId);
                if (root == null || !root.AiGenerated || root.From != offer.To || root.To != offer.From)
                    throw new InvalidOperationException("AI counter response has no original initiative");
                ObservedTradePairState pair;
                bool accepts = false;
                if (snapshot.Player(offer.From).Alive && snapshot.Player(offer.To).Alive && snapshot.TryGetPair(offer.From, offer.To, model.Day, out pair))
                {
                    ManagedGoodsSnapshotPort memory;
                    var economy = BuildWorld(model, snapshot, false, out memory);
                    var quote = HumanQuote(model, offer);
                    ExactTradeCell[] cells;
                    // Reuse the existing AI negotiation personality/roll; only its decision
                    // is observed. No detached simulated inventory/goodwill is adopted.
                    var answer = economy.Propose(offer.From, offer.To, offer.OfferedGood, offer.RequestedGood, offer.RequestedAmount, (TradeOfferGenerosity)offer.Tier, offer.Id, 0, false);
                    accepts = answer.Status == DiplomaticTradeOutcome.Accepted && answer.Quote != null && offer.OfferedAmount >= answer.Quote.OfferedAmount && economy.IsMutuallyBeneficial(quote) && TryCells(snapshot, quote, out cells);
                }

                offer.DecisionSlot = offer.To;
                offer.DecisionClock = model.Mailbox.Clock;
                offer.DecisionIsAi = true;
                offer.State = accepts ? CanonicalHumanConsentState.Accepted : CanonicalHumanConsentState.Declined;
            }
        }
    }
}
