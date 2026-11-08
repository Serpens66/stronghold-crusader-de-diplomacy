// . ORIGINAL, read-only, multi-human trade-only observation.
// No new MP engine/Steam/SaveData hooks and NO native writes. This must NOT
// call the single-human trade admission or unlock _mapActive/_mapDisabled.
// The EXISTING  signed Steam and original monitor own provenance.
// Consent-only projections remain useful while exact MP warehouse capacity
// is genuinely UNKNOWN. Do not cast solo  into multiplayer authority.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal enum OriginalCapacityAttestationState : byte
    {
        Unknown = 0,
        ExactOriginalMultiplayer = 1
    }

    internal sealed class ObservedOriginalCommodity
    {
        internal readonly byte Player;
        internal readonly ushort GoodId;
        internal readonly int NativeQuantity, ConservativeReserve, OriginalAiPendingPurchases;
        internal readonly OriginalCapacityAttestationState CapacityState;
        private readonly int? _exactCapacity;
        internal ObservedOriginalCommodity(byte player, ushort good, int nativeQuantity, int reserve, int pending, int? exactMpCapacity)
        {
            if (player < 1 || player > 8 || GoodCatalog.Find(good) == null || nativeQuantity < 0 || nativeQuantity > int.MaxValue - 200000 || reserve < 0 || reserve > nativeQuantity || pending < 0 || pending > 1000000 || (exactMpCapacity.HasValue && (exactMpCapacity.Value < nativeQuantity || exactMpCapacity.Value > int.MaxValue - 200000)))
                throw new InvalidDataException("Bad observed original MP goods");
            Player = player;
            GoodId = good;
            NativeQuantity = nativeQuantity;
            ConservativeReserve = reserve;
            OriginalAiPendingPurchases = pending;
            _exactCapacity = exactMpCapacity;
            CapacityState = exactMpCapacity.HasValue ? OriginalCapacityAttestationState.ExactOriginalMultiplayer : OriginalCapacityAttestationState.Unknown;
        }

        // Null means UNKNOWN. Zero is an independently attested ACTUAL zero
        // only; never impute a capacity from stock count or stockpile sprites.
        internal int? ExactCapacity
        {
            get
            {
                return _exactCapacity;
            }
        }
    }

    // The selected  reducer's consent-only path can accept THIS world
    // read-side capability, not a forged CanonicalTradeSnapshot with
    // Capacity=Quantity or Capacity=0. A separate coordinator-owned reducer
    // overload is required to consume it. A human can propose/decline/withdraw
    // while the Native settlement side remains unavailable.
    internal interface IOriginalConsentWorldSnapshot
    {
        long NativeSharedTick { get; }

        int NativeGameDay { get; }

        long LocalOriginalMapGeneration { get; }

        string SharedMapFingerprint { get; }

        string SharedRosterFingerprint { get; }

        bool NativePhysicalSettlementAuthorized { get; }

        ObservedOriginalCommodity[] NativeGoodsWithExplicitCapacityStatus();
        bool TryAuthoritativeHumanActor(byte slot, ulong observedSteamId);
        bool TryConsentPair(byte actor, byte recipient, out string refusal);
    }

    internal sealed class OriginalMultiplayerConsentFrame : IOriginalConsentWorldSnapshot
    {
        internal readonly VerifiedOriginalHumanRoster VerifiedRoster;
        internal readonly PersistentMapIdentity FullOriginalMapIdentity;
        private readonly CanonicalPlayer[] _players;
        private readonly Good[] _market;
        private readonly Ruler[] _rulers;
        private readonly ObservedOriginalCommodity[] _goods;
        private readonly bool[, ] _originalAlliance;
        internal readonly Bytes OriginalPinnedSignedSessionId;
        internal readonly ulong OriginalHostSteamId, OriginalLocalSteamId;
        private readonly byte[] _trusted128;
        internal readonly byte LocalOriginalHumanSlot;
        internal readonly long NativeSharedTick, LocalOriginalMapGeneration;
        internal readonly int NativeGameDay;
        internal readonly string SharedMapFingerprint, SharedRosterFingerprint;
        // The coordinator must keep this type read-only. In particular even
        // knowing all native goods quantities does not grant storage capacity.
        public bool NativePhysicalSettlementAuthorized
        {
            get
            {
                return false;
            }
        }

        long IOriginalConsentWorldSnapshot.NativeSharedTick
        {
            get
            {
                return NativeSharedTick;
            }
        }

        int IOriginalConsentWorldSnapshot.NativeGameDay
        {
            get
            {
                return NativeGameDay;
            }
        }

        long IOriginalConsentWorldSnapshot.LocalOriginalMapGeneration
        {
            get
            {
                return LocalOriginalMapGeneration;
            }
        }

        string IOriginalConsentWorldSnapshot.SharedMapFingerprint
        {
            get
            {
                return SharedMapFingerprint;
            }
        }

        string IOriginalConsentWorldSnapshot.SharedRosterFingerprint
        {
            get
            {
                return SharedRosterFingerprint;
            }
        }

        internal OriginalMultiplayerConsentFrame(VerifiedOriginalHumanRoster originalRoster, PersistentMapIdentity verifiedFullMap, Bytes originalSignedSessionId, byte nativeLocalHumanSlot, IEnumerable<Good> originalPrices, IEnumerable<Ruler> originalProfiles, IEnumerable<ObservedOriginalCommodity> originalGoods, bool[, ] observedDirectedNativeAlliances)
        {
            if (originalRoster == null || verifiedFullMap == null || originalSignedSessionId == null || originalSignedSessionId.Length != 32 || originalPrices == null || originalProfiles == null || originalGoods == null || observedDirectedNativeAlliances == null || observedDirectedNativeAlliances.GetLength(0) != 9 || observedDirectedNativeAlliances.GetLength(1) != 9 || nativeLocalHumanSlot < 1 || nativeLocalHumanSlot > 8)
                throw new ArgumentException("Original MP trade-only provenance missing");
            VerifiedRoster = originalRoster;
            FullOriginalMapIdentity = verifiedFullMap.Copy();
            OriginalPinnedSignedSessionId = new Bytes(originalSignedSessionId.Copy());
            _trusted128 = originalRoster.Scope();
            if (_trusted128.Length != 128 || !SameBytes(FullOriginalMapIdentity.ScopeHash(), _trusted128, 0, 32))
                throw new InvalidDataException("Original full map and signed context differ");
            NativeSharedTick = originalRoster.OriginalSharedTick;
            NativeGameDay = originalRoster.Day;
            LocalOriginalMapGeneration = originalRoster.OriginalMapGeneration;
            SharedMapFingerprint = originalRoster.GlobalMapFingerprint;
            SharedRosterFingerprint = originalRoster.GlobalRosterFingerprint;
            OriginalHostSteamId = originalRoster.HostSteamId;
            OriginalLocalSteamId = originalRoster.LocalSteamId;
            LocalOriginalHumanSlot = nativeLocalHumanSlot;
            _players = originalRoster.Players().OrderBy(x => x.Slot).ToArray();
            if (!_players.Any(x => x.Slot == nativeLocalHumanSlot && x.Human && x.SteamId == OriginalLocalSteamId))
                throw new InvalidDataException("Original local Steam/slot mismatch");
            _market = originalPrices.OrderBy(x => x.Id).Select(x => new Good(x.Id, x.Name, x.MarketSell, x.MarketBuy)).ToArray();
            _rulers = originalProfiles.OrderBy(x => x.PlayerId).Select(x => x.Copy()).ToArray();
            _goods = originalGoods.OrderBy(x => x.Player).ThenBy(x => x.GoodId).ToArray();
            if (_market.Length != GoodCatalog.All.Count || _market.Any(x => GoodCatalog.Find(x.Id) == null) || _market.Select(x => x.Id).Distinct().Count() != _market.Length || _rulers.Length != _players.Length || !_rulers.Select(x => x.PlayerId).SequenceEqual(_players.Select(x => (int)x.Slot)) || _goods.Length != _players.Length * GoodCatalog.All.Count || _goods.Any(x => x == null) || _goods.GroupBy(x => ((long)x.Player << 16) | x.GoodId).Any(x => x.Count() != 1))
                throw new InvalidDataException("Incomplete original MP market, ruler or goods");
            foreach (var player in _players)
                foreach (var good in GoodCatalog.All)
                    if (!_goods.Any(x => x.Player == player.Slot && x.GoodId == good.Id))
                        throw new InvalidDataException("Original MP per-good cell missing");
            _originalAlliance = (bool[, ])observedDirectedNativeAlliances.Clone();
        }

        private static bool SameBytes(byte[] candidate, byte[] scope, int offset, int count)
        {
            if (candidate == null || scope == null || candidate.Length != count || offset < 0 || scope.Length - offset < count)
                return false;
            int difference = 0;
            for (int i = 0; i < count; i++)
                difference |= candidate[i] ^ scope[offset + i];
            return difference == 0;
        }

        internal byte[] CopyTrustedScope()
        {
            return (byte[])_trusted128.Clone();
        }

        internal CanonicalPlayer[] Players()
        {
            return (CanonicalPlayer[])_players.Clone();
        }

        internal Good[] Market()
        {
            return _market.Select(x => new Good(x.Id, x.Name, x.MarketSell, x.MarketBuy)).ToArray();
        }

        internal Ruler[] Profiles()
        {
            return _rulers.Select(x => x.Copy()).ToArray();
        }

        internal ObservedOriginalCommodity[] Goods()
        {
            return (ObservedOriginalCommodity[])_goods.Clone();
        }

        public ObservedOriginalCommodity[] NativeGoodsWithExplicitCapacityStatus()
        {
            return Goods();
        }

        internal bool[, ] OriginalAlliances()
        {
            return (bool[, ])_originalAlliance.Clone();
        }

        internal int UnknownCapacityCells()
        {
            return _goods.Count(x => x.CapacityState == OriginalCapacityAttestationState.Unknown);
        }

        public bool TryAuthoritativeHumanActor(byte slot, ulong observedSteamId)
        {
            return VerifiedRoster.IsOriginalHuman(slot, observedSteamId) && _players.Any(x => x.Slot == slot && x.Human && x.Alive && x.SteamId == observedSteamId);
        }

        public bool TryConsentPair(byte actor, byte recipient, out string refusal)
        {
            refusal = "original_mp_trade_relation_unavailable";
            if (actor < 1 || actor > 8 || recipient < 1 || recipient > 8 || actor == recipient)
                return false;
            var from = _players.FirstOrDefault(x => x.Slot == actor);
            var to = _players.FirstOrDefault(x => x.Slot == recipient);
            if (from == null || to == null || !from.Human || !from.Alive || !to.Alive)
            {
                refusal = "not_live_human_consent_pair";
                return false;
            }

            if (!_originalAlliance[actor, recipient] || !_originalAlliance[recipient, actor])
            {
                // Do not derive a signed non-allied TradePact from the
                // local solo diplomacy model. That is owner work.
                refusal = "no_proven_reciprocal_original_trade_alliance";
                return false;
            }

            refusal = "consent_only_original_pair_verified_no_goods_authority";
            return true;
        }

        // integration bridge: consumes ONLY a provenance object already
        // built by the selected  post-Chore RSA + original sender mapper.
        // Never constructs CanonicalTrustedHumanInput from an unsigned packet.
        // It deliberately validates consent terms/roles WITHOUT asking for
        // a fictitious stock capacity or authorizing settlement.
        internal bool TryValidateAlreadyAuthenticatedConsentOnly(CanonicalTrustedHumanInput originalVerifiedChore, out string reason)
        {
            reason = "missing_selected__original_chore_identity";
            if (originalVerifiedChore == null || originalVerifiedChore.Command == null || originalVerifiedChore.EngineVerifiedSoloLocal || originalVerifiedChore.VerifiedMapGeneration != LocalOriginalMapGeneration || originalVerifiedChore.VerifiedSteamId <= 1000)
                return false;
            var action = originalVerifiedChore.Command;
            if (action.Requester != originalVerifiedChore.VerifiedControllerSlot || action.Sequence < 1 || action.Epoch < 1 || action.ClientNonce < 1 || !TryAuthoritativeHumanActor(action.Requester, originalVerifiedChore.VerifiedSteamId))
                return false;
            if (!TryConsentPair(action.Requester, action.Recipient, out reason))
                return false;
            // The existing selected canonical reducer still verifies
            // action-specific counter/root/frozen terms and saved highwaters.
            reason = "original_human_consent_frame_ready_without_capacity";
            return true;
        }

        internal bool HasActualReciprocalNativeAlliance(byte a, byte b)
        {
            return a >= 1 && a <= 8 && b >= 1 && b <= 8 && a != b && _originalAlliance[a, b] && _originalAlliance[b, a];
        }

        internal ObservedOriginalCommodity ReadGood(byte player, ushort good)
        {
            return _goods.FirstOrDefault(x => x.Player == player && x.GoodId == good);
        }
    }

    // Future independently confirmed MP capacity can be plugged into this
    // READ-ONLY contract by the exclusive coordinator, after reviewing the
    // actual original native provider. Implementing this interface does not
    // grant native goods mutation or durable SaveData authority.
    internal interface IOriginalMultiplayerCapacityProof : OriginalInventoryEvidence.IOriginalWarehouseCapacityAttestation
    {
        bool TryProveOriginalMultihumanScope(byte[] exactSigned128ByteScope, long localMapGeneration, out string originalApiEvidence);
    }

    internal sealed partial class DiplomacyFeature
    {
        // MP Trade-Only bootstrap is deliberately NOT a call to
        // BeginMapSession/CaptureRosterAndCoalitions. The original legacy
        // diplomacy can remain _mapDisabled. Its native gift/order/save
        // hooks and NativeAuthorized() are never touched here.
        private bool TryObserveOriginalMpTradeOnlyFrame(long requestedTick, out OriginalMultiplayerConsentFrame snapshot, out string reason)
        {
            snapshot = null;
            reason = "original_multiplayer_world_unavailable";
            if (!IsNativeDiplomacySectionHeld)
            {
                reason = "original_engine_monitor_not_owned";
                return false;
            }

            long officialTick;
            string clockReason;
            if (!TryReadOriginalMapTick(out officialTick, out clockReason) || requestedTick != officialTick)
            {
                reason = "original_mp_capture_not_at_real_native_tick:" + clockReason;
                return false;
            }

            VerifiedOriginalHumanRoster witness;
            if (!TryReadVerifiedMultiHumanRoster(officialTick, out witness, out reason))
                return false;
            try
            {
                RequireNativeDiplomacySection();
                var api = GamePlayerManagerAPI.Instance;
                if (api == null || _signed == null || _mpMapIdentity == null || !_signed.Live || !_signed.Ready)
                    throw new InvalidDataException("Original signed MP trade-only bootstrap missing");
                // Refresh the ORIGINAL map file hash, native keep layout,
                // map length and source, not a translated name or CRC.
                // The cached Context.Scope MUST match this same full
                // map file, not merely the same native map-layout digest.
                PersistentMapIdentity fresh = CaptureMapIdentity();
                if (!fresh.Same(_mpMapIdentity) || !fresh.HasLayout(NativeStartLayoutHash()) || !OriginalMapMatchesContext(fresh.ScopeHash(), witness.Scope()))
                    throw new InvalidDataException("Full original MP map file/layout changed since signed bootstrap");
                var players = witness.Players();
                var market = new List<Good>();
                var rulers = new List<Ruler>();
                var goods = new List<ObservedOriginalCommodity>();
                var alliances = new bool[9, 9];
                foreach (var item in GoodCatalog.All)
                {
                    if (!api.IsTradeGoodAllowed((eGoods)item.Id))
                        throw new InvalidDataException("Original game does not authorize a catalog good");
                    var p = api.GetTradeBasePrice((eGoods)item.Id);
                    market.Add(ObservedTradeMarketSnapshot.FromOriginalPacked(item.Id, item.Name, p.BuyPrice, p.SellPrice));
                }

                foreach (var player in players)
                {
                    var ruler = OriginalGlobalRuler(player);
                    rulers.Add(ruler);
                    foreach (var item in GoodCatalog.All)
                    {
                        int quantity = GoodAmount(player.Slot, item.Id);
                        int pending = player.Human ? 0 : ReadCommittedAiDemand(player.Slot, item.Id);
                        goods.Add(new ObservedOriginalCommodity(player.Slot, item.Id, quantity, BoundedReserve(item.Id, quantity, ruler), pending, null)); // EXPLICITLY UNKNOWN native MP capacity
                    }
                }

                foreach (var a in players)
                    foreach (var b in players)
                        if (a.Slot != b.Slot)
                            alliances[a.Slot, b.Slot] = api.IsPlayerAlliedTo(a.Slot, b.Slot);
                // Double-check against the same original engine frame.
                // No second source of quantities or a guessed free capacity.
                long endOriginalTick;
                if (!TryReadOriginalMapTick(out endOriginalTick, out clockReason) || endOriginalTick != witness.OriginalSharedTick || ScriptExtenderNativeBridge.CurrentDay() != witness.Day || _mapEpoch != witness.OriginalMapGeneration || !_signed.Context.Scope.Same(new Bytes(witness.Scope())))
                    throw new InvalidDataException("Original multiplayer tick or signed scope changed");
                foreach (var cell in goods)
                    if (GoodAmount(cell.Player, cell.GoodId) != cell.NativeQuantity)
                        throw new InvalidDataException("Original MP quantity changed under read monitor");
                snapshot = new OriginalMultiplayerConsentFrame(witness, fresh, _signed.Descriptor.Id, (byte)api.GetLocalPlayerId(), market, rulers, goods, alliances);
                reason = "original_19_goods_and_all_humans_observed_capacity_unknown";
                return true;
            }
            catch (Exception e)
            {
                reason = "original_trade_only_mp_snapshot_refused:" + e.GetType().Name;
                return false;
            }
        }

        private static bool OriginalMapMatchesContext(byte[] actualFullMapScopeSha, byte[] signedScope128)
        {
            if (actualFullMapScopeSha == null || actualFullMapScopeSha.Length != 32 || signedScope128 == null || signedScope128.Length != 128)
                return false;
            int difference = 0;
            for (int i = 0; i < 32; i++)
                difference |= actualFullMapScopeSha[i] ^ signedScope128[i];
            return difference == 0;
        }

        // This is a separate COMPLETE physical view, not a way of faking
        // a capacity into a consent-only frame. The current runtime has only
        // OriginalSoloWarehouseCapacityReader, so it MUST return Unknown/fail.
        // A future owner-proved original MP capacity reader must be injected
        // explicitly and still cannot authorize native writes.
        private bool TryProjectFullReadOnlyCanonicalMpWorld(OriginalMultiplayerConsentFrame evidence, IOriginalMultiplayerCapacityProof realOriginalMpCapacity, out CanonicalTradeSnapshot full, out string reason)
        {
            full = null;
            reason = "original_mp_warehouse_capacity_unknown";
            long officialTick;
            string originalClockReason;
            if (!IsNativeDiplomacySectionHeld || evidence == null || realOriginalMpCapacity == null || Object.ReferenceEquals(realOriginalMpCapacity, _capacityAuthority) || !TryReadOriginalMapTick(out officialTick, out originalClockReason) || evidence.NativeSharedTick != officialTick || evidence.LocalOriginalMapGeneration != _mapEpoch || ScriptExtenderNativeBridge.CurrentDay() != evidence.NativeGameDay)
                return false;
            try
            {
                string originalProvenance;
                if (!realOriginalMpCapacity.TryProveOriginalMultihumanScope(evidence.CopyTrustedScope(), _mapEpoch, out originalProvenance) || string.IsNullOrEmpty(originalProvenance) || originalProvenance.Length > 160)
                    return false;
                var stocks = new ManagedGoodsSnapshotPort();
                foreach (var observed in evidence.Goods())
                {
                    int quantity = GoodAmount(observed.Player, observed.GoodId);
                    if (quantity != observed.NativeQuantity)
                        return false;
                    int actualCapacity;
                    if (!realOriginalMpCapacity.TryReadWarehouseCapacity(observed.Player, observed.GoodId, out actualCapacity) || actualCapacity < quantity || actualCapacity > int.MaxValue - 200000)
                        return false;
                    StockCell cell;
                    if (!OriginalInventoryEvidence.TryNativeBoundStock(observed.Player, observed.GoodId, quantity, observed.ConservativeReserve, 0, realOriginalMpCapacity, out cell) || cell.Capacity != actualCapacity)
                        return false;
                    if (cell.Capacity > 0)
                        cell.Buildings = Math.Min(cell.Capacity, observed.OriginalAiPendingPurchases);
                    cell.Validate();
                    stocks.Set(observed.Player, observed.GoodId, cell);
                }

                var pairs = new List<CanonicalPairObservation>();
                var roster = evidence.Players();
                foreach (var a in roster)
                    foreach (var b in roster)
                        if (a.Slot != b.Slot)
                        {
                            // Original alliance recheck is mandatory even for a
                            // previously observed consent frame.
                            var api = GamePlayerManagerAPI.Instance;
                            bool now = api.IsPlayerAlliedTo(a.Slot, b.Slot);
                            if (now != evidence.OriginalAlliances()[a.Slot, b.Slot])
                                return false;
                            bool ab = evidence.OriginalAlliances()[a.Slot, b.Slot];
                            bool ba = evidence.OriginalAlliances()[b.Slot, a.Slot];
                            ObservedTradePairState directed = ab && ba && a.Alive && b.Alive ? OriginalNativeAlliancePair(a, b, evidence.NativeGameDay) : UnknownDirectedPair(a, b, evidence.NativeGameDay);
                            pairs.Add(new CanonicalPairObservation(directed, ab, ba, false));
                        }

                long finalOfficialTick;
                if (!TryReadOriginalMapTick(out finalOfficialTick, out originalClockReason) || finalOfficialTick != evidence.NativeSharedTick || ScriptExtenderNativeBridge.CurrentDay() != evidence.NativeGameDay)
                    return false;
                full = new CanonicalTradeSnapshot(finalOfficialTick, evidence.NativeGameDay, evidence.SharedMapFingerprint, evidence.SharedRosterFingerprint, roster, stocks, evidence.Market(), evidence.Profiles(), pairs, evidence.LocalOriginalMapGeneration, 0);
                reason = "original_exact_mp_capacity_readonly_snapshot_no_native_release";
                return true;
            }
            catch (Exception e)
            {
                reason = "original_mp_capacity_or_projection_refused:" + e.GetType().Name;
                full = null;
                return false;
            }
        }
    }
}
