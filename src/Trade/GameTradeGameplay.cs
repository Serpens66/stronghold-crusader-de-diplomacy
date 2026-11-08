// DIPLOMACY ExtendedState : game-bound UI / spontaneous AI negotiations.
// All inputs come from the actual selected game slots and inventory snapshot.
// No direct UI/native writes; authorized settlement goes via the two-line
// native inventory journal ExecuteNativeBarter (strictly gated).
using System;
using System.Linq;
using System.Collections.Generic;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private ulong _uiCounterId;
        private int _uiCounterpart;
        private string _uiReceipt = string.Empty;
        private void ResetTradeUiState()
        {
            _uiCounterId = 0;
            _uiCounterpart = 0;
            _uiReceipt = string.Empty;
        }

        private TradeRecord FromQuote(BarterQuote q, TradeDisposition state, TradeOfferGenerosity tier, bool auto, int chance, int roll)
        {
            if (q == null)
                return null;
            return new TradeRecord
            {
                Id = q.Id,
                RootId = q.RootId,
                Requester = q.Requester,
                Recipient = q.Recipient,
                OfferedGood = q.OfferedGood,
                RequestedGood = q.RequestedGood,
                OfferedAmount = q.OfferedAmount,
                RequestedAmount = q.RequestedAmount,
                IssuedDay = q.Day,
                ExpiryDay = q.ExpiryDay,
                Generosity = (byte)tier,
                State = state,
                AiToAi = auto,
                ChanceBasisPoints = chance,
                RollBasisPoints = roll
            };
        }

        private BarterQuote QuoteFromSaved(TradeRecord rec)
        {
            if (rec == null)
                throw new ArgumentNullException("rec");
            return new BarterQuote
            {
                Id = rec.Id,
                RootId = rec.RootId,
                Requester = rec.Requester,
                Recipient = rec.Recipient,
                OfferedGood = rec.OfferedGood,
                RequestedGood = rec.RequestedGood,
                OfferedAmount = rec.OfferedAmount,
                RequestedAmount = rec.RequestedAmount,
                Day = rec.IssuedDay,
                ExpiryDay = rec.ExpiryDay,
                Revision = 1,
                HostTerm = SinglePlayerTerm,
                MapSession = MapGuid(_mapFingerprint, _rosterFingerprint)
            };
        }

        private void AdoptRejected(DiplomaticTradeNegotiator world, NegotiatedTradeState result)
        {
            // Adopt ONLY the rejected pair. An autonomous AI day's offline
            // simulator may also contain accepted trades which must NOT credit
            // relations until their *real* native four-cell journal commits.
            // Copying every simulator relation here could credit uncommitted
            // AI-to-AI deals or overwrite a real receipt's relationship.
            if (world == null || result == null || result.Status != DiplomaticTradeOutcome.Rejected)
                return;
            ManagedTradeRelationship model = world.Relationships.Observe(result.Requester, result.Recipient);
            TradeRelationship saved = _archive.Relation(result.Requester, result.Recipient);
            saved.Goodwill = model.Goodwill;
            saved.RejectionStreak = model.ConsecutiveRejected;
            saved.LastRejectedDay = model.LastRejectedDay;
            saved.LastAttemptDay = model.LastAttemptDay;
            saved.LastPenaltyDay = model.LastPenaltyDay;
            saved.RejectedToday = model.RejectedToday;
            saved.AcceptedTrades = model.AcceptedTrades;
            saved.LastDealDay = model.LastDealDay;
            if (result.Requester == _localPlayer || result.Recipient == _localPlayer)
            {
                int other = result.Requester == _localPlayer ? result.Recipient : result.Requester;
                ApplyDiplomacyTradeEffect(other, false, result.RelationBefore, result.RelationAfter);
            }
        }

        private void RecordActualPreTradeGoodwill(NegotiatedTradeState model)
        {
            if (model == null)
                return;
            TradeRelationship rel = _archive.Relation(model.Requester, model.Recipient);
            // If no trade has occurred yet in this pair, initialise to the
            // real ExtendedState trust used by this particular model. The first native
            // commit then adds the +6 exactly once.
            if (rel.AcceptedTrades == 0 && rel.LastAttemptDay == -100)
                rel.Goodwill = model.RelationBefore;
        }

        private void PositiveBooked(TradeRecord record)
        {
            // A successful native commit must never silently lose its
            // corresponding managed receipt or diplomatic effect.
            if (record == null || _archive == null)
                throw new System.IO.InvalidDataException("Missing trusted native trade receipt/archive");
            TradeRelationship relation = _archive.Relation(record.Requester, record.Recipient);
            int before = relation.Goodwill;
            // every managed change is checked first; the current
            // ExtendedState memento can undo it if Native commit subsequently fails.
            TradeRelationshipRules.CommitSuccessfulTrade(relation, Math.Max(1, _lastProcessedDay));
            int other = record.Requester == _localPlayer ? record.Recipient : record.Recipient == _localPlayer ? record.Requester : 0;
            if (other > 0)
                ApplyDiplomacyTradeEffect(other, true, before, relation.Goodwill);
        }

        private bool ProcessSelectedTrade(string action, int selectedPlayer)
        {
            if (action != "Send" && action != "Accept" && action != "Decline")
                return false;
            // Commands enqueue first. This execution path requires a claimed,
            // persisted, due intent whose immutable terms belong to this pair.
            if (_executingIntent == null)
            {
                QueueSelectedTrade(action, selectedPlayer);
                return true;
            }

            TradeIntent scheduled = _executingIntent;
            if (scheduled.State != TradeIntentStatus.Processing || scheduled.Recipient != selectedPlayer || scheduled.DueClock > _mailbox.Clock)
                return true;
            string pairDenial;
            if (!CanTradePair(scheduled.Requester, scheduled.Recipient, out pairDenial))
            {
                _tradeStatus = pairDenial;
                return true;
            }

            TradeOfferGenerosity tier = (TradeOfferGenerosity)scheduled.Generosity;
            // Even a successfully rendered button cannot grant native writes.
            if (!NativeAuthorized())
            {
                _tradeStatus = Text("Handel ist für diese Partie noch nicht verfügbar.", "Trading is not available for this match yet.");
                return true;
            }

            if (selectedPlayer < 1 || selectedPlayer > 8 || selectedPlayer == _localPlayer || !_roster.Contains(selectedPlayer))
            {
                _tradeStatus = Text("Ungültiger Spielerslot", "Invalid player slot");
                return true;
            }

            if (action != "Send")
            {
                AnswerSavedCounter(action == "Accept", selectedPlayer);
                return true;
            }

            try
            {
                ArchiveEnsure();
                if (_archive.PendingNativeId != 0 || _archive.Records.Count >= TradeHistoryArchive.MaximumRecords - 5)
                    throw new InvalidOperationException("Trade journal is busy or full");
                DiplomaticTradeNegotiator world;
                ManagedGoodsSnapshotPort port;
                if (!ReadNativeWorld(out world, out port))
                    throw new InvalidOperationException("Native inventory/AI snapshot is unavailable");
                int wanted = scheduled.RequestedGood, offered = scheduled.OfferedGood;
                ulong id = scheduled.Id;
                NegotiatedTradeState result = world.Propose(scheduled.Requester, (byte)selectedPlayer, (ushort)offered, (ushort)wanted, scheduled.RequestedAmount, tier, id, 0, scheduled.AiToAi);
                switch (result.Status)
                {
                    case DiplomaticTradeOutcome.Accepted:
                        RecordActualPreTradeGoodwill(result);
                        string failure = "quote_unavailable";
                        if (result.Quote != null && ExecuteNativeBarter(result.Quote, FromQuote(result.Quote, TradeDisposition.PreparingNative, tier, scheduled.AiToAi, result.ChanceBasisPoints, result.RollBasisPoints), out failure))
                        {
                            _tradeStatus = Text("Handelsanfrage angenommen und verbucht.", "Trade accepted and booked.");
                        }
                        else
                        {
                            _tradeStatus = TradeFailureText(failure);
                        }

                        break;
                    case DiplomaticTradeOutcome.CounterOffer:
                        if (result.CounterOffer == null)
                        {
                            _tradeStatus = Text("Kein gültiges Gegenangebot", "No valid counteroffer");
                            break;
                        }

                        // Persist the original as a terminal superseded
                        // receipt, so a saved counter has a real verifiable
                        // root and cannot resurrect the first quote.
                        if (result.Quote == null || result.CounterOffer.RootId != result.Quote.Id)
                            throw new InvalidOperationException("Counteroffer has no authentic original");
                        _archive.Add(FromQuote(result.Quote, TradeDisposition.Superseded, tier, scheduled.AiToAi, result.ChanceBasisPoints, result.RollBasisPoints));
                        TradeRecord counter = FromQuote(result.CounterOffer, TradeDisposition.PendingCounter, tier, scheduled.AiToAi, result.ChanceBasisPoints, result.RollBasisPoints);
                        _archive.Add(counter);
                        if (!scheduled.AiToAi)
                        {
                            _uiCounterId = counter.Id;
                            _uiCounterpart = selectedPlayer;
                        }

                        _tradeStatus = Text("Gegenangebot: ", "Counteroffer: ") + counter.OfferedAmount + " " + GoodName(counter.OfferedGood, counter.OfferedAmount) + Text(" für ", " for ") + counter.RequestedAmount + " " + GoodName(counter.RequestedGood, counter.RequestedAmount) + Text(". Annehmen oder ablehnen.", " Accept or reject.");
                        break;
                    case DiplomaticTradeOutcome.Rejected:
                        AdoptRejected(world, result);
                        _tradeStatus = Text("Abgelehnt: ", "Rejected: ") + result.Reason + Text(" · Beziehung ", " · relation ") + result.RelationAfter;
                        if (result.Quote != null)
                            _archive.Add(FromQuote(result.Quote, TradeDisposition.Rejected, tier, scheduled.AiToAi, result.ChanceBasisPoints, result.RollBasisPoints));
                        break;
                    default:
                        _tradeStatus = result.Reason;
                        break;
                }

                _archive.Validate(_mapFingerprint, _rosterFingerprint, Math.Max(1, _lastProcessedDay));
                Log("trade_gameplay", "ui_outcome=" + result.Status + "|actor=" + _localPlayer + "|target=" + selectedPlayer + "|id=" + id);
            }
            catch (Exception exception)
            {
                _tradeStatus = Text("Der Handel kann momentan nicht abgeschlossen werden. Versucht es gleich erneut.", "The trade cannot be completed right now. Please try again shortly.");
                Log("trade_gameplay", "status=refused|cause=" + Safe(exception.Message));
            }

            return true;
        }

        private void AnswerSavedCounter(bool accept, int selectedPlayer)
        {
            var scheduled = _executingIntent;
            if (scheduled == null || scheduled.State != TradeIntentStatus.Processing || scheduled.Recipient != selectedPlayer || scheduled.Action == TradeIntentAction.Propose)
                return;
            string denial;
            if (!NativeAuthorized() || !CanTradePair(scheduled.Requester, selectedPlayer, out denial))
                return;
            ulong counterId = scheduled.CounterId;
            if (_archive == null || counterId == 0)
            {
                _tradeStatus = Text("Kein aktives Gegenangebot", "No current counteroffer");
                return;
            }

            TradeRecord rec = _archive.Record(counterId);
            if (!scheduled.MatchesCounter(rec) || rec.State != TradeDisposition.PendingCounter || rec.Requester != scheduled.Requester || rec.Recipient != selectedPlayer || rec.ExpiryDay < Math.Max(1, _lastProcessedDay) || rec.RootId == 0 || _archive.Record(rec.RootId) == null || _archive.Record(rec.RootId).State != TradeDisposition.Superseded)
            {
                _tradeStatus = Text("Gegenangebot abgelaufen oder zurückgezogen", "Counteroffer expired or withdrawn");
                if (_uiCounterId == counterId)
                {
                    _uiCounterId = 0;
                    _uiCounterpart = 0;
                }

                return;
            }

            if (_uiCounterId == counterId)
            {
                _uiCounterId = 0;
                _uiCounterpart = 0;
            }

            if (!accept)
            {
                // A counter-decline affects BOTH the  trade ledger and
                // the existing DIPLOMACY-ExtendedState Trust/Grudge ledger. Preserve
                // their common pre-state so a managed exception cannot leave
                // the saved counter cancelled but the relationship half-written.
                TradeHistoryArchive beforeTrade = _archive.Copy();
                DiplomacyManagedStateMemento beforeDiplomacy = CaptureManagedStateMemento();
                bool stable = false;
                try
                {
                    BeginStableSaveSection("barter_counter_decline", null);
                    stable = true;
                    TradeRelationship relation = _archive.Relation(rec.Requester, rec.Recipient);
                    int before = relation.Goodwill;
                    // validate before mutating either ledger; keep
                    // streak/day budget bounded for safe reload.
                    TradeRelationshipRules.DeclineCounter(relation, Math.Max(1, _lastProcessedDay));
                    rec.State = TradeDisposition.Cancelled;
                    int other = rec.Requester == _localPlayer ? rec.Recipient : rec.Recipient == _localPlayer ? rec.Requester : 0;
                    if (other > 0)
                        ApplyDiplomacyTradeEffect(other, false, before, relation.Goodwill);
                    _tradeStatus = Text("Gegenangebot abgelehnt. Beziehung ", "Counteroffer declined. Relation ") + relation.Goodwill;
                }
                catch (Exception e)
                {
                    _archive = beforeTrade;
                    try
                    {
                        RestoreManagedStateMemento(beforeDiplomacy);
                        if (!scheduled.AiToAi)
                        {
                            _uiCounterId = rec.Id;
                            _uiCounterpart = selectedPlayer;
                        }
                    }
                    catch (Exception restoreError)
                    {
                        _tradeFaulted = true;
                        Log("trade_relationship", "status=counter_decline_quarantine|reason=" + Safe(restoreError.GetType().Name));
                    }

                    _tradeStatus = Text("Die Ablehnung konnte nicht gespeichert werden. Prüft das Gegenangebot erneut.", "The refusal could not be recorded. Please review the counteroffer again.");
                    Log("trade_session", "status=counter_decline_failed|reason=" + Safe(e.GetType().Name));
                }
                finally
                {
                    if (stable)
                        EndStableSaveSection();
                }

                return;
            }

            try
            {
                DiplomaticTradeNegotiator world;
                ManagedGoodsSnapshotPort port;
                if (!ReadNativeWorld(out world, out port))
                    throw new InvalidOperationException("native inventory no longer ready");
                BarterQuote q = QuoteFromSaved(rec);
                if (!world.IsMutuallyBeneficial(q))
                    throw new InvalidOperationException("counter terms no longer fair");
                // This is the requester explicitly authorizing exact persisted
                // terms. The counter has not booked any goods before this click.
                rec.State = TradeDisposition.Cancelled;
                // The durable one-shot counter ID is not reissued after failure.
                var commit = rec.Copy();
                commit.State = TradeDisposition.PreparingNative;
                _archive.Records.Remove(rec);
                string failure;
                if (ExecuteNativeBarter(q, commit, out failure))
                {
                    _tradeStatus = Text("Gegenangebot angenommen.", "Counteroffer accepted.");
                }
                else
                {
                    // The native coordinator may have already consumed the
                    // quote ID or left an unresolved journal in quarantine.
                    // Never reinsert a duplicate or restore its old authority.
                    if (_archive.Record(q.Id) == null)
                        _archive.Records.Add(rec);
                    _tradeStatus = TradeFailureText(failure);
                }
            }
            catch (Exception e)
            {
                _tradeStatus = Text("Das Gegenangebot ist nicht mehr gültig. Sendet bei Bedarf eine neue Anfrage.", "The counteroffer is no longer valid. Send a new request if needed.");
                Log("trade_session", "status=counter_no_longer_valid|reason=" + Safe(e.Message));
                if (rec.State == TradeDisposition.PendingCounter)
                    rec.State = TradeDisposition.Cancelled;
            }
        }

        private void PumpAutonomousNativeTrading()
        {
            if (CanonicalOwnsTradeLane)
                return;
            if (!NativeAuthorized())
                return;
            try
            {
                ArchiveEnsure();
                EnsureMailbox();
                int now = Math.Max(1, _lastProcessedDay);
                if (_archive.LastAutonomousDay >= now)
                    return;
                if (_archive.Records.Count >= TradeHistoryArchive.MaximumRecords - 25)
                    return;
                byte[] humanSlots;
                string roleReason;
                if (!TryReadHumanSlots(out humanSlots, out roleReason))
                {
                    Log("trade_human_policy", "status=autonomous_proposals_held|reason=" + Safe(roleReason));
                    return;
                }

                DiplomaticTradeNegotiator world;
                ManagedGoodsSnapshotPort port;
                if (!ReadNativeWorld(out world, out port))
                    return;
                var plans = world.PlanAutonomousAiTrade(humanSlots);
                _archive.LastAutonomousDay = now;
                foreach (var plan in plans)
                {
                    string reason;
                    if (_mailbox.HasPending(plan.Requester, plan.Recipient) || !CanTradePair(plan.Requester, plan.Recipient, out reason))
                        continue;
                    if (_archive.Records.Any(x => x.Requester == plan.Requester && x.Recipient == plan.Recipient && x.State == TradeDisposition.PendingCounter && x.ExpiryDay >= now))
                        continue;
                    EnqueueTrade(plan.Requester, plan.Recipient, plan.OfferedGood, plan.RequestedGood, plan.RequestedAmount, plan.Generosity, true, TradeIntentAction.Propose, 0);
                }

                Log("trade_scheduler", "status=autonomous_requests_scheduled|day=" + now + "|plans=" + plans.Length);
            }
            catch (Exception exception)
            {
                // This planner only reads a detached world and queues mailbox
                // intents. It cannot have touched native goods. A bad price,
                // unavailable snapshot or full scheduler must not lock all
                // later player trades as though a partial native write occurred.
                // The native transaction coordinator separately quarantines
                // any real attempted inventory mutation.
                try
                {
                    Log("trade_rebellion", "status=autonomous_planning_deferred_no_native_write|reason=" + Safe(exception.GetType().Name));
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
