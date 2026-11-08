// Selected global controller, connected to the existing native transaction,
// original engine monitor and single SaveData owner. No new hook or native setter.
// The local offline lane is private/opt-in. Real multiplayer execution still
// requires independently verified host authority and ordered actor commands.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private CanonicalTradeReducer _canonical;
        private byte[] _canonicalSaved;
        private PreparedCanonicalTransition _canonicalTransition;
        private LegacyDiplomacySave _canonicalRecoverySeed;
        private long _canonicalTick = -1;
        private bool _canonicalStopped;
#if DIPLOMACY_PRIVATE_TESTS
        private string _lastCanonicalWaitingReason;
        private long _lastCanonicalWaitingFrame = -1;
        private static bool ShouldReportCanonicalWait(
            string current, string previous, long frame, long last)
        {
            return last < 0 || frame < last ||
                !String.Equals(current, previous, StringComparison.Ordinal) ||
                frame - last >= 600;
        }
#endif
        private readonly List<LocalTradeCommand> _canonicalInputs = new List<LocalTradeCommand>();
        private sealed class LocalTradeCommand
        {
            internal byte A, B, Tier;
            internal ushort Give, Want;
            internal int Amount;
            internal TradeIntentAction Action;
            internal TradeRecord Counter;
            internal CanonicalHumanOffer Consent;
            internal CanonicalTradeAction ConsentAction;
            internal int LiteralOfferedAmount;
        }

        private bool CanonicalOwnsTradeLane
        {
            get
            {
                if (_canonicalSaved != null || _canonical != null)
                    return true;
#if DIPLOMACY_PRIVATE_TESTS
                return Environment.GetEnvironmentVariable("DIPLOMACY_CANONICAL_TRADE") == "1";
#else
                return false;
#endif
            }
        }

        private void ResetCanonicalTrade()
        {
            _canonical = null;
            _canonicalSaved = null;
            _canonicalTransition = null;
            _canonicalRecoverySeed = null;
            _canonicalTick = -1;
            _canonicalStopped = false;
#if DIPLOMACY_PRIVATE_TESTS
            _lastCanonicalWaitingReason = null;
            _lastCanonicalWaitingFrame = -1;
#endif
            _canonicalInputs.Clear();
            _consentCounterDrafts.Clear();
        }

        private void RestoreCanonicalState(byte[] state)
        {
            ResetCanonicalTrade();
            _canonicalSaved = state == null ? null : (byte[])state.Clone();
        }

        private byte[] CanonicalSaveBytes()
        {
            if (_mpConsentMode)
                return _canonicalSaved == null ? null : (byte[])_canonicalSaved.Clone();
            // SnapshotSave already holds the feature gate; during native settlement it
            // also holds the same original engine monitor as the save callback.
            if (_canonical != null && _canonicalTransition == null)
            {
                _canonical.ObserveOriginalRebellionHistory(_mailbox);
                _canonicalSaved = _canonical.SaveBytes();
            }

            return _canonicalSaved == null ? null : (byte[])_canonicalSaved.Clone();
        }

        private void PrepareCanonicalSaveDay()
        {
            if (_mpConsentMode)
                return;
            if (_canonicalTransition != null || _canonicalSaved == null)
                return;
            int day = Math.Max(1, _lastProcessedDay);
            if (_canonical != null)
            {
                _canonical.ObserveOriginalRebellionHistory(_mailbox);
                _canonical.HoldAtOriginalDay(day);
                ProjectCanonical(_canonical.SaveBytes());
            }
            else
            {
                // Loading a canonical private save in a closed normal build preserves
                // its one owner. The rest of diplomacy may advance without losing saves.
                var model = CanonicalTradeCodec.Decode(_canonicalSaved);
                if (_mailbox != null)
                {
                    model.Mailbox.Rebellions.Clear();
                    model.Mailbox.Rebellions.AddRange(_mailbox.Rebellions.Select(x => x.Copy()));
                }

                ProjectCanonical(CanonicalTradeReducer.HoldSavedStateAtOriginalDay(CanonicalTradeCodec.Encode(model), day));
            }
        }

        private void ProjectCanonical(byte[] bytes)
        {
            CanonicalModel model = CanonicalTradeCodec.Decode(bytes);
            _canonicalSaved = (byte[])bytes.Clone();
            _archive = model.Archive.Copy();
            _mailbox = model.Mailbox.Copy();
            // This cursor is process-local, so rebase the detached presentation copy.
            _mailbox.Rebase(ReadSimulationTick());
            var counter = _archive.Records.Where(r => r.Requester == _localPlayer && r.State == TradeDisposition.PendingCounter && r.ExpiryDay >= Math.Max(1, _lastProcessedDay)).OrderByDescending(r => r.Id).FirstOrDefault();
            _uiCounterId = counter == null ? 0 : counter.Id;
            _uiCounterpart = counter == null ? 0 : counter.Recipient;
        }

        // Signal hash means only: these exact original managed AIC numeric
        // values were observed for this native lord ID. It is deliberately
        // NOT the custom-lord source-file fingerprint used by future patches.
        private static string FingerprintOriginalAicSignals(int lordId, InternalAIC aic)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer))
            {
                writer.Write(1); // fixed signal-schema version
                writer.Write(lordId);
                foreach (int signal in new int[]
                {
                    aic.send_goods_margin, aic.base_gold_reserves,
                    aic.buy_apples_at, aic.buy_cheese_at, aic.buy_bread_at,
                    aic.buy_wheat_at, aic.trade_wood_at, aic.trade_stone_at,
                    aic.trade_resources_at, aic.trade_weapons_at, aic.buy_weapons,
                    aic.chance_of_defensive1, aic.chance_of_seiging1,
                    aic.chance_of_harrasment1
                })
                    writer.Write(signal);
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(buffer.ToArray()))
                        .Replace("-", "").ToLowerInvariant();
            }
        }

        private static int ObservedOriginalAicDemandHint(InternalAIC aic, ushort good)
        {
            // These are live original AIC CONFIGURATION values, not proof of
            // an executed buy. Undefined commodities have no invented signal.
            switch (good)
            {
                case 2: return Math.Max(0,aic.trade_wood_at);
                case 4: return Math.Max(0,aic.trade_stone_at);
                case 6: return Math.Max(0,aic.trade_resources_at);
                case 7: return Math.Max(0,aic.trade_pitch_at);
                case 9: return Math.Max(0,aic.buy_wheat_at);
                case 10: return Math.Max(0,aic.buy_bread_at);
                case 11: return Math.Max(0,aic.buy_cheese_at);
                case 13: return Math.Max(0,aic.buy_apples_at);
                case 14: return Math.Max(0,aic.trade_ale_at);
                case 16: return Math.Max(0,aic.trade_flour_at);
                default:
                    return good >= 17 && good <= 24
                        ? Math.Max(0,Math.Max(aic.buy_weapons,aic.trade_weapons_at))
                        : 0;
            }
        }

        private CanonicalTradeSnapshot CaptureCanonicalWorld(long tick)
        {
            RequireNativeDiplomacySection();
            string reason;
            byte[] humanSlots;
            if (!IsSoloEngineSession(out reason) || !TryReadHumanSlots(out humanSlots, out reason) || humanSlots.Length != 1 || humanSlots[0] != _localPlayer)
                throw new InvalidOperationException("Original solo roster is not proven: " + reason);
            var api = GamePlayerManagerAPI.Instance;
            var data = GameData.Instance;
            var live = data == null ? null : data.lastGameState;
            if (api == null || live == null)
                throw new InvalidOperationException("No original snapshot");
            var players = new List<CanonicalPlayer>();
            var originalAiEconomy = new List<CanonicalOriginalAiEconomy>();
            var rulers = new List<Ruler>();
            var prices = new List<Good>();
            var stocks = new ManagedGoodsSnapshotPort();
            bool[] originalTradepostBySlot;
            var activeProducers = ObserveRealActiveGoodsProducers(out originalTradepostBySlot);
            if (originalTradepostBySlot == null || originalTradepostBySlot.Length != 9)
                throw new InvalidOperationException("Original tradepost snapshot missing");
            foreach (var g in GoodCatalog.All)
            {
                if (!api.IsTradeGoodAllowed((eGoods)g.Id))
                    throw new InvalidOperationException("Unsupported original good");
                var p = api.GetTradeBasePrice((eGoods)g.Id);
                prices.Add(ObservedTradeMarketSnapshot.FromOriginalPacked(g.Id, g.Name, p.BuyPrice, p.SellPrice));
            }

            for (int id = 1; id <= 8; id++)
            {
                bool human = live.is_valid_player(id), ai = live.is_skirmish_player(id);
                if (!human && !ai)
                    continue;
                if (human == ai || api.IsAIPlayer(id) != ai || !_roster.Contains(id))
                    throw new InvalidOperationException("Incomplete original player roles");
                bool alive = IsAlivePlayer(id);
#if DIPLOMACY_PRIVATE_TESTS
                // The dead original AI is still represented as a dead
                // canonical player; its inventory is NOT a tradable asset.
                // Active lords retain the original live-roster war check.
                var ruler = ReadLordProfile(id, alive);
#else
                var ruler = ReadLordProfile(id);
#endif
                players.Add(new CanonicalPlayer((byte)id, human, alive, 0));
                rulers.Add(ruler);

                // This is a native identity/GOLD observation in the SAME
                // original monitor and tick as prices, stocks, and pending
                // purchases. Slot alone never determines personality.
                var futurePurchases = new List<KeyValuePair<ushort,int>>();
                var demandSignals = new List<KeyValuePair<ushort,int>>();
                InternalAIC originalAic = default(InternalAIC);
                int nativeLord = 0, originalGold = 0, originalTeam = 0;
                int originalGoldReserveSignal = 0;
                string originalAicSignalHash = null;
                if (ai && alive)
                {
                    int capturedLord;
                    nativeLord = checked((int)api.GetAILord(id));
                    if (!_capturedLordIds.TryGetValue(id, out capturedLord) ||
                        capturedLord != nativeLord || nativeLord < 1 ||
                        nativeLord > 37 || nativeLord >= ScriptExtenderNativeBridge.AicCount())
                        throw new InvalidOperationException("Original AI lord identity drift");
                    originalGold = api.GetPlayerGold(id);
                    originalTeam = api.GetPlayerTeam(id);
                    if (originalGold < 0 || originalGold > 1000000000 ||
                        originalTeam < 0 || originalTeam > 255)
                        throw new InvalidOperationException("Original AI treasury/team not attested");
                    originalAic = ScriptExtenderNativeBridge.Aic(nativeLord);
                    originalGoldReserveSignal = Math.Max(0, originalAic.base_gold_reserves);
                    originalAicSignalHash = FingerprintOriginalAicSignals(nativeLord, originalAic);
                }
                foreach (var g in GoodCatalog.All)
                {
                    int quantity = GoodAmount(id, g.Id), producers;
                    activeProducers.TryGetValue(OriginalInventoryEvidence.ProducerKey(id, g.Id), out producers);
                    StockCell cell;
                    if (!OriginalInventoryEvidence.TryNativeBoundStock(id, g.Id, quantity, BoundedReserve(g.Id, quantity, ruler), producers, _capacityAuthority, out cell))
                        throw new InvalidOperationException("Original capacity not observed");
                    int originalPending = AttachAiDemandToSnapshot(id, g.Id, cell);
                    if (ai && alive)
                    {
                        futurePurchases.Add(new KeyValuePair<ushort,int>(g.Id, originalPending));
                        demandSignals.Add(new KeyValuePair<ushort,int>(g.Id,
                            ObservedOriginalAicDemandHint(originalAic,g.Id)));
                    }
                    _economy.Observe(id, g.Id, Math.Max(1, _lastProcessedDay), quantity, cell);
                    stocks.Set((byte)id, g.Id, cell);
                }
                if (ai && alive)
                    originalAiEconomy.Add(new CanonicalOriginalAiEconomy(
                        (byte)id, nativeLord, originalTeam, originalGold,
                        originalGoldReserveSignal, originalAicSignalHash,
                        futurePurchases, demandSignals, true, originalTradepostBySlot[id]));
            }

            if (players.Count != _roster.Distinct().Count())
                throw new InvalidOperationException("Original roster coverage changed");
            var pairs = new List<CanonicalPairObservation>();
            int day = Math.Max(1, _lastProcessedDay);
            foreach (var a in players)
                foreach (var b in players)
                    if (a.Slot != b.Slot)
                    {
                        ObservedTradePairState pair;
                        bool ab = api.IsPlayerAlliedTo(a.Slot, b.Slot), ba = api.IsPlayerAlliedTo(b.Slot, a.Slot);
                        if (!ReadTradePair(a.Slot, b.Slot, day, out pair))
                            pair = new ObservedTradePairState
                            {
                                Requester = a.Slot,
                                Recipient = b.Slot,
                                Day = day,
                                RequesterAlive = a.Alive,
                                RecipientAlive = b.Alive,
                                Authoritative = false,
                                HasTradeRelation = false,
                                Hostile = !(ab && ba)
                            };
                        pairs.Add(new CanonicalPairObservation(pair, ab, ba, pair.Authoritative && pair.HasTradeRelation && !(ab && ba)));
                    }

            return new CanonicalTradeSnapshot(tick, day, _mapFingerprint, _rosterFingerprint,
                players, stocks, prices, rulers, pairs, _mapEpoch, (byte)_localPlayer,
                originalAiEconomy);
        }

        private void EnsureCanonical(CanonicalTradeSnapshot world)
        {
            if (_canonical != null)
                return;
            ArchiveEnsure();
            EnsureMailbox();
            _canonical = _canonicalSaved == null ? CanonicalTradeReducer.MigrateLegacyAtSharedTick(world, MapGuid(_mapFingerprint, _rosterFingerprint), SinglePlayerTerm, _archive, _mailbox) : CanonicalTradeReducer.RestoreAtSharedTick(_canonicalSaved, world, MapGuid(_mapFingerprint, _rosterFingerprint), SinglePlayerTerm);
            _canonicalTick = world.SharedTick;
            ProjectCanonical(_canonical.SaveBytes());
            Log("trade_canonical", "status=single_owner_ready|next_id=" + _archive.NextId + "|saved_intents=" + _mailbox.Intents.Count + "|native_release=false");
        }

        private void QueueCanonicalLegacy(byte a, byte b, ushort give, ushort want, int amount, TradeOfferGenerosity tier, bool ai, TradeIntentAction action, ulong counterId)
        {
            if (ai || a != _localPlayer || a == b || b < 1 || b > 8 || _canonicalStopped || _canonicalInputs.Count >= 16 || _canonicalInputs.Any(x => x.A == a && x.B == b))
                throw new InvalidOperationException("Canonical input cannot be admitted");
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                throw new InvalidOperationException("Original engine busy");
            using (lease)
            {
                if (!NativeAuthorized())
                    throw new InvalidOperationException("Original native authority unavailable");
                EnsureCanonical(CaptureCanonicalWorld(ReadSimulationTick()));
                if (_mailbox.HasPending(a, b))
                    throw new InvalidOperationException("Reply pending");
                var c = action == TradeIntentAction.Propose ? null : _archive.Record(counterId);
                if (action != TradeIntentAction.Propose && (c == null || c.State != TradeDisposition.PendingCounter || c.Requester != a || c.Recipient != b))
                    throw new InvalidOperationException("Stale counter terms");
                _canonicalInputs.Add(new LocalTradeCommand { A = a, B = b, Give = give, Want = want, Amount = amount, Tier = (byte)tier, Action = action, Counter = c == null ? null : c.Copy() });
            }
        }

        private sealed class CanonicalTradeSnapshotSource : ICanonicalTradeSnapshotSource
        {
            internal CanonicalTradeSnapshot World;
            public CanonicalTradeSnapshot Capture(long tick)
            {
                if (World == null || World.SharedTick != tick)
                    throw new InvalidOperationException("Wrong original tick");
                return World;
            }
        }

        private sealed class CanonicalTradeOwnerReadback : ICanonicalTradeNativePort
        {
            internal DiplomacyFeature Owner;
            internal PreparedCanonicalTransition Transition;
            public StockCell Read(byte player, ushort good)
            {
                RequireNativeDiplomacySection();
                var expected = Transition.Cells().Single(x => x.Player == player && x.Good == good);
                StockCell observed = expected.BeforeCell();
                observed.Quantity = Owner.GoodAmount(player, good);
                return observed;
            }

            public void WriteAbsolute(byte player, ushort good, int amount)
            {
                throw new InvalidOperationException("Only the existing  native journal may write goods");
            }
        }

        private void CanonicalNativeBooked(ulong id)
        {
            var transition = _canonicalTransition;
            if (transition == null)
                return;
            if (transition.TradeId != id)
                throw new InvalidDataException("Wrong canonical native receipt");
            // Invoked after exact original four-cell readback and legacy goodwill
            // bookkeeping, inside its existing stable-save section. Adopt the prebuilt
            // mailbox projection so SaveData cannot observe Processing plus Committed.
            var after = CanonicalTradeCodec.Decode(transition.AfterState());
            _mailbox = after.Mailbox.Copy();
            _archive = after.Archive.Copy();
            _canonicalSaved = transition.AfterState();
        }

        private void PumpCanonicalSolo()
        {
            if (!_mapActive || _mapDisabled || _canonicalStopped)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                if (!NativeAuthorized())
                    return;
                try
                {
                    long tick = ReadSimulationTick();
                    if (tick <= _canonicalTick)
                        return;
                    var world = CaptureCanonicalWorld(tick);
                    EnsureCanonical(world);
                    if (tick <= _canonicalTick)
                        return;
                    _canonical.ObserveOriginalRebellionHistory(_mailbox);
                    _canonicalRecoverySeed = SnapshotSave();
                    var model = CanonicalTradeCodec.Decode(_canonical.SaveBytes());
                    var commands = new List<CanonicalTrustedHumanInput>();
                    ulong epoch = model.InputEpoch == 0 ? 1 : model.InputEpoch;
                    ulong sequence = model.InputSequence[_localPlayer], nonce = model.InputNonce[_localPlayer];
                    foreach (var input in _canonicalInputs)
                    {
                        var c = input.Counter;
                        var human = input.Consent;
                        if (human != null)
                        {
                            var response = new OrderedHumanTrade(checked(++sequence), epoch, checked(++nonce), input.A, input.B, input.ConsentAction == CanonicalTradeAction.CounterHuman ? input.Want : human.RequestedGood, input.ConsentAction == CanonicalTradeAction.CounterHuman ? input.Give : human.OfferedGood, input.ConsentAction == CanonicalTradeAction.CounterHuman ? input.Amount : human.RequestedAmount, input.ConsentAction == CanonicalTradeAction.CounterHuman ? input.Tier : human.Tier, input.ConsentAction, human.Id, human.RootId, human.OfferedAmount, human.IssuedDay, human.ExpiresDay, input.ConsentAction == CanonicalTradeAction.CounterHuman ? input.LiteralOfferedAmount : human.OfferedAmount, human.OfferedGood, human.RequestedGood, human.RequestedAmount);
                            commands.Add(new CanonicalTrustedHumanInput(response, (byte)_localPlayer, 0, _mapEpoch, true));
                            continue;
                        }

                        var command = new OrderedHumanTrade(checked(++sequence), epoch, checked(++nonce), input.A, input.B, input.Want, input.Give, input.Amount, input.Tier, input.Action == TradeIntentAction.Propose ? CanonicalTradeAction.Propose : input.Action == TradeIntentAction.AcceptCounter ? CanonicalTradeAction.AcceptCounter : CanonicalTradeAction.DeclineCounter, c == null ? 0 : c.Id, c == null ? 0 : c.RootId, c == null ? 0 : c.OfferedAmount, c == null ? 0 : c.IssuedDay, c == null ? 0 : c.ExpiryDay);
                        commands.Add(new CanonicalTrustedHumanInput(command, (byte)_localPlayer, 0, _mapEpoch, true));
                    }

                    _canonicalTransition = _canonical.AdvanceSharedTick(tick, new AuthenticatedTradeTick(model.InputEpoch == 0 ? new SignedTradeActivation(epoch) : null, commands), new CanonicalTradeSnapshotSource { World = world });
                    _canonicalTick = tick;
                    _canonicalInputs.Clear();
                    ProjectCanonical(_canonical.SaveBytes());
                    if (_canonicalTransition != null)
                        SettleCanonicalThroughExistingJournal(tick);
                    else
                        _canonicalRecoverySeed = null;
                }
                catch (Exception e)
                {
                    if (_canonicalTransition != null)
                    {
                        _canonicalStopped = true;
                        _tradeFaulted = true;
                    }

#if DIPLOMACY_PRIVATE_TESTS
                    string currentFailure = e.GetType().Name + ":" + e.Message;
                    if (_canonicalStopped ||
                        ShouldReportCanonicalWait(
                            currentFailure,_lastCanonicalWaitingReason,
                            _featureTicks,_lastCanonicalWaitingFrame))
                    {
                        _lastCanonicalWaitingReason = currentFailure;
                        _lastCanonicalWaitingFrame = _featureTicks;
                        Log("trade_canonical",
                            "status=" + (_canonicalStopped ? "quarantined" : "waiting") +
                            "|reason=" + Safe(currentFailure) +
                            "|duplicate_wait_logs_bounded=true");
                    }
#else
                    Log("trade_canonical", "status=" + (_canonicalStopped ? "quarantined" : "waiting") + "|reason=" + Safe(e.GetType().Name + ":" + e.Message));
#endif
                }
            }
        }

        private void SettleCanonicalThroughExistingJournal(long tick)
        {
            RequireNativeDiplomacySection();
            var transition = _canonicalTransition;
            var quote = transition.Quote();
            var record = _archive.Record(transition.TradeId);
            if (record == null || record.State != TradeDisposition.PreparingNative)
                throw new InvalidDataException("Canonical prepared receipt missing");
            var port = new CanonicalTradeOwnerReadback
            {
                Owner = this,
                Transition = transition
            };
            // The old executor must create its original two-leg recovery journal.
            // These projections do not own an allocator and are hidden under both locks.
            _archive.Records.Remove(record);
            _archive.PendingNativeId = 0;
            string failure;
            bool committed = ExecuteNativeBarter(quote, record.Copy(), out failure);
            if (committed)
                _canonical.CommitPrepared(transition, tick, port);
            else if (_pendingPackage != null)
            {
                // Preserve the real unresolved native journal and its Preparing state.
                // Never replace it with a guessed refund or a terminal fake receipt.
                _canonicalSaved = transition.BeforeState();
                _canonicalStopped = true;
                _tradeFaulted = true;
                _goodsTransfersUnavailable = true;
                Log("trade_canonical", "status=unresolved_original_journal_quarantined|reason=" + Safe(failure));
                return;
            }
            else
                _canonical.RejectPrepared(transition, tick, port, false);
            _canonicalTransition = null;
            _canonicalRecoverySeed = null;
            ProjectCanonical(_canonical.SaveBytes());
            if (_canonical.Quarantined)
            {
                _canonicalStopped = true;
                _tradeFaulted = true;
            }

            Log("trade_canonical", "status=" + (committed ? "native_committed" : "native_declined") + "|id=" + transition.TradeId + "|reason=" + Safe(failure));
        }
    }
}
