//  staged GAME-BOUND trade: native goods + journal, all within the
// existing DIPLOMACY-ExtendedState native inventory section, save codec, and UI owner.
// Statically fail-closed until game/Serps Native + SaveData are officially
// attested. This code is NOT an installable artifact.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private TradeHistoryArchive _archive;
        // Never wired to native gifts. This memory is only restored from a
        // checksummed, map/roster-scoped optional schema15 save component.
        private AidLedger _aidMemory;
        private bool _tradeFaulted;
        private bool _authorityLogged, _firstDenialLogged;
        private string _lastTradeResult = string.Empty;
        private const ulong SinglePlayerTerm = 1;
        private const int MaximumUnitQuote = 100000;
        private void ResetTradeSession()
        {
            _economy.Reset();
            _archive = null;
            // Map isolation: promises cannot leak into another skirmish.
            _aidMemory = null;
#if DIPLOMACY_PRIVATE_TESTS
            _lastAidPlanningDay = -1;
            _aidRetryFrame = 0;
            // Round-robin fairness is MAP-LOCAL ephemeral scheduler state.
            // A new map on the same day number must never inherit the
            // previous skirmish's donor/pledge starting offset.
            // This changes NO saved aid promise, favor, native mutation,
            // recovery journal, lease acquisition or release authority.
            _roundRobinAidDay = -1;
            _roundRobinAidCursor = 0;
            _unattestedSavedAid = false;
            _activeAidPromise = null;
#endif
            _unattestedSavedNativeTrade = false;
            _tradeFaulted = false;
            _authorityLogged = false;
            _firstDenialLogged = false;
            _lastTradeResult = string.Empty;
            // Log exactly once per active map; do not repeatedly re-hash
            // the original managed/native game every UI frame.
            _releaseAdmissionLogged = false;
            // original save evidence: the previous original SaveData readback belongs to the
            // PREVIOUS map, never to the newly bootstrapped skirmish.
            ClearOriginalSaveEvidence();
        }

        private static Guid MapGuid(string map, string roster)
        {
            if (string.IsNullOrEmpty(map) || string.IsNullOrEmpty(roster))
                throw new InvalidOperationException("Trade session has no stable roster");
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(map + "|" + roster + StrongholdEuropeDE.Runtime.CompatibilityContracts.TradeMapFingerprintSuffix));
                byte[] id = new byte[16];
                Buffer.BlockCopy(digest, 0, id, 0, id.Length);
                return new Guid(id);
            }
        }

        private bool NativeAuthorized()
        {
            // Active map scope and the exact runtime contract must agree.
            // UI input, network messages and a build flag cannot independently
            // authorize native goods. The normal release stays gated until its
            // engine, simulation, save and reload acceptance is established.
            return _mapActive && !_mapDisabled && !_mapBootstrapPending &&
                   !_goodsTransfersUnavailable && !_inventoryRestorePending &&
                   _nativeTransferProbe == null && _pendingPackage == null &&
                   _pendingTeamTransition == null && !_tradeFaulted &&
                   VerifiedNativeCommitGate() &&
                   OriginalSoloWarehouseCapacityReader.IsSoloHumanHostSkirmish();
        }

        // Require a verified engine ABI, live capacity, and the mutation gate.
        // Private runs also need a current original save and inventory lease.
        private bool VerifiedNativeCommitGate()
        {
#if DIPLOMACY_PRIVATE_TESTS
            // Authorize the exact engine, live inventory owner, and current save.
            string reason;
            bool ready = TryAuthorizeNativeInventory(out reason);
            if (ready && !_authorityLogged)
            {
                _authorityLogged = true;
                Log("trade_session", "status=native_inventory_contract_ready|scope=isolated_solo_host");
            }
            else if (!ready && !_firstDenialLogged)
            {
                _firstDenialLogged = true;
                Log("trade_session", "status=native_inventory_waiting|reason=" + Safe(reason));
            }

            return ready;
#else
            // Report the unmet prerequisites for release authorization.
            LogNormalSoloReleaseGateOnce();
            // Normal builds retain the existing release fence. A private
            // runtime contract is not a multiplayer or broad-release approval.
            // Native capacity reads still require the exclusive engine monitor.
            if (_capacityAuthority is OriginalSoloWarehouseCapacityReader)
            {
                string exactOriginalDiagnostic;
                bool valid = OriginalSoloWarehouseCapacityReader.ProvenanceReady(out exactOriginalDiagnostic);
                if (valid && !_authorityLogged)
                {
                    _authorityLogged = true;
                    Log("trade_capacity", "status=original_native_provenance_attested" + "|scope=isolated_solo_host");
                }
                else if (!valid && !_firstDenialLogged)
                {
                    _firstDenialLogged = true;
                    Log("trade_capacity", "status=not_yet_attested" + "|reason=" + Safe(exactOriginalDiagnostic));
                }

                //  independent safety correction: a Steam/native hash and
                // solo host provenance cannot attest durable, atomic goods +
                // original-engine GameSave commits. Never drop either lock.
                // Until *all* three runtime authorities exist, fail closed.
                return valid && StrictNativeTradeAuthorities.NativeGoodsAndSaveCommitAuthority && SerpsRawraCompat.CanMutateGameGoods && SerpsRawraCompat.AttestedLoadedNativeCompatibility(out exactOriginalDiagnostic);
            }

            string diagnostic;
            if (!SerpsRawraCompat.AttestedLoadedNativeCompatibility(out diagnostic))
                return false;
            if (_capacityAuthority == null || _capacityAuthority is OriginalInventoryEvidence.UnavailableOriginalWarehouseCapacity)
                return false;
            return SerpsRawraCompat.CanMutateGameGoods;
#endif
        }

        private void ArchiveEnsure()
        {
            if (_archive != null)
            {
                _archive.Validate(_mapFingerprint, _rosterFingerprint, Math.Max(1, _lastProcessedDay));
                return;
            }

            _archive = new TradeHistoryArchive
            {
                MapFingerprint = _mapFingerprint,
                RosterFingerprint = _rosterFingerprint
            };
        }

        private void RestoreTradeArchive(TradeHistoryArchive saved)
        {
            if (saved == null)
            {
                _archive = new TradeHistoryArchive
                {
                    MapFingerprint = _mapFingerprint,
                    RosterFingerprint = _rosterFingerprint
                };
                return;
            }

            saved.Validate(_mapFingerprint, _rosterFingerprint, Math.Max(1, _lastProcessedDay));
            _archive = saved.Copy();
            // Restore only a genuine, still pending counter addressed to this
            // local player. No trade is executed during save-load.
            var waiting = _archive.Records.Where(x => x.State == TradeDisposition.PendingCounter && x.Requester == _localPlayer && x.ExpiryDay >= Math.Max(1, _lastProcessedDay)).OrderByDescending(x => x.Id).FirstOrDefault();
            _uiCounterId = waiting == null ? 0 : waiting.Id;
            _uiCounterpart = waiting == null ? 0 : waiting.Recipient;
            _tradeFaulted = saved.Records.Any(r => r.State == TradeDisposition.Quarantined);
        }

#if DIPLOMACY_PRIVATE_TESTS
        // The interactive gui load original game has AI slot 4 eliminated BEFORE the human
        // opens any diplomacy panel. The canonical world includes that
        // original slot as Alive=false, but the native alive roster rightly
        // excludes it. Never demand a "live owner's war risk" for a dead
        // player and accidentally disable ALL surviving player trades.
        // All existing active-sender calls still require an exact native
        // living roster and native engine lease.
        private Ruler ReadLordProfile(int player, bool? originalAlive = null)
#else
        private Ruler ReadLordProfile(int player)
#endif
        {
            int trust = 50, greed = 40, caution = 50, commerce = 50;
            // Live SHCDESE IsPlayerAlliedTo/GetAlivePlayerIds observation.
            // Conservatively treat any nonallied living player as military risk.
#if DIPLOMACY_PRIVATE_TESTS
            bool war = !originalAlive.HasValue || originalAlive.Value
                ? ActualHostileExposure(player)
                : false;
#else
            bool war = ActualHostileExposure(player);
#endif
            string coalitionId;
            RelationState relation;
            if (_playerCoalition.TryGetValue(player, out coalitionId) && _relations.TryGetValue(coalitionId, out relation) && relation.ExtendedState != null)
            {
                trust = (int)Math.Max(0m, Math.Min(100m, relation.ExtendedState.Trust));
                greed = (int)Math.Max(5m, Math.Min(95m, relation.ExtendedState.Grudge));
                caution = 40 + (int)Math.Min(40m, relation.ExtendedState.StrategicThreat);
                commerce = 65;
            }

            int lordId;
            if (_capturedLordIds.TryGetValue(player, out lordId) && lordId >= 0 && lordId < ScriptExtenderNativeBridge.AicCount())
            {
                var aic = ScriptExtenderNativeBridge.Aic(lordId);
                greed = Math.Min(100, Math.Max(0, 10 + aic.send_goods_margin * 3 / 4));
                caution = Math.Min(100, Math.Max(0, caution + aic.chance_of_defensive1 / 5));
                commerce = Math.Min(100, Math.Max(0, 35 + aic.trade_wood_at / 5 + aic.trade_stone_at / 5));
            }

            return new Ruler
            {
                PlayerId = player,
                Name = PlayerName(player),
                Trust = trust,
                Greed = greed,
                Caution = caution,
                Commerce = commerce,
                AtWar = war
            };
        }

        private static int BoundedReserve(ushort good, int nativeQuantity, Ruler personality)
        {
            // No authoritative production/consumption ledger is exposed by
            // SHCDESE 2.14.1. Use a conservative minimum 65% stock holdback
            // PLUS strategic category buffers, never claim this is a real
            // production forecast. Pending transaction re-reads all four.
            int strategic = good >= 17 ? 80 : good >= 9 && good <= 14 ? 55 : 40;
            if (personality.AtWar && good >= 17)
                strategic += 55;
            int percentile = (int)Math.Min(int.MaxValue, (long)nativeQuantity * 65L / 100L);
            return Math.Min(nativeQuantity, Math.Max(strategic, percentile));
        }

        private bool ReadNativeWorld(out DiplomaticTradeNegotiator world, out ManagedGoodsSnapshotPort simulation)
        {
            world = null;
            simulation = null;
            if (!NativeAuthorized())
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            try
            {
                using (lease)
                {
                    var players = _roster.Distinct().Where(p => p >= 1 && p <= 8).OrderBy(p => p).ToArray();
                    if (players.Length < 2 || !players.Contains(_localPlayer))
                        return false;
                    var model = new ManagedGoodsSnapshotPort();
                    var personalities = new List<Ruler>();
                    var market = new List<Good>();
                    var api = GamePlayerManagerAPI.Instance;
                    if (api == null)
                        return false;
                    var producerObservations = ObserveRealActiveGoodsProducers();
                    var stackObservations = ObserveOriginalLocalGoodStacks();
                    foreach (Good g in GoodCatalog.All)
                    {
                        if (!api.IsTradeGoodAllowed((eGoods)g.Id))
                            return false;
                        PackedGoodPrice p = api.GetTradeBasePrice((eGoods)g.Id);
                        market.Add(ObservedTradeMarketSnapshot.FromOriginalPacked(g.Id, g.Name, p.BuyPrice, p.SellPrice));
                    }

                    foreach (int player in players)
                    {
                        if (!IsAlivePlayer(player))
                            continue;
                        Ruler ruler = ReadLordProfile(player);
                        personalities.Add(ruler);
                        foreach (Good g in GoodCatalog.All)
                        {
                            int quantity = GoodAmount(player, g.Id);
                            if (quantity < 0 || quantity > int.MaxValue - 200000)
                                return false;
                            int reserve = BoundedReserve(g.Id, quantity, ruler);
                            // A quantity alone does not prove free warehouse
                            // space. Original 's (quantity+200000) was fictitious.
                            int observedActive = 0;
                            producerObservations.TryGetValue(OriginalInventoryEvidence.ProducerKey(player, g.Id), out observedActive);
                            // A sum of exact native stack values MAY establish
                            // a local candidate, never sufficient game capacity
                            // authority. This diagnostic does not enable writes.
                            int localCapacityCandidate;
                            bool localStackParity = OriginalInventoryEvidence.TryCandidateLocalStackCapacity(player, g.Id, quantity, stackObservations, out localCapacityCandidate);
                            StockCell evidence;
                            if (!OriginalInventoryEvidence.TryNativeBoundStock(player, g.Id, quantity, reserve, observedActive, _capacityAuthority, out evidence))
                            {
                                Log("trade_evidence", "status=unattested_native_warehouse_capacity|player=" + player + "|good=" + g.Id + "|localStackParity=" + (localStackParity ? "yes" : "no") + "|candidateOnly=" + (localStackParity ? localCapacityCandidate : 0));
                                return false; // no Native offer or goods mutation
                            }

                            AttachAiDemandToSnapshot(player, g.Id, evidence);
                            _economy.Observe(player, g.Id, Math.Max(1, _lastProcessedDay), quantity, evidence);
                            model.Set((byte)player, g.Id, evidence);
                        }
                    }

                    if (personalities.Count < 2)
                        return false;
                    ArchiveEnsure();
                    var prototype = new DiplomaticTradeNegotiator(model, new ObservedTradeMarketSnapshot(market), personalities, MapGuid(_mapFingerprint, _rosterFingerprint), SinglePlayerTerm, Math.Max(1, _lastProcessedDay), unchecked((int)(_sessionGeneration | 1)), _archive.NextId, new LiveTradePairPolicy(this));
                    var prior = _archive.Relationships.Select(x => new ManagedTradeRelationship { A = x.A, B = x.B, Goodwill = x.Goodwill, ConsecutiveRejected = x.RejectionStreak, LastRejectedDay = x.LastRejectedDay, LastAttemptDay = x.LastAttemptDay, LastPenaltyDay = x.LastPenaltyDay, RejectedToday = x.RejectedToday, AcceptedTrades = x.AcceptedTrades, LastDealDay = x.LastDealDay }).ToArray();
                    prototype.Relationships.Restore(prior);
                    foreach (int a in players)
                        foreach (int b in players)
                            if (a < b)
                            {
                                if (!_archive.Relationships.Any(x => x.A == a && x.B == b))
                                {
                                    int initial = 50;
                                    string c;
                                    RelationState rel;
                                    if ((a == _localPlayer || b == _localPlayer) && _playerCoalition.TryGetValue(a == _localPlayer ? b : a, out c) && _relations.TryGetValue(c, out rel) && rel.ExtendedState != null)
                                        initial = (int)Math.Max(0m, Math.Min(100m, rel.ExtendedState.Trust));
                                    prototype.Relationships.Initialize((byte)a, (byte)b, initial);
                                }
                            }

                    simulation = model;
                    world = prototype;
                    return true;
                }
            }
            catch (Exception e)
            {
                Log("trade_gameplay", "status=stock_snapshot_failed|error=" + Safe(e.GetType().Name));
                return false;
            }
        }

        private void AdoptRelationships(DiplomaticTradeNegotiator world)
        {
            if (world == null)
                return;
            foreach (ManagedTradeRelationship record in world.Relationships.Snapshot())
            {
                TradeRelationship x = _archive.Relation(record.A, record.B);
                x.Goodwill = record.Goodwill;
                x.RejectionStreak = record.ConsecutiveRejected;
                x.LastRejectedDay = record.LastRejectedDay;
                x.LastAttemptDay = record.LastAttemptDay;
                x.LastPenaltyDay = record.LastPenaltyDay;
                x.RejectedToday = record.RejectedToday;
                x.AcceptedTrades = record.AcceptedTrades;
                x.LastDealDay = record.LastDealDay;
            }
        }

        private void ApplyDiplomacyTradeEffect(int other, bool accepted, int goodwillBefore, int goodwillAfter)
        {
            if (other == _localPlayer)
                return;
            string coalitionId;
            RelationState relation;
            if (!_playerCoalition.TryGetValue(other, out coalitionId) || !_relations.TryGetValue(coalitionId, out relation) || relation.ExtendedState == null)
                return;
            int delta = goodwillAfter - goodwillBefore;
            var currentSave = relation.ExtendedState;
            if (delta > 0)
            {
                currentSave.Trust = Math.Min(currentSave.TrustCeiling, currentSave.Trust + Math.Min(6, delta) / 2m);
                currentSave.Grudge = Math.Max(0m, currentSave.Grudge - 1m);
                relation.Goodwill = Math.Min(100m, relation.Goodwill + Math.Min(5, delta));
            }
            else if (delta < 0)
            {
                currentSave.Trust = Math.Max(0m, currentSave.Trust + delta / 2m);
                currentSave.Grudge = Math.Min(100m, currentSave.Grudge + Math.Min(10, -delta));
                relation.Goodwill = Math.Max(0m, relation.Goodwill + delta);
            }
        }

        private bool ExecuteNativeBarter(BarterQuote q, TradeRecord record, out string failure)
        {
            failure = "native_commit_not_authorized";
            if (q == null || record == null || !NativeAuthorized())
                return false;
            if (!CanTradePair(q.Requester, q.Recipient, out failure))
                return false;
            if (_archive == null || _archive.PendingNativeId != 0 || _archive.Record(record.Id) != null)
                return false;
            if (q.MapSession != MapGuid(_mapFingerprint, _rosterFingerprint) || q.HostTerm != SinglePlayerTerm || q.Id != record.Id || q.Requester != record.Requester || q.Recipient != record.Recipient || q.OfferedGood != record.OfferedGood || q.RequestedGood != record.RequestedGood || q.OfferedAmount != record.OfferedAmount || q.RequestedAmount != record.RequestedAmount)
                return false;
            // refuse an unsaveable trade counter BEFORE entering the
            // native inventory path. No ledger is created or mutated here.
            // Keep the later managed-commit validation as a second fence.
            int pair = TradeHistoryArchive.PairKey(q.Requester, q.Recipient);
            TradeRelationship existingTradeRelation = _archive.Relationships.FirstOrDefault(r => TradeHistoryArchive.PairKey(r.A, r.B) == pair);
            if (existingTradeRelation != null && !TradeHistoryArchive.CanBookAnotherTrade(existingTradeRelation.AcceptedTrades))
            {
                failure = "accepted_trade_count_exhausted_before_native_write";
                return false;
            }

            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
            {
                failure = "native_busy";
                return false;
            }

            bool complete = false;
            try
            {
                using (lease)
                {
                    AtomicBoundary.MutationScope saveHookScope;
#if DIPLOMACY_PRIVATE_TESTS
                    string Preflight;
                    if (!TryAuthorizeNativeInventory(out Preflight))
                    {
                        failure = "native_contract_before_mutation:" + Preflight;
                        return false;
                    }

#endif
                    if (!BeginNativeMutation(out saveHookScope))
                    {
                        failure = "native_save_or_trade_busy";
                        return false;
                    }

                    try
                    {
                        string pairDenial;
                        if (!CanTradePair(q.Requester, q.Recipient, out pairDenial))
                            throw new InvalidOperationException("trade relationship changed: " + pairDenial);
                        if (!IsAlivePlayer(q.Requester) || !IsAlivePlayer(q.Recipient))
                            throw new InvalidOperationException("trade party dead");
                        // Native prices can change after the earlier AI snapshot.
                        // Recheck the exact frozen terms under the same engine lease.
                        if (!CurrentNativeEconomics(q))
                        {
                            failure = "market_changed_before_native_commit";
                            throw new InvalidOperationException(failure);
                        }

                        int a = GoodAmount(q.Requester, q.OfferedGood);
                        int b = GoodAmount(q.Recipient, q.OfferedGood);
                        int c = GoodAmount(q.Recipient, q.RequestedGood);
                        int e = GoodAmount(q.Requester, q.RequestedGood);
                        if (a < q.OfferedAmount || c < q.RequestedAmount || b > int.MaxValue - q.OfferedAmount || e > int.MaxValue - q.RequestedAmount)
                        {
                            failure = "stocks_changed_before_native_commit";
                            throw new InvalidOperationException("native inventory changed or overflow");
                        }

                        // BEFORE archiving PREPARED or the first native
                        // debit, use one shared exclusive simulation lease and
                        // require an authoritative current capacity for ALL four
                        // goods cells. The  local-stack parity candidate does
                        // NOT qualify as an authority; no cap is inferred.
                        var requestedSnapshot = new FourCellSnapshot
                        {
                            Requester = q.Requester,
                            Recipient = q.Recipient,
                            OfferedGood = q.OfferedGood,
                            RequestedGood = q.RequestedGood,
                            OfferedAmount = q.OfferedAmount,
                            RequestedAmount = q.RequestedAmount,
                            RequesterOfferedBefore = a,
                            RecipientOfferedBefore = b,
                            RecipientRequestedBefore = c,
                            RequesterRequestedBefore = e,
                            RequesterOfferedReserve = ReserveWithPlannedPurchases(q.Requester, q.OfferedGood, a, ReadLordProfile(q.Requester)),
                            RecipientRequestedReserve = ReserveWithPlannedPurchases(q.Recipient, q.RequestedGood, c, ReadLordProfile(q.Recipient))
                        };
                        FourCellSnapshot attestedSnapshot;
                        string nativeCapacityDenial;
                        if (!FourCellPrecommit.TryAuthorizeWithCapacity(requestedSnapshot, _capacityAuthority, out attestedSnapshot, out nativeCapacityDenial))
                            throw new InvalidOperationException("Native four-cell admission refused: " + (nativeCapacityDenial ?? "missing_capacity"));
                        ArchiveEnsure();
                        // The rollback seed must be a coherent pre-transaction
                        // save, before claiming a pending quote or native writes.
                        _inventoryRecoveryBaseState = CaptureInventoryRecoveryBaseState();
                        var preparing = record.Copy();
                        preparing.State = TradeDisposition.PreparingNative;
                        var preparedPackage = new DiplomacyPendingPackage
                        {
                            SenderPlayer = q.Requester,
                            TargetPlayer = q.Recipient,
                            GameDay = _lastProcessedDay,
                            TradeRecordId = q.Id,
                            Lines = new List<DiplomacyPendingTransfer>
                            {
                                new DiplomacyPendingTransfer
                                {
                                    SenderPlayer = q.Requester,
                                    TargetPlayer = q.Recipient,
                                    GoodId = q.OfferedGood,
                                    Amount = q.OfferedAmount,
                                    Purpose = DiplomacyTransferPurpose.Barter,
                                    SenderBefore = a,
                                    RecipientBefore = b,
                                    DirectAtomicTransfer = true,
                                    GameDay = _lastProcessedDay,
                                    Progress = new DiplomacyTransferProgress()
                                },
                                new DiplomacyPendingTransfer
                                {
                                    SenderPlayer = q.Recipient,
                                    TargetPlayer = q.Requester,
                                    GoodId = q.RequestedGood,
                                    Amount = q.RequestedAmount,
                                    Purpose = DiplomacyTransferPurpose.Barter,
                                    SenderBefore = c,
                                    RecipientBefore = e,
                                    DirectAtomicTransfer = true,
                                    GameDay = _lastProcessedDay,
                                    Progress = new DiplomacyTransferProgress()
                                }
                            }
                        };
                        // Allocate the complete journal before claiming its ID. A
                        // failed allocation cannot leave an orphan PREPARING receipt.
                        _archive.Add(preparing);
                        _archive.PendingNativeId = record.Id;
                        _pendingPackage = preparedPackage;
                        try
                        {
                            foreach (DiplomacyPendingTransfer line in _pendingPackage.Lines)
                            {
                                NoteNativeWrite();
                                DiplomacyTransferRecovery.ApplyLine(line, GoodAmount, ChangeTransferAmount);
                                if (line.Progress.RecipientAfter != line.RecipientBefore + line.Amount)
                                    throw new InvalidOperationException("Native recipient capacity changed; no partial barter accepted");
                            }

                            complete = true;
                        }
                        catch
                        {
                            _pendingPackage.RollbackPending = true;
                            bool nativeRollbackVerified = false;
                            try
                            {
                                nativeRollbackVerified = DiplomacyTransferRecovery.Rollback(_pendingPackage, GoodAmount, RestoreGoodAmount);
                            }
                            catch (Exception rollbackException)
                            {
                                // Rollback exceptions must never become an ordinary
                                // cancellation of an already-touched native cell.
                                _tradeFaulted = true;
                                failure = "rollback_exception_quarantine";
                                try
                                {
                                    Log("trade_rebellion", "status=rollback_failed|reason=" + Safe(rollbackException.GetType().Name));
                                }
                                catch (Exception)
                                {
                                }
                            }

                            if (!nativeRollbackVerified)
                            {
                                _tradeFaulted = true;
                                if (failure != "rollback_exception_quarantine")
                                    failure = "rollback_pending_quarantine";
                            }
                            else
                            {
                                _pendingPackage = null;
                                _archive.PendingNativeId = 0;
                                preparing.State = TradeDisposition.Quarantined;
                                TradeRecord persisted = _archive.Record(record.Id);
                                if (persisted != null)
                                    persisted.State = TradeDisposition.Cancelled;
                                _inventoryRecoveryBaseState = null;
                                NoteNativeSettled();
                                failure = "native_rolled_back";
                            }

                            throw;
                        }

                        //  SP: retain *the SAME EngineInterface.threadLock* for
                        // native two-leg goods, exact readbacks, ExtendedState managed ledger
                        // and current SaveData epoch. A native Save or simulation
                        // tick cannot observe a half-settled trade between them.
                        if (complete)
                        {
                            try
                            {
                                CommitPendingPackage();
                                NoteNativeSettled();
                                // Trend accounting cannot invalidate a settled trade.
                                try
                                {
                                    RecordCommittedBarter(record);
                                }
                                catch (Exception)
                                {
                                    _economy.Reset();
                                }

                                failure = null;
                                return true;
                            }
                            catch (Exception commitFailure)
                            {
                                // A successful managed booking clears _pendingPackage.
                                // A later log/settlement callback may still throw.
                                // A null journal is NEVER proof of a completed refund:
                                // never claim "native_rolled_back" or run blind rollback.
                                if (_pendingPackage == null)
                                {
                                    _tradeFaulted = true;
                                    _goodsTransfersUnavailable = true;
                                    failure = "native_commit_state_uncertain_quarantined";
                                    try
                                    {
                                        Log("trade_rebellion", "status=post_commit_fault_no_rollback_claim|reason=" + Safe(commitFailure.GetType().Name));
                                    }
                                    catch (Exception)
                                    {
                                    }

                                    return false;
                                }

                                // Native goods have already changed. Restore them by
                                // the SAME ExtendedState two-leg recovery path, or quarantine if
                                // concurrent Native events make undo unverifiable.
                                // Do not allow a second ACCEPT or hidden partial gift.
                                _tradeFaulted = true;
                                string undo = "commit_rollback_unavailable";
                                try
                                {
                                    undo = AbortPendingPackage();
                                }
                                catch (Exception undoFailure)
                                {
                                    Log("trade_precommit", "status=commit_rollback_threw|reason=" + Safe(undoFailure.GetType().Name));
                                }

                                if (undo == null)
                                    NoteNativeSettled();
                                failure = undo == null ? "managed_commit_failed_native_rolled_back" : "managed_commit_failed_native_quarantined";
                                Log("trade_precommit", "status=managed_commit_fault|reason=" + Safe(commitFailure.GetType().Name));
                                return false;
                            }
                        }
                    }
                    finally
                    {
                        EndNativeMutation(saveHookScope);
                    }
                }
            }
            catch (Exception e)
            {
                if (failure == "native_commit_not_authorized")
                    failure = "native_failed_" + e.GetType().Name;
                Log("trade_gameplay", "status=native_commit_fail|id=" + record.Id + "|reason=" + Safe(failure));
            }

            return false;
        }

        private void CommitNativeTradePackage(DiplomacyPendingPackage p)
        {
            if (p == null || p.TradeRecordId == 0 || p.RollbackPending || p.Lines == null || p.Lines.Count != 2 || _archive == null || _archive.PendingNativeId != p.TradeRecordId || p.Lines.Any(x => !DiplomacyTransferRecovery.HasCompletedNativeWrites(x)))
                throw new InvalidOperationException("No complete trusted  barter receipt");
            TradeRecord result = _archive.Record(p.TradeRecordId);
            if (result == null || result.State != TradeDisposition.PreparingNative || p.Lines[0].SenderPlayer != result.Requester || p.Lines[0].TargetPlayer != result.Recipient || p.Lines[0].GoodId != result.OfferedGood || p.Lines[0].Amount != result.OfferedAmount || p.Lines[1].SenderPlayer != result.Recipient || p.Lines[1].TargetPlayer != result.Requester || p.Lines[1].GoodId != result.RequestedGood || p.Lines[1].Amount != result.RequestedAmount)
                throw new InvalidDataException("Barter journal does not match its exact request");
            // On restore, do not trust journal flags alone: engine goods
            // must contain precisely all four expected after-values.
            IDisposable auditLease;
            if (!TryEnterNativeInventorySection(out auditLease))
                throw new InvalidOperationException("Native barter readback lease busy");
            using (auditLease)
            {
                foreach (var line in p.Lines)
                {
                    if (GoodAmount(line.SenderPlayer, line.GoodId) != line.SenderBefore - line.Amount || GoodAmount(line.TargetPlayer, line.GoodId) != line.RecipientBefore + line.Amount)
                        throw new InvalidOperationException("Native barter after-state conflicts with its journal");
                }

                ObservedNativeTrade observed = CaptureCompletedNativeTrade(p);
                TradeHistoryArchive previous = _archive.Copy();
                DiplomacyManagedStateMemento oldManaged = CaptureManagedStateMemento();
                bool stable = false;
                try
                {
                    BeginStableSaveSection("barter_journal_commit", null);
                    stable = true;
                    result.State = TradeDisposition.Committed;
                    _archive.PendingNativeId = 0;
                    // Diplomacy relations are part of the SAME managed commit.
                    // Recovering a fully applied native barter commits them once,
                    // before clearing the persisted package journal.
                    PositiveBooked(result);
                    CanonicalNativeBooked(result.Id);
                    _pendingPackage = null;
                    _inventoryRecoveryBaseState = null;
                    _goodsTransfersUnavailable = false;
                    _observedNativeTrade = observed;
#if DIPLOMACY_PRIVATE_TESTS
                    if (_qaGeneration == _sessionGeneration && observed.Id == _qaExpectedTrade)
                    {
                        _qaObservedTrade = observed;
                        HeadlessQaDriver.FreezeObservedNativeTrade();
                    }
#endif
                }
                catch
                {
                    _archive = previous;
                    // A failure after PositiveBooked must undo BOTH the trade
                    // archive and the genuine ExtendedState Trust/Grudge/Goodwill ledger.
                    try
                    {
                        RestoreManagedStateMemento(oldManaged);
                    }
                    finally
                    {
                        _tradeFaulted = true;
                    }

                    throw;
                }
                finally
                {
                    if (stable)
                        EndStableSaveSection();
                }

                // Logging is strictly diagnostic: a logger failure AFTER native
                // cells and the managed ledger have committed is not a rollback.
                // Otherwise the outer catch could attempt undo with no journal.
                try
                {
                    Log("trade_gameplay", "status=committed|id=" + result.Id);
                }
                catch (Exception)
                {
                }

#if DIPLOMACY_PRIVATE_TESTS
                // After all native and managed commit steps and after the
                // stable-save section. This observes the already captured
                // four-cell receipt, including autonomous AI barter; it does
                // not depend on the singled-out QA ID or any fresh native read.
                // Contain even logger/formatter/allocation failures HERE, so
                // they cannot escape into outer compensation or quarantine.
                try
                {
                    TraceCommittedNativeBarterFourCells(observed);
                }
                catch (Exception)
                {
                // Committed trade remains committed; diagnostic is optional.
                }
#endif
            } // Retain the original engine lease through managed publication too.
        }

        private void RecordNativeTradeAbort(ulong id)
        {
            if (_archive == null || id == 0)
                return;
            TradeRecord record = _archive.Record(id);
            if (record != null && record.State == TradeDisposition.PreparingNative)
                record.State = TradeDisposition.Cancelled;
            if (_archive.PendingNativeId == id)
                _archive.PendingNativeId = 0;
        }
    }
}
