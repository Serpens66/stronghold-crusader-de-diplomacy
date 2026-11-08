//  integration in the existing game-frame lane. Native release gates remain mandatory.
using System;
using System.Linq;
using SHCDESE.API;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private TradeIntentMailbox _mailbox;
        private TradeIntent _executingIntent;
        private sealed class LiveTradePairPolicy : IObservedTradePairProvider
        {
            private readonly DiplomacyFeature _owner;
            internal LiveTradePairPolicy(DiplomacyFeature owner)
            {
                _owner = owner;
            }

            public bool TryGetPair(byte a, byte b, int day, out ObservedTradePairState pair)
            {
                return _owner.ReadTradePair(a, b, day, out pair);
            }
        }

        private static int TradePolicyPercent(decimal x)
        {
            return (int)Math.Max(0m, Math.Min(100m, x));
        }

        private static int DiplomaticGoodwill(DiplomacyRelationState s)
        {
            // Legacy RelationState.Goodwill is gift-value credit, not a percent.
            // Use bounded current ExtendedState relationship signals on their real scale.
            return s == null ? 0 : TradePolicyPercent(s.Trust * 0.65m + s.Respect * 0.35m - s.Grudge * 0.30m);
        }

        private bool ReadTradePair(byte a, byte b, int day, out ObservedTradePairState pair)
        {
            pair = null;
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || a < 1 || a > 8 || b < 1 || b > 8 || a == b || !_roster.Contains(a) || !_roster.Contains(b) || day != Math.Max(1, _lastProcessedDay))
                return false;
            var p = new ObservedTradePairState
            {
                Requester = a,
                Recipient = b,
                Day = day,
                RequesterAlive = IsAlivePlayer(a),
                RecipientAlive = IsAlivePlayer(b)
            };
            var api = GamePlayerManagerAPI.Instance;
            if (api == null)
                return false;
            RelationState relation = null;
            string id;
            bool touchesLocal = a == _localPlayer || b == _localPlayer;
            if (touchesLocal)
            {
                int other = a == _localPlayer ? b : a;
                if (!_playerCoalition.TryGetValue(other, out id) || !_relations.TryGetValue(id, out relation) || relation.ExtendedState == null)
                    return false;
                var s = relation.ExtendedState;
                bool alliance = s.Agreement == DiplomacyAgreementKind.Alliance && relation.Allied && api.IsPlayerAlliedTo(a, b) && api.IsPlayerAlliedTo(b, a);
                bool pact = s.Agreement == DiplomacyAgreementKind.TradePact && s.AgreementUntilDay > day;
                p.Hostile = s.Stage == DiplomacyNegotiationStage.Hostile || s.Stage == DiplomacyNegotiationStage.Blocked || s.Agreement == DiplomacyAgreementKind.None;
                p.HasTradeRelation = alliance || pact;
                p.Goodwill = DiplomaticGoodwill(s);
                p.Trust = TradePolicyPercent(s.Trust);
                p.Grudge = TradePolicyPercent(s.Grudge);
                p.Threat = TradePolicyPercent(s.StrategicThreat);
            }
            else
            {
                // AI pairs have no fabricated human-facing pact. A reciprocal
                // actual alliance is an established relation above trade tier.
                bool allied = api.IsPlayerAlliedTo(a, b) && api.IsPlayerAlliedTo(b, a);
                if (!allied)
                    return false; // no authoritative nonlocal pact/hostility evidence
                p.HasTradeRelation = true;
                p.Hostile = false;
                TradeRelationship history = _archive == null ? null : _archive.Relationships.FirstOrDefault(x => x.A == Math.Min(a, b) && x.B == Math.Max(a, b));
                p.Goodwill = history == null ? 50 : history.Goodwill;
                p.Trust = p.Goodwill;
                p.Grudge = history == null ? 0 : Math.Min(100, history.RejectionStreak * 4);
                // No pair-specific threat evidence exists for independent AI
                // coalitions. This neutral prior is not a measured war forecast.
                p.Threat = 0;
            }

            foreach (var r in _relations.Values)
            {
                if (r == null || r.ExtendedState == null || !r.Allied || !DiplomacyVassalage.IsActive(r.ExtendedState))
                    continue;
                int v = DiplomacyVassalage.VassalOf(r.ExtendedState), o = DiplomacyVassalage.OverlordOf(r.ExtendedState);
                if (v == a && o == b || v == b && o == a)
                {
                    p.Vassal = (byte)v;
                    p.Overlord = (byte)o;
                    break;
                }
            }

            p.Authoritative = true;
            pair = p;
            return true;
        }

        private bool CanTradePair(int a, int b, out string reason)
        {
            reason = Text("Handel erst ab Handelsabkommen möglich.", "A trade pact is required.");
            if (a < 1 || a > 8 || b < 1 || b > 8)
                return false;
            ObservedTradePairState pair;
            try
            {
                return ReadTradePair((byte)a, (byte)b, Math.Max(1, _lastProcessedDay), out pair) && TradePairEligibilityRules.IsEligible(pair, (byte)a, (byte)b, Math.Max(1, _lastProcessedDay), out reason);
            }
            catch (Exception e)
            {
                Log("trade_scheduler", "status=relation_unavailable|cause=" + Safe(e.GetType().Name));
                return false;
            }
        }

        private bool CanTradeWith(int selected, out string reason)
        {
            return CanTradePair(_localPlayer, selected, out reason);
        }

        private void EnsureMailbox()
        {
            if (_mailbox != null)
                return;
            _mailbox = new TradeIntentMailbox
            {
                MapFingerprint = _mapFingerprint,
                RosterFingerprint = _rosterFingerprint
            };
            _mailbox.Rebase(ReadSimulationTick());
        }

        private void ResetTradeRuntime()
        {
            ClearSteamInbox();
            ResetCanonicalTrade();
            _mailbox = null;
            _executingIntent = null;
        }

        private void RestoreTradeRuntime(TradeIntentMailbox saved)
        {
            _executingIntent = null;
            if (saved == null)
            {
                _mailbox = null;
                return;
            }

            saved.Validate(_mapFingerprint, _rosterFingerprint, Math.Max(1, _lastProcessedDay), _archive);
            _mailbox = saved.Copy();
            _mailbox.RestoreAfterLoad(ReadSimulationTick());
        }

        private bool HasPendingTrade(int selected)
        {
            return _mailbox != null && _mailbox.HasPending(_localPlayer, selected);
        }

        private string PendingTradeStatus(int selected)
        {
            if (HasPendingTrade(selected))
                return Text("Ein Bote ist unterwegs. Die Antwort steht noch aus.", "A messenger is on the way. Awaiting a reply.");
            if (_mailbox == null)
                return string.Empty;
            var last = _mailbox.Intents.Where(x => !x.AiToAi && x.Requester == _localPlayer && x.Recipient == selected).OrderByDescending(x => x.Id).FirstOrDefault();
            return last == null ? string.Empty : last.Outcome;
        }

        private ulong ReserveIntentId()
        {
            ArchiveEnsure();
            ulong id = _archive.NextId;
            if (id >= ulong.MaxValue - 8)
                throw new InvalidOperationException("Trade identifiers exhausted");
            // Preflight alone must not consume a persistent identity.
            // The scheduler is authoritative for admission and must accept
            // the request BEFORE the archival high-water is committed.
            return id;
        }

        private void EnqueueTrade(byte requester, byte recipient, ushort offered, ushort wanted, int amount, TradeOfferGenerosity tier, bool ai, TradeIntentAction action, ulong counterId)
        {
            if (CanonicalOwnsTradeLane)
            {
                QueueCanonicalLegacy(requester, recipient, offered, wanted, amount, tier, ai, action, counterId);
                return;
            }

            IDisposable scheduleLease;
            if (!TryEnterNativeInventorySection(out scheduleLease))
                throw new InvalidOperationException("Original engine is busy; request not admitted");
            using (scheduleLease)
            {
                EnsureMailbox();
                // Account for time before this click now. Otherwise the next frame
                // would incorrectly count it as part of this request's waiting time.
                _mailbox.Observe(ReadSimulationTick());
                if (_mailbox.HasPending(requester, recipient))
                    throw new InvalidOperationException("A reply for this pair is pending");
                var counter = action == TradeIntentAction.Propose ? null : _archive == null ? null : _archive.Record(counterId);
                if (action != TradeIntentAction.Propose && (counter == null || counter.State != TradeDisposition.PendingCounter))
                    throw new InvalidOperationException("No pending counteroffer for these terms");
                ulong id = ReserveIntentId();
                int issuedDay = Math.Max(1, _lastProcessedDay);
                ObservedTradePairState currentPair;
                string pairReason;
                if (!ReadTradePair(requester, recipient, issuedDay, out currentPair) || !TradePairEligibilityRules.CanTrade(currentPair, requester, recipient, issuedDay, out pairReason))
                    throw new InvalidOperationException("No authoritative trade relationship at scheduling");
                // A human peer cannot be silently accepted/refused by the AI.
                // Explicit human-to-human consent awaits the authenticated
                // multiplayer manual-decision path, which is not yet attested.
                string recipientRole;
                if (!MayAutoAnswerTrade(recipient, out recipientRole))
                    throw new InvalidOperationException("AI response not authorised: " + recipientRole);
                Ruler recipientProfile = ReadLordProfile(recipient);
                if (recipientProfile == null || recipientProfile.PlayerId != recipient)
                    throw new InvalidOperationException("No recipient personality observation");
                int pending = _mailbox.Intents.Count(z => z.State == TradeIntentStatus.Waiting || z.State == TradeIntentStatus.Processing);
                int replyTicks = NegotiationTiming.Delay(new ReplyContext { Id = id, Day = issuedDay, Requester = requester, Recipient = recipient, Action = (int)action, RequestedAmount = amount, Trust = currentPair.Trust, Grudge = currentPair.Grudge, Threat = currentPair.Threat, Urgency = Math.Max(currentPair.Threat, recipientProfile.AtWar ? 75 : 0), Caution = recipientProfile.Caution, Commerce = recipientProfile.Commerce, RecipientAtWar = recipientProfile.AtWar, PendingNegotiations = pending });
                var x = new TradeIntent
                {
                    Id = id,
                    CounterId = counterId,
                    Requester = requester,
                    Recipient = recipient,
                    OfferedGood = offered,
                    RequestedGood = wanted,
                    RequestedAmount = amount,
                    Generosity = (byte)tier,
                    IssuedDay = issuedDay,
                    EnqueuedClock = _mailbox.Clock,
                    DueClock = checked(_mailbox.Clock + replyTicks),
                    ExpiryClock = checked(_mailbox.Clock + TradeIntentMailbox.MaximumLifetimeTicks),
                    CounterRootId = counter == null ? 0 : counter.RootId,
                    CounterOfferedAmount = counter == null ? 0 : counter.OfferedAmount,
                    CounterCreatedDay = counter == null ? 0 : counter.IssuedDay,
                    CounterExpiresDay = counter == null ? 0 : counter.ExpiryDay,
                    Action = action,
                    AiToAi = ai,
                    State = TradeIntentStatus.Waiting
                };
                if (counter != null && !x.MatchesCounter(counter))
                    throw new InvalidOperationException("Counteroffer terms changed");
                x.Validate(_mailbox.Clock, issuedDay, id);
                // No archival ID advance on rejection, invalid terms, full
                // mailbox, duplicate pair or invalid simulation clock.
                _mailbox.Enqueue(x, issuedDay);
                _archive.NextId = checked(id + 4);
                // Never reinterpret an admitted durable intent as a rejected
                // click if telemetry fails after the mailbox commit.
                try
                {
                    Log("trade_scheduler", "status=queued|id=" + id + "|actor=" + requester + "|target=" + recipient + "|due=" + x.DueClock);
                }
                catch (Exception)
                {
                }
            }
        }

        private void QueueSelectedTrade(string action, int selected)
        {
            if (action != "Send" && action != "Accept" && action != "Decline")
                return;
            string denial;
            if (!CanTradeWith(selected, out denial))
            {
                _tradeStatus = denial;
                return;
            }

            if (!NativeAuthorized())
            {
                _tradeStatus = Text("Handel ist für diese Partie noch nicht verfügbar.", "Trading is not yet available in this match.");
                return;
            }

            if (HasPendingTrade(selected))
            {
                _tradeStatus = PendingTradeStatus(selected);
                return;
            }

            try
            {
                if (action == "Send")
                {
                    if (_archive != null && _archive.Records.Any(x => x.Requester == _localPlayer && x.Recipient == selected && x.State == TradeDisposition.PendingCounter && x.ExpiryDay >= Math.Max(1, _lastProcessedDay)))
                        throw new InvalidOperationException("Answer the pending counteroffer first");
                    ushort wanted = _tradeWantedIndex < 0 || _tradeWantedIndex >= TradeAllowedGoods.Length ? (ushort)0 : (ushort)TradeAllowedGoods[_tradeWantedIndex];
                    ushort offered = _tradeOfferedIndex < 0 || _tradeOfferedIndex >= TradeAllowedGoods.Length ? (ushort)0 : (ushort)TradeAllowedGoods[_tradeOfferedIndex];
                    EnqueueTrade((byte)_localPlayer, (byte)selected, offered, wanted, _tradeRequestedAmount, _tradeGenerosity, false, TradeIntentAction.Propose, 0);
                }
                else
                {
                    TradeRecord c = _archive == null ? null : _archive.Record(_uiCounterId);
                    if (c == null || c.State != TradeDisposition.PendingCounter || c.Requester != _localPlayer || c.Recipient != selected || _uiCounterpart != selected || c.ExpiryDay < Math.Max(1, _lastProcessedDay))
                        throw new InvalidOperationException("No current counteroffer for this ruler");
                    EnqueueTrade(c.Requester, c.Recipient, c.OfferedGood, c.RequestedGood, c.RequestedAmount, (TradeOfferGenerosity)c.Generosity, false, action == "Accept" ? TradeIntentAction.AcceptCounter : TradeIntentAction.DeclineCounter, c.Id);
                }

                _statusPartner = selected;
                _tradeStatus = PendingTradeStatus(selected);
            }
            catch (Exception e)
            {
                Log("trade_session", "status=request_not_admitted|reason=" + Safe(e.Message));
                _tradeStatus = Text("Die Anfrage konnte nicht gesendet werden. Prüft das Angebot und versucht es erneut.", "The request could not be sent. Check the offer and try again.");
            }
        }

        private void PumpTradeMailbox()
        {
            if (CanonicalOwnsTradeLane)
            {
                PumpCanonicalSolo();
                return;
            }

            if (_mailbox == null || !_mapActive || _mapDisabled)
                return;
            _mailbox.Observe(ReadSimulationTick());
            foreach (var request in _mailbox.Due())
            {
                // The same original monitor spans eligibility, AI snapshot,
                // quote settlement and both native legs. A preflight followed
                // by a second lease attempt could lose an admitted request.
                IDisposable mailboxLease;
                if (!TryEnterNativeInventorySection(out mailboxLease))
                {
                    request.Outcome = Text("Die Antwort steht noch aus.", "Awaiting a reply.");
                    continue;
                }

                using (mailboxLease)
                {
                    string savedUi = _tradeStatus;
                    int savedPartner = _statusPartner;
                    bool display = !request.AiToAi && request.Requester == _localPlayer && _draftPartner == request.Recipient;
                    string denied;
                    if (_mailbox.Clock > request.ExpiryClock || !CanTradePair(request.Requester, request.Recipient, out denied))
                    {
                        request.State = TradeIntentStatus.Cancelled;
                        request.Outcome = Text("Anfrage verfallen oder Handelsbedingungen nicht mehr erfüllt.", "Request expired or trading conditions changed.");
                        if (display)
                        {
                            _tradeStatus = request.Outcome;
                            _statusPartner = request.Recipient;
                        }

                        continue;
                    }

                    string currentRole;
                    if (!MayAutoAnswerTrade(request.Recipient, out currentRole))
                    {
                        if (currentRole == "human_recipient_requires_explicit_consent")
                        {
                            request.State = TradeIntentStatus.Cancelled;
                            request.Outcome = Text("Der menschliche Empfänger muss selbst entscheiden.", "The human recipient must decide.");
                            if (display)
                            {
                                _tradeStatus = request.Outcome;
                                _statusPartner = request.Recipient;
                            }
                        }
                        else
                            request.Outcome = Text("Spielerrollen werden geprüft. Antwort steht aus.", "Checking player roles. Reply pending.");
                        continue;
                    }

                    // A simulation/save boundary can be briefly busy. Preserve the
                    // admitted request and its original saved expiry while waiting;
                    // a temporary native lease failure is not a diplomatic refusal.
                    if (!NativeAuthorized())
                    {
                        request.Outcome = Text("Die Antwort steht noch aus.", "Awaiting a reply.");
                        continue;
                    }

                    request.State = TradeIntentStatus.Processing;
                    _executingIntent = request;
                    // A previous ruler's UI string is NOT a terminal result of
                    // this due request. Clear it before the isolated settlement.
                    _tradeStatus = string.Empty;
                    try
                    {
                        if (request.Action == TradeIntentAction.Propose)
                            ProcessSelectedTrade("Send", request.Recipient);
                        else if (request.Action == TradeIntentAction.EvaluateAiCounter)
                        {
                            var rec = _archive == null ? null : _archive.Record(request.CounterId);
                            if (!request.MatchesCounter(rec) || rec.State != TradeDisposition.PendingCounter)
                            {
                                request.State = TradeIntentStatus.Cancelled;
                                request.Outcome = "ai_counter_stale_no_diplomatic_penalty";
                                continue;
                            }

                            DiplomaticTradeNegotiator world;
                            ManagedGoodsSnapshotPort port;
                            if (!ReadNativeWorld(out world, out port))
                            {
                                // An engine read failure is NOT an AI refusal.
                                // Retry only while still inside the same bounded
                                // simulation-time envelope and saved counter term.
                                long next = _mailbox.Clock + 60L;
                                if (next <= request.ExpiryClock && next - request.EnqueuedClock <= TradeIntentMailbox.MaximumDelayTicks && rec.ExpiryDay >= Math.Max(1, _lastProcessedDay))
                                {
                                    request.DueClock = next;
                                    request.State = TradeIntentStatus.Waiting;
                                    request.Outcome = "ai_counter_snapshot_waiting_without_penalty";
                                }
                                else
                                {
                                    request.State = TradeIntentStatus.Cancelled;
                                    request.Outcome = "ai_counter_snapshot_unavailable_without_penalty";
                                }

                                continue;
                            }

                            bool accept = world.CanAcceptAiCounter(QuoteFromSaved(rec));
                            AnswerSavedCounter(accept, request.Recipient);
                        }
                        else
                            AnswerSavedCounter(request.Action == TradeIntentAction.AcceptCounter, request.Recipient);
                        if (string.IsNullOrEmpty(_tradeStatus))
                            throw new InvalidOperationException("Trade response lacked a settled outcome");
                        request.Outcome = _tradeStatus;
                        if (request.Outcome.Length > 240)
                            request.Outcome = request.Outcome.Substring(0, 240);
                        request.State = TradeIntentStatus.Finished;
                    }
                    catch (Exception e)
                    {
                        bool uncertain = _tradeFaulted || _pendingPackage != null && _pendingPackage.TradeRecordId != 0;
                        TradeRecord savedRecord = _archive == null ? null : _archive.Record(request.Action == TradeIntentAction.Propose ? request.Id : request.CounterId);
                        if (savedRecord != null && (savedRecord.State == TradeDisposition.PreparingNative || savedRecord.State == TradeDisposition.Committed))
                            uncertain = true;
                        request.State = TradeIntentStatus.Cancelled;
                        request.Outcome = uncertain ? Text("Der Handel wurde zur Prüfung angehalten. Es wird kein weiterer Tausch ausgeführt.", "Trading has been paused for verification. No further exchange will be carried out.") : Text("Die Anfrage konnte nicht ausgeführt werden. Bitte versucht es erneut.", "The request could not be completed. Please try again.");
                        if (uncertain)
                        {
                            _tradeFaulted = true;
                            _goodsTransfersUnavailable = true;
                        }

                        try
                        {
                            Log("trade_rebellion", "status=request_failed|id=" + request.Id + "|native_uncertain=" + uncertain + "|reason=" + Safe(e.GetType().Name));
                        }
                        catch (Exception)
                        {
                        }
                    }
                    finally
                    {
                        _executingIntent = null;
                        if (display)
                        {
                            _tradeStatus = request.Outcome;
                            _statusPartner = request.Recipient;
                        }
                        else
                        {
                            _tradeStatus = savedUi;
                            _statusPartner = savedPartner;
                        }
                    }

                    // The AI requester considers an actual saved counter on a
                    // second delayed turn using another fresh inventory snapshot.
                    if (request.State == TradeIntentStatus.Finished && request.AiToAi && request.Action == TradeIntentAction.Propose)
                    {
                        try
                        {
                            var c = _archive == null ? null : _archive.Records.FirstOrDefault(x => x.RootId == request.Id && x.State == TradeDisposition.PendingCounter);
                            if (c != null)
                                EnqueueTrade(c.Requester, c.Recipient, c.OfferedGood, c.RequestedGood, c.RequestedAmount, (TradeOfferGenerosity)c.Generosity, true, TradeIntentAction.EvaluateAiCounter, c.Id);
                        }
                        catch (Exception followupFailure)
                        {
                            // A failed follow-up cannot cancel other due requests or
                            // reinterpret an already terminal request as unprocessed.
                            Log("trade_save", "status=ai_counter_queue_refused|id=" + request.Id + "|reason=" + Safe(followupFailure.GetType().Name));
                        }
                    }
                } // original native monitor for this complete due request
            }
        }

        private RebellionAssessmentDecision ObserveVassalRisk(RelationState relation, int day, long vassalPower, long overlordPower)
        {
            var state = relation.ExtendedState;
            EnsureMailbox();
            byte v = (byte)DiplomacyVassalage.VassalOf(state), o = (byte)DiplomacyVassalage.OverlordOf(state);
            var memory = _mailbox.Rebellions.FirstOrDefault(x => x.Vassal == v && x.Overlord == o);
            if (memory == null)
            {
                if (_mailbox.Rebellions.Count >= TradeIntentMailbox.MaximumRebellionHistories)
                    throw new InvalidOperationException("Rebellion ledger full");
                memory = new TradeRebellionMemory
                {
                    Vassal = v,
                    Overlord = o,
                    SinceDay = state.VassalSinceDay,
                    LastDay = day - 1
                };
                _mailbox.Rebellions.Add(memory);
            }

            if (memory.SinceDay != state.VassalSinceDay)
            {
                memory.SinceDay = state.VassalSinceDay;
                memory.LastDay = day - 1;
                memory.StrengthDays = memory.PoorDays = 0;
                memory.AtRisk = false;
            }

            // Backwards day evidence cannot inherit prior hysteresis.
            if (day < memory.LastDay)
                memory.AtRisk = false;
            bool priorRisk = memory.AtRisk;
            bool strong = vassalPower > 0 && overlordPower > 0 && (decimal)vassalPower / overlordPower >= (priorRisk ? TradePairEligibilityRules.RebellionHoldStrengthRatio : TradePairEligibilityRules.RebellionEnterStrengthRatio);
            int goodwill = DiplomaticGoodwill(state);
            bool poor = goodwill <= (priorRisk ? 40 : 35) && (state.Trust <= 45m || state.Grudge >= 40m);
            RebellionObservation.Observe(memory, day, strong, poor);
            Ruler ruler = ReadLordProfile(v);
            var decision = TradePairEligibilityRules.EvaluateRebellion(new RebellionAssessmentInput { Authoritative = IsAlivePlayer(v) && IsAlivePlayer(o), ActiveVassal = DiplomacyVassalage.IsActive(state), PreviouslyAtRisk = priorRisk, Day = day, VassalSinceDay = state.VassalSinceDay, LastRebellionDay = memory.LastRebellionDay, StrengthEdgeDays = memory.StrengthDays, PoorRelationDays = memory.PoorDays, Goodwill = goodwill, Trust = TradePolicyPercent(state.Trust), Grudge = TradePolicyPercent(state.Grudge), Ambition = TradePolicyPercent(DiplomacyAgreementRules.BreachSensitivityOf(state) / 3m), Caution = ruler.Caution, VassalPower = vassalPower, OverlordPower = overlordPower, SupplyReadiness = null, ExternalThreat = TradePolicyPercent(state.StrategicThreat) });
            memory.AtRisk = decision.Valid && decision.UnderPressure;
            return decision;
        }

        private bool RebellionPermitted(RelationState relation, int day)
        {
            if (relation == null || relation.ExtendedState == null || !DiplomacyVassalage.IsActive(relation.ExtendedState))
                return false;
            var s = relation.ExtendedState;
            var result = ObserveVassalRisk(relation, day, MilitaryOf(DiplomacyVassalage.VassalOf(s), day), MilitaryOf(DiplomacyVassalage.OverlordOf(s), day));
            return result.Valid && result.ShouldRebel;
        }

        private void RecordCommittedRebellion(DiplomacyPendingTeamTransition transition, DiplomacyManagedStateMemento before)
        {
            if (transition == null || before == null || _mailbox == null || transition.Kind != DiplomacyTeamTransitionKind.AiBreak || transition.Reason != VassalRebellionReason)
                return;
            var previous = before.Relations.FirstOrDefault(x => x.CoalitionId == transition.CoalitionId);
            if (previous == null || previous.ExtendedState == null || !DiplomacyVassalage.IsActive(previous.ExtendedState))
                return;
            int v = DiplomacyVassalage.VassalOf(previous.ExtendedState), o = DiplomacyVassalage.OverlordOf(previous.ExtendedState);
            var memory = _mailbox.Rebellions.FirstOrDefault(x => x.Vassal == v && x.Overlord == o);
            if (memory == null)
                return;
            // No native action or UI work follows this ledger update in the
            // transaction body; unsuccessful transitions never reach it.
            memory.LastRebellionDay = Math.Max(1, _lastProcessedDay);
            memory.AtRisk = false;
            memory.StrengthDays = memory.PoorDays = 0;
        }
    }
}
