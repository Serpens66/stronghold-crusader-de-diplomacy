// autonomous AI-to-AI relief using real, observed native goods.
// A promise is not delivery. Native two-cell readback, the existing saved
// rollback journal and committed aid receipt must ALL succeed before credit.
// Private pinned game only until independent cold-save/MP attestation.
// The normal release never invokes or authorizes this lane.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Linq;
using System.Collections.Generic;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private int _lastAidPlanningDay = -1;
        // A planner-day seal belongs to the ORIGINAL loaded map
        // generation, never the lifetime of a reusable plugin instance.
        // The persisted AidLedger is NOT reset by a normal epoch change.
        private long _lastAidPlanningGeneration = long.MinValue;
        private long _aidRetryFrame;
        internal static bool NewOriginalAidPlanningGeneration(long previous,
            long current)
        {
            return current >= 0 && previous != current;
        }
        private int _roundRobinAidDay = -1;
        private ulong _roundRobinAidCursor;
        private bool _unattestedSavedAid;
        private AidPledge _activeAidPromise;
        // Only for the test runner's later authorized, explicitly enabled private
        // Headless AI Gift proof. This probe is OBSERVATION/LOGGING only:
        // it never writes stock, fabricates prices, quests, native commands,
        // Steam identities, original saves or original AI team roles.
        private static bool PrivateHeadlessAidProbe
        {
            get
            {
                try
                {
                    return Environment.GetEnvironmentVariable("DIPLOMACY_AID_PROBE") == "1";
                }
                catch (Exception)
                {
                    // An unavailable optional probe must not veto aid planning.
                    return false;
                }
            }
        }

        private void HeadlessAidTrace(string status, string evidence)
        {
            try
            {
                if (PrivateHeadlessAidProbe)
                    Log("aid_probe", "status=" + status + "|" + evidence);
            }
            catch (Exception)
            {
            // Optional telemetry is never a planner/native authority gate.
            }
        }

        private void PumpAutonomousAiAid()
        {
            // Explicit opt-in: never start new native movements during the
            // earlier four-cell QA unless the isolated operator selects aid.
            if (Environment.GetEnvironmentVariable("DIPLOMACY_ENABLE_AUTONOMOUS_AID") != "1")
                return;
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _unattestedSavedAid || _tradeFaulted || _goodsTransfersUnavailable || !NativeAuthorized() || _inventoryRestorePending || _nativeTransferProbe != null || _pendingPackage != null)
                return;
            if (_sessionGeneration < 0)
                return; // no attested loaded world to own the day seal
            if (NewOriginalAidPlanningGeneration(_lastAidPlanningGeneration,
                _sessionGeneration))
            {
                // A later actual map may legitimately start
                // again at day one. A prior map's same-day in-memory seal
                // must NOT suppress all future AI help on the new original
                // generation. Do NOT reset persisted promises, favors or
                // the historical native-provenance quarantine.
                _lastAidPlanningGeneration = _sessionGeneration;
                _lastAidPlanningDay = -1;
                _aidRetryFrame = 0;
                _roundRobinAidDay = -1;
                _roundRobinAidCursor = 0;
            }
            int day = Math.Max(1, _lastProcessedDay);
            if (_featureTicks < _aidRetryFrame)
                return;
            _aidRetryFrame = _featureTicks + 120;
            try
            {
                byte[] humans;
                string rosterReason;
                if (!TryReadHumanSlots(out humans, out rosterReason))
                    return;
                // Only an authenticated local single-human host can mutate
                // the disposable clone; an MP lobby is not an aid host.
                string contract;
                if (!NativeInventoryContract.IsStrictSoloHost(out contract))
                    return;
                if (_aidMemory == null)
                {
                    _aidMemory = new AidLedger(_mapFingerprint, _rosterFingerprint, day);
                }
                else
                {
                    AidPromiseLedgerCodec.ValidateScope(_aidMemory, _mapFingerprint, _rosterFingerprint, day);
                    // A normal COLD-restored history is NOT itself native
                    // provenance. Retry the exclusive the test runner owner's 
                    // original-save-commit receipt verification BEFORE any
                    // day advancement. The owner validates exact current
                    // .sav SHA, complete AidLedger SHA, same generation and
                    // pinned original live Gift/SaveData scope, then sets
                    // 's PRIVATE history bit in its own private partial.
                    // This call's bool alone cannot grant authority.
                    if (_aidMemory.HasUnattestedHistoricalNativeAid)
                    {
                        bool reattested = TryRestoreOriginalAidHistory();
                        if (!reattested || _aidMemory.HasUnattestedHistoricalNativeAid)
                        {
                            HeadlessAidTrace("historical_origin_waiting", "original__sav_or_aid_receipt_unverified" + "|aid_ledger_day=" + _aidMemory.Day + "|current_original_day=" + day + "|native_gift_mutation=false");
                            return; // retry next safe private pump
                        }
                    }

                    if (_aidMemory.Day < day)
                        _aidMemory.AdvanceDay(day);
                }

                // SHA-correct historical Delivered/Favor records are not
                // independently attested native shipments on RESTORE.
                // The normal release already rejects their use; private
                // headless proof must also refuse to recycle their credit.
                // Fresh runtime  native receipt uses a NON-persisted
                // attestation latch in . After cold load the latch is
                // deliberately false until an independent real witness.
                if (_aidMemory.HasUnattestedHistoricalNativeAid)
                {
                    // Ordinary unproven cold history is RETRYABLE, not an
                    // unsafe PREPARED-GIFT journal. Keep the latter's
                    // existing _unattestedSavedAid hard quarantine.
                    HeadlessAidTrace("historical_origin_waiting", "owner_cryptographic_sav_receipt_unverified" + "|native_credit=false|retry_next_private_pump=true");
                    return;
                }

                // Existing, persisted pledges settle on a later game day.
                // Their native stock may have changed; no assumed shipment.
                AidPledge[] pending = _aidMemory.Promises.Where(x => x.Status == AidPledgeStatus.Pending && x.IssuedDay < day && x.DueDay >= day).OrderBy(x => x.IssuedDay).ThenBy(x => x.Id).ToArray();
                if (pending.Length != 0)
                {
                    HeadlessAidTrace("due_original_pending", "game_day=" + day + "|ids=" + string.Join(",", pending.Select(x => x.Id.ToString())) + "|original_gift_attempts=not_yet_started");
                    // Do not starve a younger, independently funded pledge
                    // when an old gift repeatedly fails the REAL native
                    // transfer. Rotate on each original pump, not just after
                    // a successful gift. Attempt at most ONE physical native
                    // transfer (even if it fails or rolls back).
                    if (_roundRobinAidDay != day)
                    {
                        _roundRobinAidDay = day;
                        _roundRobinAidCursor = 0;
                    }

                    int start = (int)(_roundRobinAidCursor % (ulong)pending.Length);
                    _roundRobinAidCursor++;
                    for (int offset = 0; offset < pending.Length; offset++)
                    {
                        var pledge = pending[(start + offset) % pending.Length];
                        if (AttemptRealAidDelivery(pledge, day, humans))
                            break;
                    }

                    return;
                }

                // Derive the same-day planning seal from the saved ledger,
                // so restarting the process cannot plan another batch today.
                if (_lastAidPlanningDay >= day || _aidMemory.Promises.Any(x => x.IssuedDay == day))
                    return;
                // Economic capacity is attested per goods cell by the same
                // source used for native barter, never a synthetic ManagedGoodsSnapshotPort
                // allowance of warehouse space. No native writes here.
                DiplomaticTradeNegotiator detached;
                ManagedGoodsSnapshotPort stocks;
                if (!ReadNativeWorld(out detached, out stocks))
                    return;
                var people = new List<ObservedAidParticipant>();
                var agreements = new List<ObservedMutualAidAgreement>();
                var observed = new List<ObservedAidCommodity>();
                // PRIVATE opt-in diagnostic input only. Capture the
                // provenance of the already read managed archive relation
                // next to the exact live pact used by the real planner.
                // No second original/native query is made for logging.
                var capturedPactDiagnostics = PrivateHeadlessAidProbe ? new List<Tuple<ObservedMutualAidAgreement, bool>>() : null;
                var active = _roster.Where(IsAlivePlayer).Distinct().OrderBy(x => x).ToArray();
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                    return;
                using (lease)
                {
                    var api = GamePlayerManagerAPI.Instance;
                    if (api == null)
                        return;
                    foreach (int player in active)
                    {
                        bool human = humans.Contains((byte)player);
                        people.Add(new ObservedAidParticipant { PlayerId = (byte)player, IsHuman = human, // Last-stand raw pointer is not attested for this
 // engine. Never invent an emergency flag.
                        VerifiedLastStand = false });
                        foreach (var good in GoodCatalog.All)
                        {
                            var cell = stocks.Read((byte)player, good.Id);
                            if (cell == null)
                                return;
                            int pendingPurchases = human ? 0 : ReadCommittedAiDemand(player, good.Id);
                            int receivingSpace = 0;
                            if (!human && TryReadConservativeIncomingReservation(player, good.Id,
                                    out long incoming, out _))
                                receivingSpace = (int)Math.Max(0L, cell.Space - incoming);
                            observed.Add(new ObservedAidCommodity
                            {
                                Owner = (byte)player,
                                Good = good.Id,
                                ObservationDay = day,
                                Snapshot = cell,
                                PendingPurchases = pendingPurchases,
                                ExactCapacityAttested = true,
                                UnreservedReceivingSpace = receivingSpace
                            });
                        }
                    }

                    for (int i = 0; i < active.Length; i++)
                        for (int j = i + 1; j < active.Length; j++)
                        {
                            int a = active[i], b = active[j];
                            if (humans.Contains((byte)a) || humans.Contains((byte)b))
                                continue; // never move a human player's stock
                            if (!api.IsAIPlayer(a) || !api.IsAIPlayer(b))
                                continue; // original API and register must agree
                            if (!OriginalNativeAiPair(api, a, b))
                                continue;
                            string reason;
                            if (!CanTradePair(a, b, out reason))
                                continue;
                            bool recordedTradeGoodwillUsed;
                            int goodwill;
                            if (!AllianceAidAffinity.TryResolve(_archive, _mailbox,
                                (byte)a, (byte)b, true, out goodwill,
                                out recordedTradeGoodwillUsed))
                                continue;
                            var capturedPact = _aidMemory.AidTerms((byte)a, (byte)b, goodwill, true);
                            agreements.Add(capturedPact);
                            if (capturedPactDiagnostics != null)
                            {
                                try
                                {
                                    capturedPactDiagnostics.Add(Tuple.Create(capturedPact, recordedTradeGoodwillUsed));
                                }
                                catch (Exception)
                                {
                                    // Optional capture failure cannot remove an admitted pact.
                                    capturedPactDiagnostics = null;
                                }
                            }
                        }
                }

                // Headless scenario evidence is derived solely from the
                // real original InventoryPort+AI pending purchases read
                // under the existing monitor. If the ordinary skirmish
                // produces no legitimate AI shortage, log that fact and
                // WAIT; never manufacture a donor stock, alliance or Gift.
                if (PrivateHeadlessAidProbe)
                {
                    try
                    {
                        // One optional row per already admitted mutual AI pact.
                        // Only the captured in-memory AidTerms/StockCells are read,
                        // after releasing the existing original inventory lease.
                        if (capturedPactDiagnostics != null)
                        {
                            foreach (var captured in capturedPactDiagnostics)
                            {
                                string note;
                                if (ObservedAidEligibilityDiagnostic.TryDescribe(day, captured.Item1, captured.Item2, observed, out note))
                                    HeadlessAidTrace("captured_planner_eligibility", note);
                            }
                        }

                        var airoster = people.Where(x => !x.IsHuman).Select(x => x.PlayerId).OrderBy(x => x).ToArray();
                        HeadlessAidTrace("original_ai_snapshot", "game_day=" + day + "|ai_slots=" + string.Join(",", airoster) + "|allied_original_pairs=" + agreements.Count + "|original_stock_cells=" + observed.Count + "|human_slots=" + string.Join(",", humans));
                        foreach (var pact in agreements.OrderBy(x => x.A).ThenBy(x => x.B))
                            foreach (var cell in observed.Where(x => (x.Owner == pact.A || x.Owner == pact.B) && x.Good >= 10 && x.Good <= 13).OrderBy(x => x.Owner).ThenBy(x => x.Good))
                            {
                                var original = cell.Snapshot;
                                HeadlessAidTrace("original_ai_food_goods", "day=" + day + "|allyA=" + pact.A + "|allyB=" + pact.B + "|owner=" + cell.Owner + "|good=" + cell.Good + "|native_quantity=" + original.Quantity + "|exact_capacity=" + original.Capacity + "|core_reserve=" + original.CoreReserve + "|observed_pending_purchases=" + cell.PendingPurchases + "|capacity_attested=" + cell.ExactCapacityAttested);
                            }
                    }
                    catch (Exception)
                    {
                    // A malformed optional summary never skips the planner.
                    }
                }

                ProposedAidShipment[] suggestions = MutualAidIntentPlanner.Plan(day, people, agreements, observed, 2);
                HeadlessAidTrace("native_authority_not_attempted_planner_only", "day=" + day + "|actual_eligible_original_aid_intents=" + suggestions.Length);
                foreach (ProposedAidShipment suggestion in suggestions)
                {
                    // Candidate planning uses observed Mod-policy quantities.
                    // Check the stricter live ORIGINAL storage and AI reserves
                    // before creating a durable pending promise at all.
                    IDisposable candidateLease;
                    if (!TryEnterNativeInventorySection(out candidateLease))
                    {
                        HeadlessAidTrace("pledge_original_budget_deferred",
                            "reason=native_engine_lease_busy|promise_created=false");
                        break;
                    }
                    bool fits;
                    string budgetDenial;
                    using (candidateLease)
                        fits = FitsLiveOriginalAidBudget(suggestion.Donor,
                            suggestion.Recipient, suggestion.Good,
                            suggestion.Quantity, out budgetDenial);
                    if (!fits)
                    {
                        HeadlessAidTrace("pledge_original_budget_refused",
                            "donor=" + suggestion.Donor + "|recipient=" +
                            suggestion.Recipient + "|good=" + suggestion.Good +
                            "|amount=" + suggestion.Quantity + "|cause=" +
                            Safe(budgetDenial) +
                            "|promise_created=false|native_write=false");
                        continue;
                    }
                    try
                    {
                        var promise = _aidMemory.Promise(suggestion);
                        try
                        {
                            Log("aid_runtime", "status=pledged_not_delivered|id=" + promise.Id + "|donor=" + promise.Donor + "|recipient=" + promise.Recipient + "|good=" + promise.Good + "|amount=" + promise.Quantity + "|day=" + day);
                        }
                        catch (Exception)
                        {
                        // A logging failure must not discard later suggestions.
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    // Capacity, a concurrent pledge or a bounded ledger
                    // refuses THIS suggestion without claiming delivery.
                    }
                }

                _lastAidPlanningDay = day;
            }
            catch (Exception e)
            {
                Log("aid_runtime", "status=planning_or_delivery_refused|reason=" + Safe(e.GetType().Name));
            }
        }

        // SHCDESE.IsPlayerAlliedTo compares team IDs only, including 0==0.
        // Only an ACTUAL positive original-game team is an eligible alliance.
        // Called exclusively beneath the existing original native monitor.
        private static bool OriginalNativeAiPair(
            GamePlayerManagerAPI api, int first, int second)
        {
            if (api == null || first < 1 || first > 8 || second < 1 ||
                second > 8 || first == second ||
                !api.IsAIPlayer(first) || !api.IsAIPlayer(second))
                return false;
            int a = api.GetPlayerTeam(first);
            int b = api.GetPlayerTeam(second);
            return a > 0 && a == b &&
                api.IsPlayerAlliedTo(first, second) &&
                api.IsPlayerAlliedTo(second, first);
        }

        private static int OriginalAidStorageClass(ushort good)
        {
            switch (good)
            {
                case 2: case 4: case 6: case 7: case 9: case 14: case 16:
                    return 0x0A;
                case 10: case 11: case 12: case 13:
                    return 0x13;
                case 17: case 18: case 19: case 20:
                case 21: case 22: case 23: case 24:
                    return 0x0B;
                default:
                    return -1;
            }
        }

        private static unsafe bool TryReadConservativeIncomingReservation(
            int recipient, ushort good, out long pending, out string denial)
        {
            RequireNativeDiplomacySection();
            pending = 0;
            denial = "aid_incoming_original_storage_class_unavailable";
            RequireNativeDiplomacySection();
            int storageClass = OriginalAidStorageClass(good);
            if (storageClass < 0 || recipient < 1 || recipient > 8)
                return false;

            var players = GamePlayerManagerAPI.Instance;
            if (players == null || !players.IsPlayerIdValid(recipient))
            {
                denial = "aid_incoming_original_player_unavailable";
                return false;
            }
            GamePlayerResources* r;
            if (!players.TryGetPlayerResourcesById(recipient, out r) || r == null)
            {
                denial = "aid_incoming_original_resources_unavailable";
                return false;
            }

            if (storageClass == 0x0A)
            {
                long target;
                switch (good)
                {
                    case 2: target = r->r_incomingWoodPlanks; break;
                    case 4: target = r->r_incomingStoneBlocks; break;
                    case 6: target = r->r_incomingIronIngots; break;
                    case 7: target = r->r_incomingPitchRaw; break;
                    case 9: target = r->r_incomingRawWheat; break;
                    case 14: target = r->r_incomingFoodAle; break;
                    case 16: target = r->r_incomingFlour; break;
                    default: return false;
                }
                // Preserve all original stockpile incoming fields, including
                // goods outside the aid catalog. Unknown fragmentation stays
                // an explicit denial, never becomes an invented free space.
                long stockpileIncoming = (long)r->r_incomingWoodLogs
                    + r->r_incomingWoodPlanks
                    + r->r_incomingRawHops
                    + r->r_incomingStoneBlocks
                    + r->r_incomingCowHides
                    + r->r_incomingIronIngots
                    + r->r_incomingPitchRaw
                    + r->r_incomingPitchRefined
                    + r->r_incomingRawWheat
                    + r->r_incomingFoodAle
                    + r->r_incomingFlour;
                if (stockpileIncoming != target)
                {
                    denial = "aid_incoming_stockpile_other_goods_ambiguous";
                    return false;
                }
                pending = target;
                denial = null;
                return true;
            }

            if (storageClass == 0x13)
            {
                pending = (long)r->r_incomingFoodBread
                    + r->r_incomingFoodCheese
                    + r->r_incomingFoodMeat
                    + r->r_incomingFoodFruit;
                denial = null;
                return true;
            }

            if (storageClass == 0x0B)
            {
                pending = (long)r->r_incomingBows
                    + r->r_incomingCrossbows
                    + r->r_incomingSpears
                    + r->r_incomingPikes
                    + r->r_incomingMaces
                    + r->r_incomingSwords
                    + r->r_incomingLeatherArmour
                    + r->r_incomingMetalArmour;
                denial = null;
                return true;
            }
            return false;
        }

        private bool FitsLiveOriginalAidBudget(int sender, int recipient,
            ushort good, int amount, out string denial)
        {
            RequireNativeDiplomacySection();
            denial = "aid_budget_original_source_unavailable";
            if (sender < 1 || sender > 8 || recipient < 1 ||
                recipient > 8 || sender == recipient ||
                GoodCatalog.Find(good) == null || _capacityAuthority == null)
                return false;
            try
            {
                int donor = GoodAmount(sender, good);
                int receiver = GoodAmount(recipient, good);
                if (donor < 0 || receiver < 0)
                {
                    denial = "aid_budget_original_stock_unreadable";
                    return false;
                }
                int maximum;
                if (!_capacityAuthority.TryReadWarehouseCapacity(recipient,
                        good, out maximum))
                {
                    denial = "aid_recipient_native_capacity_unattested";
                    return false;
                }
                long incoming;
                if (!TryReadConservativeIncomingReservation(recipient, good,
                        out incoming, out denial))
                    return false;
                int reserve = ReserveWithPlannedPurchases(sender, good,
                    donor, ReadLordProfile(sender));
                return MutualAidIntentPlanner.FitsOriginalAidStockAndSpace(
                    donor, reserve, receiver, maximum, incoming,
                    amount, out denial);
            }
            catch (Exception exception)
            {
                // A failing original AI/warehouse read is NOT zero reserve
                // or permission to ship. Keep the native gift journal inert.
                denial = "aid_budget_original_read_failed_" +
                    exception.GetType().Name;
                return false;
            }
        }

        private bool CanTransferAiAid(int sender, int recipient, int good,
            int amount, int day, out string denial)
        {
            RequireNativeDiplomacySection();
            denial = "aid_owner_day_or_active_journal_not_ready";
            if (day != Math.Max(1, _lastProcessedDay) ||
                sender == _localPlayer || recipient == _localPlayer ||
                sender < 1 || sender > 8 || recipient < 1 || recipient > 8 ||
                sender == recipient || amount < 3 || amount > 40 ||
                good < 1 || good > 24 || _aidMemory == null ||
                _aidMemory.HasUnattestedHistoricalNativeAid ||
                _aidMemory.Day != day || !IsAlivePlayer(sender) ||
                !IsAlivePlayer(recipient))
                return false;
            byte[] humans;
            string reason;
            if (!TryReadHumanSlots(out humans, out reason))
            {
                denial = "aid_original_human_roster_unverified";
                return false;
            }
            if (humans.Contains((byte)sender) ||
                humans.Contains((byte)recipient))
            {
                denial = "aid_original_human_inventory_excluded";
                return false;
            }
            if (!CanTradePair(sender, recipient, out reason))
            {
                denial = "aid_original_trade_pair_ineligible";
                return false;
            }
            var api = GamePlayerManagerAPI.Instance;
            if (!OriginalNativeAiPair(api, sender, recipient))
            {
                denial = "aid_original_ai_alliance_not_attested";
                return false;
            }
            var promises = _aidMemory.Promises.Where(x =>
                x.Status == AidPledgeStatus.Pending && x.Donor == sender &&
                x.Recipient == recipient && x.Good == good &&
                x.Quantity == amount && x.IssuedDay < day &&
                x.DueDay >= day).ToArray();
            if (promises.Length != 1)
            {
                denial = "aid_exact_pending_promise_missing_or_ambiguous";
                return false;
            }
            string contract;
            if (!TryAuthorizeNativeInventory(out contract))
            {
                denial = "aid_original_native_authority_unattested";
                return false;
            }
            // Fresh native stock/capacity/incoming/reserve, not the earlier
            // promise's saved fields or the planner's estimated free space.
            return FitsLiveOriginalAidBudget(sender, recipient,
                (ushort)good, amount, out denial);
        }

        private bool AttemptRealAidDelivery(AidPledge promise, int day, byte[] humans)
        {
            if (promise == null || humans.Contains(promise.Donor) || humans.Contains(promise.Recipient))
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
            {
                HeadlessAidTrace("original_native_lease_busy", "id=" + promise.Id + "|day=" + day + "|pledge_status=Pending");
                return true;
            }

            using (lease)
            {
                string preflightDenial;
                if (!CanTransferAiAid(promise.Donor, promise.Recipient,
                        promise.Good, promise.Quantity, day,
                        out preflightDenial))
                {
                    HeadlessAidTrace("original_native_aid_preflight_denied",
                        "id=" + promise.Id + "|day=" + day + "|donor=" +
                        promise.Donor + "|recipient=" + promise.Recipient +
                        "|good=" + promise.Good + "|amount=" + promise.Quantity +
                        "|cause=" + Safe(preflightDenial) +
                        "|pledge_status=Pending|attempted_native_goods=false");
                    // This pledge is currently infeasible. Another
                    // independent pledge may still be native-admissible.
                    return false;
                }

                AtomicBoundary.MutationScope mutation;
                if (!BeginNativeMutation(out mutation))
                {
                    HeadlessAidTrace("original_native_mutation_boundary_busy", "id=" + promise.Id + "|day=" + day + "|pledge_status=Pending");
                    return true;
                }

                HeadlessAidTrace("original_native_gift_attempt_begin", "id=" + promise.Id + "|day=" + day + "|donor=" + promise.Donor + "|recipient=" + promise.Recipient + "|good=" + promise.Good + "|amount=" + promise.Quantity + "|native_readback_and_save_still_required=true");
                _activeAidPromise = promise;
                try
                {
                    NoteNativeWrite();
                    string failure;
                    bool transferred = TryAtomicTransferFrom(promise.Donor, promise.Recipient, promise.Good, promise.Quantity, out failure, DiplomacyTransferPurpose.Gift, day);
                    if (!transferred)
                    {
                        Log("aid_runtime", "status=not_delivered|id=" + promise.Id + "|reason=" + Safe(failure));
                        // A verified rollback closes this mutation. An uncertain
                        // journal leaves the atomic boundary quarantined.
                        if (_nativeTransferProbe == null && !_goodsTransfersUnavailable)
                            NoteNativeSettled();
                        return true; // one native attempt per pump, even after rollback
                    }

                    // The native coordinator has read both cells after settlement.
                    // Commit bookkeeping under the same ORIGINAL engine monitor;
                    // it alone may convert Pending -> Delivered and credit favors.
                    var completed = _nativeTransferProbe;
                    if (completed == null)
                        throw new InvalidOperationException("Native aid probe missing after transfer");
                    CompleteVerifiedNativeTransfer(completed, "autonomous_native_aid");
                    NoteNativeSettled();
                    Log("aid_runtime", "status=native_delivered_and_journal_committed" + "|id=" + promise.Id + "|donor=" + promise.Donor + "|recipient=" + promise.Recipient + "|good=" + promise.Good + "|amount=" + promise.Quantity + "|day=" + day + "|cold_save_verification=still_required");
                    return true;
                }
                finally
                {
                    _activeAidPromise = null;
                    // CompleteVerifiedNativeTransfer may have verified an
                    // inverse rollback before rethrowing a bookkeeping error.
                    if (_nativeTransferProbe == null && !_goodsTransfersUnavailable)
                        NoteNativeSettled();
                    EndNativeMutation(mutation);
                }
            }
        }

        private bool IsActiveAidMutation(int donor, int recipient, int good, int quantity, int day)
        {
            RequireNativeDiplomacySection();
            var p = _activeAidPromise;
            return !_unattestedSavedAid && _aidMemory != null && !_aidMemory.HasUnattestedHistoricalNativeAid && p != null && _nativeMutation != null && _atomicBoundary != null && _atomicBoundary.MutationActive && p.Status == AidPledgeStatus.Pending && p.Donor == donor && p.Recipient == recipient && p.Good == good && p.Quantity == quantity && p.IssuedDay < day && p.DueDay >= day && _aidMemory != null && _aidMemory.Promises.Any(x => Object.ReferenceEquals(x, p));
        }

        internal static bool IsPersistableAidJournal(LegacyDiplomacySave state, DiplomacyPendingTransfer t)
        {
            if (state == null || t == null || state.AidMemory == null || t.SenderPlayer == state.LocalPlayerId || t.TargetPlayer == state.LocalPlayerId || t.Purpose != DiplomacyTransferPurpose.Gift || !t.DirectAtomicTransfer || !t.AlliedNativeTransfer || t.GameDay != state.LastProcessedDay || state.AidMemory.Day != t.GameDay || state.AidMemory.Map != state.MapFingerprint || state.AidMemory.Roster != state.RosterFingerprint || !state.Coalitions.Any(c => c.MemberPlayerIds.Contains(t.SenderPlayer)) || !state.Coalitions.Any(c => c.MemberPlayerIds.Contains(t.TargetPlayer)))
                return false;
            int donorTeam, recipientTeam;
            if (!state.TeamPartition.TryGetValue(t.SenderPlayer, out donorTeam) || !state.TeamPartition.TryGetValue(t.TargetPlayer, out recipientTeam) || donorTeam == 0 || donorTeam != recipientTeam)
                return false;
            return state.AidMemory.Promises.Count(p => p.Status == AidPledgeStatus.Pending && p.Donor == t.SenderPlayer && p.Recipient == t.TargetPlayer && p.Good == t.GoodId && p.Quantity == t.Amount && p.IssuedDay < t.GameDay && p.DueDay >= t.GameDay) == 1;
        }

        private void CommitNativeAidBookkeeping(NativeTransferProbe probe)
        {
            RequireNativeDiplomacySection();
            if (probe == null || probe.RollbackPending || probe.Purpose != DiplomacyTransferPurpose.Gift || ProbeSender(probe) == _localPlayer || _aidMemory == null || _aidMemory.HasUnattestedHistoricalNativeAid || _aidMemory.Day != probe.GameDay)
                throw new InvalidOperationException("Invalid or historically unverified native aid journal");
            if (probe.Progress == null || !probe.Progress.SenderObserved || !probe.Progress.RecipientObserved || !probe.Progress.SenderTouched || !probe.Progress.RecipientTouched)
                throw new InvalidOperationException("Incomplete two-cell native aid");
            int donor = ProbeSender(probe), recipient = probe.TargetPlayer;
            if (!CanTransferAidReceipt(probe, donor, recipient))
                throw new InvalidOperationException("Native aid readback or relationship changed");
            _aidMemory.RecordObservedNativeDelivery((byte)donor, (byte)recipient, (ushort)probe.GoodId, probe.Amount, probe.GameDay);
        }

        private bool CanTransferAidReceipt(NativeTransferProbe probe, int donor, int recipient)
        {
            byte[] humans;
            string reason;
            if (!TryReadHumanSlots(out humans, out reason) || humans.Contains((byte)donor) || humans.Contains((byte)recipient) || !CanTradePair(donor, recipient, out reason))
                return false;
            var original = GamePlayerManagerAPI.Instance;
            if (!OriginalNativeAiPair(original, donor, recipient))
                return false;
            return GoodAmount(donor, probe.GoodId) == probe.SenderBefore - probe.Amount && GoodAmount(recipient, probe.GoodId) == probe.RecipientBefore + probe.Amount;
        }
    }
}
#endif
