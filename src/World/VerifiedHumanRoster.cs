//  isolated source candidate: ORIGINAL managed multiplayer observation.
// No Harmony hook, SaveData registration, native setter, quote allocator or
// parallel canonical controller. Only the coordinator attaches these methods.
// In particular the previously proven  capacity is SOLO-ONLY; this path
// refuses MP goods snapshots until a separate exact ORIGINAL capacity port
// is actually verified. DO NOT change NativeAuthorized to admit MP.
using System;
using System.Collections.Generic;
using System.Linq;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class VerifiedOriginalHumanRoster
    {
        internal readonly long OriginalMapGeneration, OriginalSharedTick;
        internal readonly int Day;
        internal readonly string GlobalMapFingerprint, GlobalRosterFingerprint;
        internal readonly ulong LocalSteamId, HostSteamId;
        private readonly byte[] _scope;
        private readonly CanonicalPlayer[] _players;
        internal VerifiedOriginalHumanRoster(TrustedContext context, long mapGeneration, long tick, int day, IEnumerable<CanonicalPlayer> observedPlayers)
        {
            if (context == null || mapGeneration < 1 || tick < 1 || day < 1 || observedPlayers == null)
                throw new ArgumentException("Missing original MP provenance");
            var scope = context.Scope.Copy();
            if (scope.Length != 128)
                throw new ArgumentException("Wrong pinned host scope");
            var actors = observedPlayers.ToArray();
            if (actors.Length < 2 || actors.Length > 8 || actors.Any(x => x == null))
                throw new ArgumentException("Null or invalid MP actor roster");
            Array.Sort(actors, (first, second) => first.Slot.CompareTo(second.Slot));
            if (actors.Select(x => x.Slot).Distinct().Count() != actors.Length || actors.Count(x => x.Human) < 2 || actors.Where(x => x.Human).Any(x => x.SteamId <= 1000) || actors.Where(x => !x.Human).Any(x => x.SteamId != 0) || !actors.Where(x => x.Human).Select(x => x.SteamId).OrderBy(x => x).SequenceEqual(context.HumanIds()))
                throw new ArgumentException("Unproven complete human roster");
            _scope = (byte[])scope.Clone();
            _players = actors;
            OriginalMapGeneration = mapGeneration;
            OriginalSharedTick = tick;
            Day = day;
            GlobalMapFingerprint = Hex(scope, 0, 32);
            GlobalRosterFingerprint = Hex(scope, 32, 32);
            LocalSteamId = context.LocalSteamId;
            HostSteamId = context.HostSteamId;
        }

        private static string Hex(byte[] value, int offset, int length)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(value, offset, slice, 0, length);
            return BitConverter.ToString(slice).Replace("-", "").ToLowerInvariant();
        }

        internal byte[] Scope()
        {
            return (byte[])_scope.Clone();
        }

        internal CanonicalPlayer[] Players()
        {
            return (CanonicalPlayer[])_players.Clone();
        }

        internal bool IsOriginalHuman(byte slot, ulong authenticatedSteamId)
        {
            return authenticatedSteamId > 1000 && _players.Any(x => x.Slot == slot && x.Human && x.Alive && x.SteamId == authenticatedSteamId);
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        // Phase 1: usable for NO-GOODS host/peer actor authentication while
        // original MP capacity and globally authoritative trade relations
        // are not yet proven. The actor IDs derive from GameNetworkAPI's
        // ORIGINAL Steam roster, never from a client-supplied action.
        private bool TryReadVerifiedMultiHumanRoster(long requestedTick, out VerifiedOriginalHumanRoster verified, out string reason)
        {
            verified = null;
            reason = "original_mp_session_not_attested";
            if (!IsNativeDiplomacySectionHeld)
            {
                reason = "original_engine_monitor_not_owned";
                return false;
            }

            // The existing solo diplomacy bootstrap intentionally rejects
            // other actual humans (CaptureRosterAndCoalitions). This original
            // MP observation must still work while solo diplomacy is DISABLED,
            // without resurrecting its gifts, teams or native goods hooks.
            if (!_signedBridgeInstalled || _signed == null || _trustedKey == null || _mapEpoch < 1 || requestedTick < 1)
            {
                reason = "mp_world_or_ordered_tick_not_ready";
                return false;
            }

            // The official ORIGINAL map clock comes from rawra's
            // GameTimeManagerAPI.GetElapsedMapTicks / CurrentGameTickVA;
            // ReadSimulationTick is only a process-local callback counter.
            // At frame CAPTURE time the observed tick must match exactly.
            long observedOriginalTick;
            string originalClockReason;
            if (!TryReadOriginalMapTick(out observedOriginalTick, out originalClockReason) || observedOriginalTick != requestedTick)
            {
                reason = "original_map_tick_not_at_requested_capture:" + originalClockReason;
                return false;
            }

            try
            {
                TrustedContext context;
                string key, why;
                if (!ReadTrustedNetworkContext(out context, out key, out why))
                {
                    reason = "original_network_context:" + why;
                    return false;
                }

                if (!_signed.Live || !_signed.Ready || _signed.Context.Generation != context.Generation || !_signed.Context.Scope.Same(context.Scope) || _signed.Context.HostSteamId != context.HostSteamId || _signed.Context.LocalSteamId != context.LocalSteamId || !string.Equals(_trustedKey, key, StringComparison.Ordinal))
                {
                    reason = "signed_session_or_map_generation_changed";
                    return false;
                }

                var api = GamePlayerManagerAPI.Instance;
                var data = GameData.Instance;
                var native = data == null ? null : data.lastGameState;
                if (api == null || native == null || _boundSteamParticipants == null || _boundSteamParticipants.Length != 9)
                {
                    reason = "original_player_register_unavailable";
                    return false;
                }

                int local = api.GetLocalPlayerId();
                // Multiplayer trade-only can observe independently of
                // solo CaptureRosterAndCoalitions, which intentionally
                // refuses real remote humans. Never rely on _localPlayer
                // being initialized by that rejected solo bootstrap.
                if (local < 1 || local > 8 || local != data.playerID || local != GameNetworkAPI.GetLocalPlayerId() || _boundSteamParticipants[local] != context.LocalSteamId)
                {
                    reason = "original_local_slot_changed";
                    return false;
                }

                int originalDay = ReadOriginalNativeDay();
                var alive = api.GetAlivePlayerIds();
                if (originalDay < 1 || alive == null || alive.Length < 1)
                {
                    reason = "original_game_day_or_alive_slots_unavailable";
                    return false;
                }

                var aliveIds = new HashSet<int>(alive);
                var slots = new List<CanonicalPlayer>();
                var actual = new HashSet<int>();
                for (int id = 1; id <= 8; id++)
                {
                    bool human = native.is_valid_player(id);
                    bool ai = native.is_skirmish_player(id);
                    if (human && ai)
                    {
                        reason = "conflicting_native_role:" + id;
                        return false;
                    }

                    if (!human && !ai)
                    {
                        if (_boundSteamParticipants[id] != 0)
                        {
                            reason = "unregistered_original_slot:" + id;
                            return false;
                        }

                        continue;
                    }

                    if (!actual.Add(id) || api.IsAIPlayer(id) != ai)
                    {
                        reason = "native_player_roster_mismatch:" + id;
                        return false;
                    }

                    ulong steam = _boundSteamParticipants[id];
                    if (human)
                    {
                        if (steam <= 1000 || !context.Contains(steam))
                        {
                            reason = "missing_authenticated_human:" + id;
                            return false;
                        }
                    }
                    else if (steam != 0)
                    {
                        reason = "ai_slot_claims_human_steam:" + id;
                        return false;
                    }

                    slots.Add(new CanonicalPlayer((byte)id, human, aliveIds.Contains(id), human ? steam : 0UL));
                }

                if (slots.Count != actual.Count || aliveIds.Any(x => !actual.Contains(x)) || !aliveIds.Contains(local) || slots.Count(x => x.Human) < 2 || slots.Count(x => x.Human && x.Slot == local) != 1)
                {
                    reason = "incomplete_original_multihuman_roster";
                    return false;
                }

                // Network Scope[0..31] comes from the SAME original full
                // map file+native start-layout SHA on every peer. [32..63]
                // binds all actual Steam occupants, including host slot.
                // Never reuse _rosterFingerprint here: its local-player
                // encoding differs across connected humans.
                long finalOriginalTick;
                if (!TryReadOriginalMapTick(out finalOriginalTick, out originalClockReason) || finalOriginalTick != requestedTick || ReadOriginalNativeDay() != originalDay)
                {
                    reason = "original_native_clock_or_day_advanced_during_roster";
                    return false;
                }

                verified = new VerifiedOriginalHumanRoster(context, _mapEpoch, finalOriginalTick, originalDay, slots);
                reason = "original_multihuman_roster_attested_without_goods";
                return true;
            }
            catch (Exception e)
            {
                reason = "verified_mp_roster_refused:" + e.GetType().Name;
                return false;
            }
        }

        // An actual reciprocal native ALLIANCE is sufficient evidence
        // for trading eligibility. A non-allied human-to-human TradePact
        // requires a separately signed canonical pact, not local guesses.
        // Neutral numbers here are deterministic POLICY INITIAL VALUES,
        // not claims of measured trust, goodwill, war or threat.
        private static ObservedTradePairState OriginalNativeAlliancePair(CanonicalPlayer from, CanonicalPlayer to, int day)
        {
            return new ObservedTradePairState
            {
                Requester = from.Slot,
                Recipient = to.Slot,
                Day = day,
                RequesterAlive = from.Alive,
                RecipientAlive = to.Alive,
                Authoritative = true,
                HasTradeRelation = true,
                Hostile = false,
                Goodwill = 50,
                Trust = 50,
                Grudge = 0,
                Threat = 0
            };
        }

        // No per-client _capturedLordIds or local-only diplomacy mood:
        // the original skirmish AI index is already incorporated into
        // the pinned multiplayer roster hash by ReadTrustedNetworkContext.
        // The economic defaults are rules, not invented production evidence.
        private Ruler OriginalGlobalRuler(CanonicalPlayer player)
        {
            RequireNativeDiplomacySection();
            int greed = 40, caution = 50, commerce = 50;
            if (!player.Human)
            {
                var state = GameData.Instance.lastGameState;
                int id = state.computer_register[player.Slot];
                if (id < 0 || id >= ScriptExtenderNativeBridge.AicCount())
                    throw new InvalidOperationException("Unproven multiplayer AI lord");
                var aic = ScriptExtenderNativeBridge.Aic(id);
                greed = Math.Min(100, Math.Max(0, 10 + aic.send_goods_margin * 3 / 4));
                caution = Math.Min(100, Math.Max(0, 50 + aic.chance_of_defensive1 / 5));
                commerce = Math.Min(100, Math.Max(0, 35 + aic.trade_wood_at / 5 + aic.trade_stone_at / 5));
            }

            return new Ruler
            {
                PlayerId = player.Slot,
                Name = "OriginalSlot" + player.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Trust = 50,
                Greed = greed,
                Caution = caution,
                Commerce = commerce,
                AtWar = player.Alive && ActualHostileExposure(player.Slot)
            };
        }

        private static ObservedTradePairState UnknownDirectedPair(CanonicalPlayer from, CanonicalPlayer to, int day)
        {
            // Unknown is NOT an inferred trade pact, vassalage, combat
            // hostility observation or consent. Hard-deny this direction.
            return new ObservedTradePairState
            {
                Requester = from.Slot,
                Recipient = to.Slot,
                Day = day,
                RequesterAlive = from.Alive,
                RecipientAlive = to.Alive,
                Authoritative = false,
                HasTradeRelation = false,
                Hostile = true
            };
        }

        // Phase 2: a READ-ONLY complete world for the already-owned
        // CanonicalTradeController. This may correctly return false
        // indefinitely in the current build;  has proven only SOLO
        // warehouse capacity and MUST NEVER become an MP fallback.
        private bool TryCaptureVerifiedMultiHumanWorld(long tick, out CanonicalTradeSnapshot snapshot, out string reason)
        {
            snapshot = null;
            VerifiedOriginalHumanRoster witness;
            if (!TryReadVerifiedMultiHumanRoster(tick, out witness, out reason))
                return false;
            try
            {
                RequireNativeDiplomacySection();
                var api = GamePlayerManagerAPI.Instance;
                var players = witness.Players();
                var prices = new List<Good>();
                var profiles = new List<Ruler>();
                var stocks = new ManagedGoodsSnapshotPort();
                var producers = ObserveRealActiveGoodsProducers();
                foreach (var good in GoodCatalog.All)
                {
                    if (!api.IsTradeGoodAllowed((eGoods)good.Id))
                    {
                        reason = "unsupported_original_good:" + good.Id;
                        return false;
                    }

                    var original = api.GetTradeBasePrice((eGoods)good.Id);
                    prices.Add(ObservedTradeMarketSnapshot.FromOriginalPacked(good.Id, good.Name, original.BuyPrice, original.SellPrice));
                }

                foreach (var player in players)
                {
                    var ruler = OriginalGlobalRuler(player);
                    profiles.Add(ruler);
                    foreach (var good in GoodCatalog.All)
                    {
                        int count = GoodAmount(player.Slot, good.Id);
                        int activeProducers;
                        producers.TryGetValue(OriginalInventoryEvidence.ProducerKey(player.Slot, good.Id), out activeProducers);
                        StockCell observed;
                        if (!OriginalInventoryEvidence.TryNativeBoundStock(player.Slot, good.Id, count, BoundedReserve(good.Id, count, ruler), activeProducers, _capacityAuthority, out observed))
                        {
                            reason = "original_mp_exact_good_capacity_unproven:" + player.Slot + ":" + good.Id;
                            return false;
                        }

                        // No guessed future production, positive stock or
                        // available warehouse space. Pending AI purchases
                        // add to reserves only when observed by original API.
                        observed.DailyProduction = 0;
                        AttachAiDemandToSnapshot(player.Slot, good.Id, observed);
                        stocks.Set(player.Slot, good.Id, observed);
                    }
                }

                var relations = new List<CanonicalPairObservation>();
                for (int i = 0; i < players.Length; i++)
                    for (int j = i + 1; j < players.Length; j++)
                    {
                        var left = players[i];
                        var right = players[j];
                        bool lr = api.IsPlayerAlliedTo(left.Slot, right.Slot);
                        bool rl = api.IsPlayerAlliedTo(right.Slot, left.Slot);
                        // Old ReadTradePair is LOCAL-COALITION scoped.
                        // It refuses once multiplayer's human-human bootstrap
                        // remains intentionally disabled, and it can report
                        // different trust on two real human peers. Only a
                        // CURRENT RECIPROCAL ORIGINAL native alliance is admitted
                        // as a common cross-peer trade relation. Explicit
                        // non-allied TradePacts need an authenticated saved MP
                        // treaty owner and stay unavailable in this proposal.
                        bool allied = lr && rl && left.Alive && right.Alive;
                        ObservedTradePairState forward = allied ? OriginalNativeAlliancePair(left, right, witness.Day) : UnknownDirectedPair(left, right, witness.Day);
                        ObservedTradePairState reverse = allied ? OriginalNativeAlliancePair(right, left, witness.Day) : UnknownDirectedPair(right, left, witness.Day);
                        bool pact = false;
                        relations.Add(new CanonicalPairObservation(forward, lr, rl, pact));
                        relations.Add(new CanonicalPairObservation(reverse, rl, lr, pact));
                    }

                // No local-user-dependent roster token: map+roster hashes
                // are the cross-peer SAME bits from the pinned signed
                // TrustedContext, not guessed nicknames or cached UI data.
                long finalOriginalTick;
                string clockReason;
                if (!TryReadOriginalMapTick(out finalOriginalTick, out clockReason) || finalOriginalTick != witness.OriginalSharedTick || ReadOriginalNativeDay() != witness.Day)
                {
                    reason = "original_native_clock_advanced_during_full_snapshot";
                    return false;
                }

                snapshot = new CanonicalTradeSnapshot(finalOriginalTick, witness.Day, witness.GlobalMapFingerprint, witness.GlobalRosterFingerprint, players, stocks, prices, profiles, relations, witness.OriginalMapGeneration, 0);
                reason = "complete_readonly_mp_snapshot_no_native_release";
                return true;
            }
            catch (Exception e)
            {
                snapshot = null;
                reason = "original_mp_snapshot_refused:" + e.GetType().Name;
                return false;
            }
        }
    }
}
