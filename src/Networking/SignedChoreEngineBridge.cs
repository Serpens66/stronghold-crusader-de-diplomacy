// Official Steam metadata bootstraps an ephemeral HOST key. Signed commands
// enter only original Chore 106, are captured without work in native callbacks,
// and are verified AFTER returning to managed code under the same engine lock.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CrusaderDE;
using HarmonyLib;
using SHCDESE.API;
using SHCDESE.API.Components.Network;
using SHCDESE.EventAPI;
using R3;
using Steamworks;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    // One existing bounded FIFO. Only this short lock protects its storage;
    // it never contains a game/API/controller call or an engine-monitor wait.
    // A reset retires identity, clears the SAME FIFO, and detaches the SAME
    // signed session atomically. Old producers/finalizers retain old identity.
    internal sealed class ManagedOriginalChoreCapture
    {
        internal sealed class Lifetime
        {
            internal int Retired, RevokeRequested, RevokeHandled;
        }

        private readonly object _sync = new object ();
        private readonly Queue<Bytes> _ordered = new Queue<Bytes>();
        internal Lifetime CurrentLifetime = new Lifetime();
        private long _mapEpoch, _networkGeneration;
        private bool _bound;
        internal bool IsCurrent(Lifetime expected)
        {
            return expected != null && Object.ReferenceEquals(Volatile.Read(ref CurrentLifetime), expected) && Volatile.Read(ref expected.Retired) == 0;
        }

        internal static void RequestRevoke(Lifetime expected)
        {
            if (expected != null)
                Interlocked.Exchange(ref expected.RevokeRequested, 1);
        }

        internal void RetireAndDetach(ref HostSignedTradeSession active, out HostSignedTradeSession retired)
        {
            lock (_sync)
            {
                var old = CurrentLifetime;
                Interlocked.Exchange(ref old.Retired, 1);
                _ordered.Clear();
                _bound = false;
                _mapEpoch = 0;
                _networkGeneration = 0;
                retired = Interlocked.Exchange(ref active, null);
                Volatile.Write(ref CurrentLifetime, new Lifetime());
            }
        }

        internal bool TryAppend(Lifetime expected, long mapEpoch, long networkGeneration, Bytes exactManagedBody)
        {
            lock (_sync)
            {
                if (!IsCurrent(expected) || Volatile.Read(ref expected.RevokeRequested) != 0)
                    return false;
                if (exactManagedBody == null || mapEpoch < 1 || networkGeneration < 1 || _ordered.Count >= 64 || (_bound && (_mapEpoch != mapEpoch || _networkGeneration != networkGeneration)))
                {
                    RequestRevoke(expected);
                    return false;
                }

                _bound = true;
                _mapEpoch = mapEpoch;
                _networkGeneration = networkGeneration;
                _ordered.Enqueue(exactManagedBody);
                return true;
            }
        }

        internal int Count(Lifetime expected)
        {
            lock (_sync)
                return IsCurrent(expected) ? _ordered.Count : 0;
        }

        internal bool TryPeek(Lifetime expected, out Bytes exactManagedBody)
        {
            exactManagedBody = null;
            lock (_sync)
            {
                if (!IsCurrent(expected) || Volatile.Read(ref expected.RevokeRequested) != 0 || _ordered.Count == 0)
                    return false;
                exactManagedBody = _ordered.Peek();
                return true;
            }
        }

        internal bool ConfirmHeadConsumed(Lifetime expected, Bytes exactManagedBody)
        {
            lock (_sync)
            {
                if (!IsCurrent(expected))
                    return false;
                if (_ordered.Count == 0 || !Object.ReferenceEquals(_ordered.Peek(), exactManagedBody))
                {
                    RequestRevoke(expected);
                    return false;
                }

                _ordered.Dequeue();
                return true;
            }
        }

        internal void ClearCurrent(Lifetime expected)
        {
            lock (_sync)
            {
                if (IsCurrent(expected))
                    _ordered.Clear();
            }
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private const short SignedPacketId = 1902;
        private HostSignedTradeSession _signed;
        // All state below belongs to the existing bridge and is transient per
        // original session. No second world cache, replay ledger or SaveData owner.
        private AuthenticatedHostActionInbox _hostInbox;
        private readonly ManagedOriginalChoreCapture _managedCapture = new ManagedOriginalChoreCapture();
        private long _nextReceiptRetry;
        private bool _activationIssued, _needsWorldFrame, _revokePending;
        private ulong _publishedInputFrontier;
        private HumanTradeActionEnvelope _humanAwaitingPublication;
        private PublicationFrameBinding _publication;
        private OriginalPeerPublicationQuorum _publicationQuorum;
        private SignedPublicationBarrier _publicationBarrier;
        private PeerPublicationAcknowledgement _localReceipt;
        private SteamIngress _signedInbox;
        private IDisposable _signedSubscription;
        private R3PacketEventHook<byte[]> _signedHook;
        private PersistentMapIdentity _mpMapIdentity;
        private long _mpIdentityEpoch = -1, _protocolGeneration, _nextContextPoll;
        private string _trustedKey, _protocolStatus;
        private ulong[] _boundSteamParticipants;
        private long _nextChallengeTick, _nextRetryTick;
        private MethodInfo _headlessRunTarget, _receiveTarget, _dispatchTarget, _strictQueueTarget;
        private bool _signedBridgeInstalled;
        private static DiplomacyFeature _orderedBridgeOwner;
        [ThreadStatic]
        private static byte[][] _capturedChores;
        [ThreadStatic]
        private static int _capturedCount, _engineCallDepth;
        [ThreadStatic]
        private static bool _captureOverflow, _captureActive;
        [ThreadStatic]
        private static DiplomacyFeature _captureOwner;
        [ThreadStatic]
        private static ManagedOriginalChoreCapture.Lifetime _captureLifetime;
        private static int _unownedCaptureFault;
        private struct OrderedCall
        {
            internal object Monitor;
            internal DiplomacyFeature Owner;
            internal ManagedOriginalChoreCapture.Lifetime CaptureLifetime;
            internal long Epoch, Generation;
            internal bool Entered, Outer;
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
            internal bool OriginalMilitaryRun;
            internal long MilitaryTickBefore;
#endif
        }

        private void ProtocolNote(string status)
        {
            if (_protocolStatus == status)
                return;
            _protocolStatus = status;
            Log("trade_lockstep", status + "|native_release=false");
        }

        private static int CapturedOriginalDay()
        {
            RequireNativeDiplomacySection();
            long day = ScriptExtenderNativeBridge.CurrentDay();
            if (day < 1 || day > int.MaxValue)
                throw new InvalidDataException("Original native day outside canonical Int32 range");
            return checked((int)day);
        }

        private static byte[] CanonicalStateHash(byte[] bytes)
        {
            using (var h = SHA256.Create())
                return h.ComputeHash(bytes);
        }

        private static byte[] HashText(string text)
        {
            return CanonicalStateHash(Encoding.UTF8.GetBytes(text));
        }

        private bool ReadTrustedNetworkContext(out TrustedContext context, out string key, out string why)
        {
            RequireNativeDiplomacySection();
            context = null;
            key = null;
            why = "multiplayer_session_not_ready";
            var data = GameData.Instance;
            var live = data == null ? null : data.lastGameState;
            var api = GamePlayerManagerAPI.Instance;
            if (!_applied || _mapEpoch < 1 || _sessionGeneration < 1 || data == null || live == null || api == null || data.coopTrailID != 0 || !GameNetworkAPI.IsNetworkedEnvironment() || !GameNetworkAPI.IsMultiplayerGame() || !GameNetworkAPI.IsPlayerSlotMappingAvailable())
                return false;
            int local = api.GetLocalPlayerId();
            if (local < 1 || local > 8 || local != data.playerID || local != GameNetworkAPI.GetLocalPlayerId())
            {
                why = "native_local_slot_disagrees";
                return false;
            }

            CSteamID steamLocal = SteamUser.GetSteamID();
            var host = GameNetworkAPI.GetHostSteamId();
            if (!steamLocal.IsValid() || steamLocal.m_SteamID <= 1000 || !host.HasValue || !host.Value.IsValid() || host.Value.m_SteamID <= 1000 || GameNetworkAPI.IsLocalHost() != (host.Value.m_SteamID == steamLocal.m_SteamID))
            {
                why = "actual_lobby_host_or_local_identity_unresolved";
                return false;
            }

            var members = GameNetworkAPI.GetPlayers();
            if (members == null || members.Count < 2 || members.Count > 8)
                return false;
            var slots = new Dictionary<int, Platform_Multiplayer.MPGameMember>();
            var realIds = new HashSet<ulong>();
            foreach (var p in members)
            {
                if (p == null || p.playerID < 1 || p.playerID > 8 || slots.ContainsKey(p.playerID))
                {
                    why = "network_slot_not_unique";
                    return false;
                }

                slots.Add(p.playerID, p);
                if (p.isSelf && p.playerID != local)
                {
                    why = "network_self_slot_disagrees";
                    return false;
                }

                if (p.steamID > 1000 && !realIds.Add(p.steamID))
                {
                    why = "steam_identity_not_unique";
                    return false;
                }
            }

            var humans = new List<int>();
            var steamIds = new ulong[9];
            byte[] rosterHash;
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m))
            {
                w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.EngineRosterFingerprintDomain);
                for (int id = 1; id <= 8; id++)
                {
                    bool human = live.is_valid_player(id), ai = live.is_skirmish_player(id);
                    if (human && ai)
                    {
                        why = "ambiguous_native_player_role";
                        return false;
                    }

                    if ((human || ai) && api.IsAIPlayer(id) != ai)
                    {
                        why = "native_and_snapshot_role_disagree";
                        return false;
                    }

                    Platform_Multiplayer.MPGameMember member;
                    slots.TryGetValue(id, out member);
                    if (human)
                    {
                        if (member == null || member.kicked || member.skirmishAI || member.steamID <= 1000 || (id == local && (!member.isSelf || member.steamID != steamLocal.m_SteamID)) || (id != local && member.isSelf) || GameNetworkAPI.GetPlayerIdForSteamId(new CSteamID(member.steamID)) != id)
                        {
                            why = "registered_human_steam_mapping_unresolved";
                            return false;
                        }

                        humans.Add(id);
                        steamIds[id] = member.steamID;
                    }
                    else if (member != null && (member.isSelf || member.steamID > 1000))
                    {
                        why = "unregistered_or_ai_slot_has_real_peer";
                        return false;
                    }

                    w.Write(id);
                    w.Write(human);
                    w.Write(ai);
                    w.Write(steamIds[id]);
                    w.Write(ai ? (int)api.GetAILord(id) : 0);
                }

                w.Flush();
                rosterHash = CanonicalStateHash(m.ToArray());
            }

            if (humans.Count < 2 || !humans.Contains(local) || !steamIds.Contains(host.Value.m_SteamID))
            {
                why = "complete_multiplayer_human_roster_not_proven";
                return false;
            }

            if (_mpIdentityEpoch != _mapEpoch || _mpMapIdentity == null)
            {
                _mpMapIdentity = CaptureMapIdentity();
                _mpIdentityEpoch = _mapEpoch;
            }

            // Recheck immutable native layout even when the original source digest is
            // cached. Live balances, teams and defeat state never define identity.
            if (!_mpMapIdentity.HasLayout(NativeStartLayoutHash()))
            {
                why = "native_map_layout_changed";
                return false;
            }

            if (_rules == null || _rules.Validate() != null)
            {
                why = "local_rules_invalid";
                return false;
            }

            var rules = new StringBuilder(StrongholdEuropeDE.Runtime.CompatibilityContracts.TradeRulesDomain);
            foreach (var field in typeof(DiplomacyRules).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.FieldType != typeof(int))
                {
                    why = "unknown_rule_type";
                    return false;
                }

                rules.Append('|').Append(field.Name).Append('=').Append(((int)field.GetValue(_rules)).ToString(CultureInfo.InvariantCulture));
            }

            var assembly = typeof(DiplomacyFeature).Assembly;
            byte[] buildHash = HashText(assembly.GetName().FullName + "|" + assembly.ManifestModule.ModuleVersionId.ToString("D") + "|" + DisposableGameScope.GameManagedSha + "|" + DisposableGameScope.RawraSha);
            byte[] scope = new byte[128];
            Buffer.BlockCopy(_mpMapIdentity.ScopeHash(), 0, scope, 0, 32);
            Buffer.BlockCopy(rosterHash, 0, scope, 32, 32);
            Buffer.BlockCopy(buildHash, 0, scope, 64, 32);
            Buffer.BlockCopy(HashText(rules.ToString()), 0, scope, 96, 32);
            key = BitConverter.ToString(CanonicalStateHash(scope)).Replace("-", "") + "|" + steamLocal.m_SteamID + "|" + host.Value.m_SteamID + "|" + _mapEpoch + "|" + _sessionGeneration;
            context = new TrustedContext(Math.Max(1, _protocolGeneration), host.Value.m_SteamID, steamLocal.m_SteamID, scope, humans.Select(i => steamIds[i]).ToArray());
            _boundSteamParticipants = steamIds;
            why = "actual_engine_roster_and_steam_host_proven";
            return true;
        }

        private void StartSignedSteamInbox()
        {
            if (_signedSubscription != null)
                return;
            var methods = typeof(GameNetworkAPI).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Where(m => m.Name == "GetPacketEventFor" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CustomNetworkPacketType)).ToArray();
            if (methods.Length != 1)
                throw new MissingMethodException("Unique official packet registry missing");
            var hook = methods[0].MakeGenericMethod(typeof(byte[])).Invoke(GameNetworkAPI.Instance, new object[] { (CustomNetworkPacketType)SignedPacketId }) as R3PacketEventHook<byte[]>;
            if (hook == null || hook.GetPacketId() != SignedPacketId)
                throw new InvalidDataException("Fixed signed packet ID collision");
            var inbox = new SteamIngress(32);
            _signedInbox = inbox;
            _signedSubscription = ObservableSubscribeExtensions.Subscribe(hook.GetBaseHook().Observable, e => ReceiveAuthenticatedSteamPacket(inbox, e));
            _signedHook = hook;
        }

        private void ResetSignedSession()
        {
            // Gate-only teardown MUST NOT wait for the original engine monitor.
            // The short FIFO synchronization atomically retires capture identity and
            // detaches the old session before any remaining teardown work.
            HostSignedTradeSession previous;
            _managedCapture.RetireAndDetach(ref _signed, out previous);
            var inbox = Interlocked.Exchange(ref _signedInbox, null);
            if (inbox != null)
                inbox.Close();
            if (_signedSubscription != null)
            {
                _signedSubscription.Dispose();
                _signedSubscription = null;
            }

            _signedHook = null;
            if (_hostInbox != null)
                _hostInbox.Invalidate();
            _hostInbox = null;
            if (_publicationQuorum != null)
                _publicationQuorum.Revoke();
            _publicationQuorum = null;
            _publication = null;
            _publicationBarrier = null;
            _localReceipt = null;
            _humanAwaitingPublication = null;
            _activationIssued = false;
            _needsWorldFrame = false;
            _publishedInputFrontier = 0;
            _nextReceiptRetry = 0;
            _revokePending = true;
            _existingEngineSubmitIntent = null;
            if (previous != null)
                previous.Dispose();
            _trustedKey = null;
            _boundSteamParticipants = null;
            _mpMapIdentity = null;
            _mpIdentityEpoch = -1;
            _nextChallengeTick = 0;
            _nextRetryTick = 0;
            _nextContextPoll = 0;
        }

        private bool SendBootstrap(Bytes wire, ulong recipient)
        {
            RequireNativeDiplomacySection();
            if (wire == null || _signed == null || _signedHook == null || _boundSteamParticipants == null || Volatile.Read(ref _managedCapture.CurrentLifetime.RevokeRequested) != 0)
                return false;
            var envelope = AdmissionCodec.Decode(wire);
            // This one sender is reused for bootstrap and unsigned host-bound
            // intents/receipts. It NEVER transports any signed execution packet.
            if (envelope.Kind != PacketKind.Challenge && envelope.Kind != PacketKind.Hello && envelope.Kind != PacketKind.Ack && envelope.Kind != PacketKind.HumanIntent && envelope.Kind != PacketKind.PeerPublicationReceipt)
                return false;
            if ((envelope.Kind == PacketKind.HumanIntent || envelope.Kind == PacketKind.PeerPublicationReceipt) && (recipient != _signed.Context.HostSteamId || _signed.IsHost))
                return false;
            int slot = Array.IndexOf(_boundSteamParticipants, recipient);
            if (slot < 1 || slot > 8)
                return false;
            var player = GameNetworkAPI.GetPlayerById(slot);
            if (player == null || player.kicked || player.skirmishAI || player.steamID != recipient)
                return false;
            GameNetworkAPI.SendPacketToPlayerId<byte[]>(slot, wire.Copy(), SignedPacketId);
            return true;
        }

        private void PumpSignedNetwork()
        {
            if (!_signedBridgeInstalled)
                return;
            var waitingCapture = Volatile.Read(ref _managedCapture.CurrentLifetime);
            if (_captureActive && Object.ReferenceEquals(_captureOwner, this))
                return;
            if (_featureTicks < _nextContextPoll && (_signedInbox == null || _signedInbox.Count == 0) && _managedCapture.Count(waitingCapture) == 0 && Volatile.Read(ref waitingCapture.RevokeRequested) == 0 && Volatile.Read(ref _unownedCaptureFault) == 0)
                return;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            using (lease)
            {
                bool ownerEntered = false;
                try
                {
                    Monitor.TryEnter(_gate, 0, ref ownerEntered);
                    if (!ownerEntered)
                        return;
                    var captureLifetime = Volatile.Read(ref _managedCapture.CurrentLifetime);
                    if (!ServiceCaptureRevocationUnderOwner(captureLifetime))
                        return;
                    TrustedContext context;
                    string key, why;
                    if (!ReadTrustedNetworkContext(out context, out key, out why))
                    {
                        if (_signed != null)
                            ResetSignedSession();
                        _nextContextPoll = _featureTicks + 30;
                        ProtocolNote("status=waiting|reason=" + why);
                        return;
                    }

                    if (_signed == null || key != _trustedKey)
                    {
                        ResetSignedSession();
                        _protocolGeneration = checked(_protocolGeneration + 1);
                        if (!ReadTrustedNetworkContext(out context, out key, out why))
                            return;
                        _signed = new HostSignedTradeSession(context);
                        _trustedKey = key;
                        StartSignedSteamInbox();
                        if (_signed.IsHost)
                            _hostInbox = new AuthenticatedHostActionInbox(context, _signed.Descriptor, (ulong[])_boundSteamParticipants.Clone());
                        ProtocolNote("status=authenticated_bootstrap_started|role=" + (_signed.IsHost ? "host" : "peer") + "|humans=" + context.HumanIds().Length);
                    }

                    _nextContextPoll = _featureTicks + 30;
                    var inbox = _signedInbox;
                    InboundObservation incoming;
                    for (int i = 0; i < 16 && inbox != null && inbox.TryTake(out incoming); i++)
                    {
                        if (!Object.ReferenceEquals(inbox, _signedInbox) || !DeliveryEpoch.Matches(incoming.MapEpoch, incoming.SessionGeneration, _mapEpoch, _sessionGeneration))
                            continue;
                        try
                        {
                            ulong replyTo;
                            AdmissionReason routed;
                            Bytes reply = AuthenticatedTradeIngressRouter.Route(_signed, _hostInbox, new Bytes(incoming.CopyWire()), new SteamDelivery(_signed.Context.Generation, incoming.VerifiedSenderSteamId), ReadActivatedCanonicalActor, _publicationQuorum, ActualOriginalSteamSlot, out replyTo, out routed);
                            if (reply != null)
                                SendBootstrap(reply, replyTo);
                        }
                        catch (AdmissionException e)
                        {
                            if (_publicationQuorum != null && _publicationQuorum.IsRevoked)
                            {
                                _signed.Dispose();
                                _revokePending = true;
                            }

                            ProtocolNote("status=bootstrap_packet_refused|reason=" + e.Reason);
                        }
                        catch (Exception e)
                        {
                            ProtocolNote("status=bootstrap_packet_refused|reason=" + e.GetType().Name);
                        }
                    }

                    if (!_signed.Live)
                    {
                        ProtocolNote("status=stream_revoked_requires_fresh_lifecycle");
                        return;
                    }

                    if (!_signed.IsHost && _featureTicks >= _nextChallengeTick)
                    {
                        SendBootstrap(_signed.Challenge, _signed.Context.HostSteamId);
                        _nextChallengeTick = _featureTicks + 300;
                    }

                    if (_signed.Ready)
                        ProtocolNote(_signed.IsHost ? "status=all_human_host_acks_complete|execution=chore_only" : "status=host_key_pinned|ack_scope=local_peer_only|execution=chore_only");
                    // Native execution already captured into our FIFO takes precedence
                    // over retransmission. The FIFO is NOT a second replay ledger.
                    RetryAfterCapturedFifoUnderOwner(context);
                }
                catch (Exception e)
                {
                    var failed = Volatile.Read(ref _managedCapture.CurrentLifetime);
                    if (Volatile.Read(ref failed.RevokeRequested) != 0 || (_signed != null && !_signed.Live))
                    {
                        ManagedOriginalChoreCapture.RequestRevoke(failed);
                        _revokePending = true;
                    }
                    else
                        ResetSignedSession();
                    _nextContextPoll = _featureTicks + 60;
                    ProtocolNote("status=network_context_unavailable|reason=" + e.GetType().Name);
                }
                finally
                {
                    if (ownerEntered)
                        Monitor.Exit(_gate);
                }
            }
        }

        private bool OwnsOrderedEngineBridge()
        {
            if (!_signedBridgeInstalled || _harmony == null || _dispatchTarget == null)
                return false;
            foreach (var target in new[]
            {
                _headlessRunTarget,
                _receiveTarget
            }

            )
            {
                if (target == null)
                    return false;
                string prefixMethod = "BeginOrderedEngineCall";
                string finalizerMethod = "FinishOrderedEngineCall";
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                if (Object.ReferenceEquals(target, _headlessRunTarget) &&
                    OriginalPreWorkerRegistration.IsInstalled)
                {
                    prefixMethod = "BeginCombinedRun";
                    finalizerMethod = "FinishCombinedRun";
                }
#endif
                var p = Harmony.GetPatchInfo(target);
                if (p == null || !p.Prefixes.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == prefixMethod) || !p.Postfixes.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == "EndOrderedEngineCall") || !p.Finalizers.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == finalizerMethod))
                    return false;
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                if (Object.ReferenceEquals(target, _headlessRunTarget) &&
                    OriginalPreWorkerRegistration.IsInstalled &&
                    !p.Transpilers.Any(x => x.owner == _harmony.Id &&
                        x.PatchMethod.Name == "TranspileUnarmed"))
                    return false;
#endif
            }

            var dispatch = Harmony.GetPatchInfo(_dispatchTarget);
            return dispatch != null && dispatch.Prefixes.Any(x => x.owner == _harmony.Id && x.PatchMethod.Name == "CaptureStrictChorePrefix");
        }

        private bool QueueStrictSignedChore(Bytes wire)
        {
            RequireNativeDiplomacySection();
            if (!OwnsOrderedEngineBridge() || _strictQueueTarget == null || wire == null || wire.Length > AdmissionCodec.MaximumBytes || _signed == null || !_signed.IsHost || !_signed.Ready || Volatile.Read(ref _managedCapture.CurrentLifetime.RevokeRequested) != 0 || _managedCapture.Count(Volatile.Read(ref _managedCapture.CurrentLifetime)) != 0)
                return false;
            TrustedContext current;
            string key, why;
            if (!ReadTrustedNetworkContext(out current, out key, out why) || key != _trustedKey)
                return false;
            var payload = new byte[wire.Length + 2];
            payload[0] = (byte)(SignedPacketId & 255);
            payload[1] = (byte)(SignedPacketId >> 8);
            Buffer.BlockCopy(wire.Copy(), 0, payload, 2, wire.Length);
            // This pinned INTERNAL method has no Steam fallback. True means queued,
            // never a native commit, saved receipt, peer vote or engine acknowledgement.
            return (bool)_strictQueueTarget.Invoke(null, new object[] { payload });
        }

        private QueueResult QueueSavedHostQuote(TradeRecord record)
        {
            RequireNativeDiplomacySection();
            if (record == null || _signed == null || !_signed.IsHost || _archive == null || !Object.ReferenceEquals(_archive.Record(record.Id), record) || record.State != TradeDisposition.PreparingNative || record.IssuedDay > Math.Max(1, _lastProcessedDay) || record.ExpiryDay < Math.Max(1, _lastProcessedDay))
                return QueueResult.Refused;
            record.Validate();
            return _signed.Queue(record.Id, record.RootId, record.Requester, record.Recipient, record.OfferedGood, record.RequestedGood, record.OfferedAmount, record.RequestedAmount, record.IssuedDay, record.ExpiryDay, QueueStrictSignedChore);
        }

        internal static bool SignedTermsMatch(TradeCommand c, TradeRecord r)
        {
            return c != null && r != null && c.QuoteId == r.Id && c.RootId == r.RootId && c.Requester == r.Requester && c.Recipient == r.Recipient && c.OfferedGood == r.OfferedGood && c.RequestedGood == r.RequestedGood && c.OfferedAmount == r.OfferedAmount && c.RequestedAmount == r.RequestedAmount && c.IssuedDay == r.IssuedDay && c.ExpiresDay == r.ExpiryDay;
        }

        private void ReduceSignedTrade(TradeCommand command)
        {
            RequireNativeDiplomacySection();
            // Authentication, canonical quote admission, and native commit authority
            // are separate results. Inspect existing terms without creating a quote,
            // updating a replay ledger in SaveData, or inventing a native receipt.
            string terms = "canonical_state_busy";
            bool entered = false;
            try
            {
                Monitor.TryEnter(_gate, 0, ref entered);
                if (entered)
                {
                    var record = _archive == null ? null : _archive.Record(command.QuoteId);
                    int day = Math.Max(1, _lastProcessedDay);
                    terms = record == null ? "canonical_quote_missing" : !SignedTermsMatch(command, record) ? "canonical_terms_differ" : record.State != TradeDisposition.PreparingNative ? "canonical_quote_not_prepared" : day < command.IssuedDay || day > command.ExpiresDay ? "quote_expired" : !IsAlivePlayer(command.Requester) || !IsAlivePlayer(command.Recipient) ? "party_not_alive" : "canonical_terms_verified";
                }
            }
            finally
            {
                if (entered)
                    Monitor.Exit(_gate);
            }

            // No multiplayer native mutation port has yet obtained a durable original
            // game-save witness on every peer. Keep all native writes closed even when
            // the signature and prepared quote match. This is not a commit result.
            ProtocolNote("status=ordered_signed_intent_verified|sequence=" + command.Sequence + "|quote=" + command.QuoteId + "|terms=" + terms + "|execution=blocked_native_save_authority");
        }

        private bool ActualOriginalSteamSlot(byte slot, ulong steam)
        {
            RequireNativeDiplomacySection();
            if (slot < 1 || slot > 8 || steam <= 1000 || _boundSteamParticipants == null || _boundSteamParticipants[slot] != steam)
                return false;
            var actual = GameNetworkAPI.GetPlayerById(slot);
            return actual != null && !actual.kicked && !actual.skirmishAI && actual.steamID == steam && GameNetworkAPI.GetPlayerIdForSteamId(new CSteamID(steam)) == slot;
        }

        private PersistedHumanActionCursor ReadActivatedCanonicalActor(byte actor)
        {
            return _publishedInputFrontier == 0 ? null : ReadSavedCanonicalActor(actor);
        }

        // Existing UI delegate: keep the exact bounded unsigned intent immutable.
        // Host-local admission uses the actual native local register separately.
        private bool SubmitOriginalHumanIntent(Bytes wire, byte actor)
        {
            RequireNativeDiplomacySection();
            if (OriginalCaptureRevocationPending || !Monitor.IsEntered(_gate) || _signed == null || !_signed.Live || !_signed.Ready || wire == null || _publishedInputFrontier == 0)
                return false;
            TrustedContext current;
            string key, reason;
            if (!ReadTrustedNetworkContext(out current, out key, out reason) || key != _trustedKey || GamePlayerManagerAPI.Instance == null || GamePlayerManagerAPI.Instance.GetLocalPlayerId() != actor || !ActualOriginalSteamSlot(actor, current.LocalSteamId))
                return false;
            var request = HumanTradeActionEnvelope.DecodeClientIntent(wire, _signed.Descriptor.Id);
            if (request.ActorSlot != actor)
                return false;
            if (!_signed.IsHost)
                return SendBootstrap(wire, current.HostSteamId);
            if (_hostInbox == null)
                return false;
            var cursor = ReadSavedCanonicalActor(actor);
            if (cursor == null || cursor.Epoch == 0)
                return false;
            if (OriginalCaptureRevocationPending)
                return false;
            var admitted = _hostInbox.StageHostLocal(wire, actor, cursor.Epoch, cursor.LastSequence, cursor.LastNonce);
            if (OriginalCaptureRevocationPending)
            {
                ServiceCaptureRevocationUnderOwner(Volatile.Read(ref _managedCapture.CurrentLifetime));
                return false;
            }

            return admitted == AdmissionReason.ok || admitted == AdmissionReason.duplicate;
        }

        private PublishedSignedChoreFrontier ActualWorldFrontier(ulong effectiveEpoch, long actualTick, int actualDay)
        {
            RequireNativeDiplomacySection();
            if (!Monitor.IsEntered(_gate) || _signed == null || _signed.LastVerifiedInputSequence == 0)
                return null;
            byte[] actualBefore = ReadActualBeforeCanonicalOwnerBytes();
            ulong savedEpoch = CanonicalTradeCodec.Decode(actualBefore).InputEpoch;
            return new PublishedSignedChoreFrontier(_signed.Descriptor.Id, _signed.Context.Scope, savedEpoch, effectiveEpoch, _signed.LastVerifiedInputSequence, _publishedInputFrontier, _mapEpoch, actualTick, actualDay, actualBefore);
        }

        // Called ONLY in this existing managed post-native drain. Initial real
        // world observation precedes activation; a cache epoch is not a bootstrap
        // prerequisite. No ReadSimulationTick or invented original outer envelope.
        private bool ObserveOriginalUnderOwner(TrustedContext current)
        {
            if (_revokePending)
            {
                RevokeControllerOriginalSession(true);
                _revokePending = false;
            }

            long now;
            string why;
            if (!TryReadOriginalMapTick(out now, out why))
                return false;
            OriginalMultiplayerConsentFrame observed;
            if (!TryObserveOriginalMpTradeOnlyFrame(now, out observed, out why))
                return false;
            InitializeOriginalConsentOwner(observed, current);
            _existingEngineSubmitIntent = SubmitOriginalHumanIntent;
            ulong epoch = CapturedSignedInputEpoch();
            if (epoch == 0 || _signed.LastVerifiedInputSequence == 0)
                return true;
            var frontier = ActualWorldFrontier(epoch, now, observed.NativeGameDay);
            return TryCaptureActualConsentWorldForCurrentNativeTick(epoch, frontier, out why);
        }

        private void StartNextOriginalInput()
        {
            if (_publication != null)
            {
                // A next globally ordered input is legal only after this peer applied
                // the previous original signed barrier. Late unsigned receipt retries
                // have no canonical effect; one current local receipt remains bounded.
                if (_localReceipt == null || _localReceipt.Phase != PublicationReceiptPhase.OwnerReadbackApplied)
                    throw new InvalidOperationException("Input before original publication barrier");
                _publication = null;
                _publicationBarrier = null;
                _localReceipt = null;
                _publicationQuorum = null;
            }

            _needsWorldFrame = true;
        }

        private void OnOriginalActivation(AuthenticatedHumanActionActivation activation)
        {
            StartNextOriginalInput();
            OnVerifiedSignedActivation(activation);
        }

        private void OnOriginalHuman(HumanTradeActionEnvelope action)
        {
            StartNextOriginalInput();
            if (_humanAwaitingPublication != null)
                throw new InvalidOperationException("One human input per publication round");
            if (!ActualOriginalSteamSlot(action.ActorSlot, action.ActorSteamId))
                throw new AdmissionException(AdmissionReason.sender_not_participant);
            OnVerifiedSignedHumanAction(action);
            _humanAwaitingPublication = action;
        }

        private void AdoptActualPreparedRound()
        {
            PublicationFrameBinding binding;
            PeerPublicationAcknowledgement local;
            if (_publication != null || !TryGetPreparedPublication(out binding, out local))
                return;
            if (binding == null || local == null || local.Phase != PublicationReceiptPhase.CandidateReady || !local.Binding.Same(binding) || binding.LastIncludedVerifiedChore != _signed.LastVerifiedInputSequence || binding.FrameChoreSequence != binding.LastIncludedVerifiedChore + 1 || !binding.SessionId.Same(_signed.Descriptor.Id) || !ActualOriginalSteamSlot(local.ActorSlot, _signed.Context.LocalSteamId))
                throw new InvalidDataException("Missing actual original owner readiness binding");
            _publication = binding;
            _localReceipt = local;
            _nextReceiptRetry = 0;
            if (_signed.IsHost)
            {
                _publicationQuorum = new OriginalPeerPublicationQuorum(_signed.Context, _signed.Descriptor, (ulong[])_boundSteamParticipants.Clone(), binding);
                _publicationQuorum.ReceiveOriginalOwnerLocalReceipt(local, local.ActorSlot, _signed.Context.LocalSteamId);
            }
        }

        private void OnOriginalWorldFrame(OrderedHostWorldWitness frame)
        {
            long now;
            string why;
            if (!TryReadOriginalMapTick(out now, out why))
                throw new InvalidDataException("Original elapsed-map tick unavailable");
            StageVerifiedWorldFrame(frame, now, CapturedOriginalDay());
            AdoptActualPreparedRound();
            if (_publication == null)
                throw new InvalidDataException("Verified WorldFrame did not stage actual owner");
        }

        private void OnOriginalPublicationBarrier(SignedPublicationBarrier barrier)
        {
            if (_publication == null || barrier == null || !barrier.Binding.Same(_publication) || barrier.GlobalChoreSequence != _publication.FrameChoreSequence + 1)
                throw new InvalidDataException("Original ordered barrier differs from local stage");
            var applied = PublishVerifiedBarrier(barrier, barrier.GlobalChoreSequence);
            if (applied == null || applied.Phase != PublicationReceiptPhase.OwnerReadbackApplied || !applied.Binding.Same(barrier.Binding) || applied.PublishBarrierChoreSequence != barrier.GlobalChoreSequence || !PublicationFrameBinding.Same32(ReadActualPublishedOwnerProjectionSha(), barrier.Binding.OwnerReadbackSha()))
                throw new InvalidDataException("Actual same-owner publication readback failed");
            // This observation advances ONLY after the existing canonical owner
            // actually published. It is not the ChoreSession replay cursor.
            string released;
            if (!TryReleasePinnedAfterOrderedBarrier(barrier.Binding.FrameChoreSequence, _signed.LastVerifiedChoreSequence, out released))
                throw new InvalidDataException("Original ordered barrier did not release its pinned witness: " + released);
            _publishedInputFrontier = barrier.Binding.LastIncludedVerifiedChore;
            if (_signed.IsHost)
            {
                if (_publicationQuorum == null || _publicationQuorum.ExistingBarrier == null || !_publicationQuorum.ExistingBarrier.Binding.Same(barrier.Binding))
                    throw new InvalidDataException("No original host signed barrier quorum");
                _publicationQuorum.ConfirmOriginalLocallyConsumedBarrier(barrier, _signed.LastVerifiedChoreSequence, ReadActualPublishedOwnerProjectionSha());
                _publicationQuorum.ReceiveOriginalOwnerLocalReceipt(applied, applied.ActorSlot, _signed.Context.LocalSteamId);
                if (_humanAwaitingPublication != null)
                {
                    var cursor = ReadSavedCanonicalActor(_humanAwaitingPublication.ActorSlot);
                    _hostInbox.ConfirmCanonicalConsumption(_humanAwaitingPublication, cursor.LastSequence, cursor.LastNonce);
                }
            }
            else
                _publicationBarrier = barrier;
            _humanAwaitingPublication = null;
            _localReceipt = applied;
            _nextReceiptRetry = 0;
        }

        private void DriveOriginalHostAndReceipts(TrustedContext current)
        {
            AdoptActualPreparedRound();
            if (!_signed.IsHost)
            {
                if (_localReceipt != null && _featureTicks >= _nextReceiptRetry)
                {
                    SendBootstrap(_localReceipt.UnsignedSteamPacket(), current.HostSteamId);
                    _nextReceiptRetry = _featureTicks + 60;
                }

                return;
            }

            if (_publicationQuorum != null && _publicationQuorum.IsRevoked)
                throw new AdmissionException(AdmissionReason.session_invalidated);
            if (_signed.IsBusyWithOrderedChore)
                return;
            if (_publication != null)
            {
                if (_publicationBarrier == null)
                {
                    if (!_publicationQuorum.AllActualOriginalHumansReady)
                        return;
                    var barrier = new SignedPublicationBarrier(_signed.NextHostSequence, _publication);
                    var result = _signed.QueuePublicationBarrier(barrier, _publicationQuorum, QueueStrictSignedChore);
                    if (result == QueueResult.QueuedNotCommitted || result == QueueResult.RetainedForRetry)
                        _publicationBarrier = barrier;
                    return;
                }

                if (!_publicationQuorum.AllActualOriginalHumansApplied)
                    return;
                _signed.ConfirmExactPublishedOwnerQuorum(_publicationQuorum, _publicationBarrier, ReadActualPublishedOwnerProjectionSha());
                _publicationQuorum = null;
                _publication = null;
                _publicationBarrier = null;
                _localReceipt = null;
            }

            if (!_activationIssued)
            {
                var queued = _signed.QueueActivation(NextOriginalActivationEpoch(), QueueStrictSignedChore);
                if (queued == QueueResult.QueuedNotCommitted || queued == QueueResult.RetainedForRetry)
                    _activationIssued = true;
                return;
            }

            if (_needsWorldFrame)
            {
                long now;
                string why;
                if (!TryReadOriginalMapTick(out now, out why))
                    return;
                ulong epoch = CapturedSignedInputEpoch();
                if (epoch == 0)
                    return;
                var frontier = ActualWorldFrontier(epoch, now, CapturedOriginalDay());
                OrderedHostWorldWitness frame;
                if (!TryPrepareHostKind8ConsentFrameCandidate(epoch, _signed.NextHostSequence, frontier, out frame, out why))
                    return;
                var queued = _signed.QueueWorldFrame(frame, QueueStrictSignedChore);
                if (queued == QueueResult.QueuedNotCommitted || queued == QueueResult.RetainedForRetry)
                {
                    // Same original monitor: exact just-signed world cannot be evicted
                    // between capture/sign/pin. A strict queue rejection still retains
                    // the signer's immutable bytes and therefore pins this same witness.
                    if (!TryRetainQueuedHostSignedWorldFrame(frame, _signed.LastVerifiedInputSequence, out why))
                        throw new InvalidDataException("Signed original WorldFrame could not be pinned: " + why);
                    _needsWorldFrame = false;
                }

                return;
            }

            if (_signed.HasPublicationFrame || _hostInbox == null || _hostInbox.Count == 0)
                return;
            var next = _hostInbox.OldestToSign(_signed.NextHostSequence);
            _signed.QueueHumanAction(next, QueueStrictSignedChore);
        }

        // Always original engine monitor + nonblocking _gate, never raw/native.
        // A finalizer/raw marker only addresses its captured lifetime. Reset can
        // retire that lifetime without letting its late notification hit a new one.
        private bool ServiceCaptureRevocationUnderOwner(ManagedOriginalChoreCapture.Lifetime lifetime)
        {
            RequireNativeDiplomacySection();
            if (!Monitor.IsEntered(_gate))
                throw new InvalidOperationException("Capture owner gate missing");
            if (!_managedCapture.IsCurrent(lifetime))
                return false;
            if (Interlocked.Exchange(ref _unownedCaptureFault, 0) != 0)
                ManagedOriginalChoreCapture.RequestRevoke(lifetime);
            if (_signed != null && !_signed.Live)
                ManagedOriginalChoreCapture.RequestRevoke(lifetime);
            if (Volatile.Read(ref lifetime.RevokeRequested) == 0)
            {
                // A same-map rekey can already have a new ready signed object while its
                // old controller projection still awaits revocation. Retire that owner
                // before trusting the new context; external save/UI stays fenced meanwhile.
                if (_revokePending)
                {
                    RevokeControllerOriginalSession(true);
                    _revokePending = false;
                }

                return true;
            }

            if (Volatile.Read(ref lifetime.RevokeHandled) == 0)
            {
                bool hadSignedOwner = _signed != null;
                if (hadSignedOwner)
                {
                    _revokePending = true;
                    _signed.Dispose();
                }

                // Retry controller cleanup if it throws. Never ACK a partial cleanup.
                if (_revokePending)
                {
                    RevokeControllerOriginalSession(true);
                    _revokePending = false;
                }

                if (_hostInbox != null)
                    _hostInbox.Invalidate();
                if (_publicationQuorum != null)
                    _publicationQuorum.Revoke();
                _hostInbox = null;
                _publicationQuorum = null;
                _publication = null;
                _publicationBarrier = null;
                _localReceipt = null;
                _humanAwaitingPublication = null;
                _needsWorldFrame = false;
                _managedCapture.ClearCurrent(lifetime);
                Volatile.Write(ref lifetime.RevokeHandled, 1);
                ProtocolNote("status=capture_lifetime_revoked_requires_fresh_lifecycle");
            }

            return false;
        }

        private bool ConsumeCapturedFifoUnderOwner(TrustedContext current, ManagedOriginalChoreCapture.Lifetime lifetime)
        {
            if (!ServiceCaptureRevocationUnderOwner(lifetime) || _signed == null || !_signed.Live || !_signed.Ready)
                return false;
            try
            {
                if (!ObserveOriginalUnderOwner(current))
                    return false;
                Bytes exact;
                while (_managedCapture.TryPeek(lifetime, out exact))
                {
                    if (!ServiceCaptureRevocationUnderOwner(lifetime))
                        return false;
                    var result = _signed.ConsumeOrderedMixed(exact, ReduceSignedTrade, OnOriginalHuman, OnOriginalActivation, OnOriginalWorldFrame, OnOriginalPublicationBarrier);
                    if (!_managedCapture.ConfirmHeadConsumed(lifetime, exact))
                        return false;
                    if (result != AdmissionReason.ok && result != AdmissionReason.duplicate)
                        throw new AdmissionException(result);
                    if (!ObserveOriginalUnderOwner(current))
                        return false;
                }

                return ServiceCaptureRevocationUnderOwner(lifetime) && _managedCapture.Count(lifetime) == 0;
            }
            catch
            {
                ManagedOriginalChoreCapture.RequestRevoke(lifetime);
                _revokePending = true;
                // Same owner locks prevent concurrent reset here. Bind disposal to the
                // still-current lifetime instead of any later global session reference.
                if (_managedCapture.IsCurrent(lifetime) && _signed != null)
                    _signed.Dispose();
                throw;
            }
        }

        private void RetryAfterCapturedFifoUnderOwner(TrustedContext current)
        {
            RequireNativeDiplomacySection();
            if (!Monitor.IsEntered(_gate))
                throw new InvalidOperationException("Retry owner gate missing");
            var lifetime = Volatile.Read(ref _managedCapture.CurrentLifetime);
            if (!ServiceCaptureRevocationUnderOwner(lifetime))
                return;
            if (_managedCapture.Count(lifetime) != 0 && !ConsumeCapturedFifoUnderOwner(current, lifetime))
                return;
            if (!_managedCapture.IsCurrent(lifetime) || _managedCapture.Count(lifetime) != 0 || !ServiceCaptureRevocationUnderOwner(lifetime))
                return;
            if (_featureTicks >= _nextRetryTick && _signed != null)
            {
                _signed.RetryPending(QueueStrictSignedChore);
                _nextRetryTick = _featureTicks + 120;
            }
        }

        private void DrainOrderedChores(OrderedCall call)
        {
            if (!call.Outer || call.Owner != this || _captureOwner != this || !Object.ReferenceEquals(this, Volatile.Read(ref _orderedBridgeOwner)))
                return;
            var lifetime = call.CaptureLifetime;
            if (!_managedCapture.IsCurrent(lifetime))
                return;
            if (call.Epoch != Interlocked.Read(ref _mapEpoch) || call.Generation != Interlocked.Read(ref _sessionGeneration) || _captureOverflow)
                ManagedOriginalChoreCapture.RequestRevoke(lifetime);
            // Freeze native references only AFTER native return. Atomic append checks
            // the same lifetime INSIDE the FIFO lock, after any gate-only reset.
            // No session/controller read or disposal occurs ahead of _gate acquisition.
            for (int i = 0; i < _capturedCount; i++)
            {
                if (!_managedCapture.IsCurrent(lifetime) || Volatile.Read(ref lifetime.RevokeRequested) != 0)
                    break;
                var raw = _capturedChores[i];
                var body = new byte[raw.Length - 2];
                Buffer.BlockCopy(raw, 2, body, 0, body.Length);
                if (!_managedCapture.TryAppend(lifetime, call.Epoch, call.Generation, new Bytes(body)))
                    break;
            }

            bool entered = false;
            try
            {
                Monitor.TryEnter(_gate, 0, ref entered);
                if (!entered)
                    return;
                if (!ServiceCaptureRevocationUnderOwner(lifetime) || _signed == null || !_signed.Live || !_signed.Ready)
                    return;
                if (!OwnsOrderedEngineBridge())
                    throw new InvalidOperationException("Original capture hook owner changed");
                TrustedContext current;
                string key, why;
                if (!ReadTrustedNetworkContext(out current, out key, out why) || key != _trustedKey)
                    throw new InvalidDataException("Original managed signed context changed");
                if (ConsumeCapturedFifoUnderOwner(current, lifetime))
                    DriveOriginalHostAndReceipts(current);
            }
            catch
            {
                ManagedOriginalChoreCapture.RequestRevoke(lifetime);
                if (_managedCapture.IsCurrent(lifetime) && _signed != null)
                {
                    _revokePending = true;
                    _signed.Dispose();
                }

                throw;
            }
            finally
            {
                if (entered)
                    Monitor.Exit(_gate);
            }
        }

        // Runs in MANAGED engine entry, before calling the original native code.
        // Extend the same recursive monitor through the managed postfix. No feature
        // gate is blocked on by the engine thread; managed drain uses TryEnter(0).
        private static void BeginOrderedEngineCall(
            MethodBase __originalMethod, out OrderedCall __state)
        {
            __state = default(OrderedCall);
            var owner = Volatile.Read(ref _orderedBridgeOwner);
            var monitor = Volatile.Read(ref _nativeInventoryMonitor);
            if (owner == null || monitor == null)
                return;
            Monitor.Enter(monitor);
            __state.Monitor = monitor;
            __state.Entered = true;
            __state.Owner = owner;
            __state.Epoch = Interlocked.Read(ref owner._mapEpoch);
            __state.Generation = Interlocked.Read(ref owner._sessionGeneration);
            __state.Outer = _engineCallDepth++ == 0;
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
            __state.OriginalMilitaryRun = __state.Outer &&
                owner._originalMilitaryRunVerified &&
                __originalMethod == owner._headlessRunTarget;
            if (__state.OriginalMilitaryRun)
                __state.MilitaryTickBefore = owner.ReadSimulationTick();
#endif
            __state.CaptureLifetime = !__state.Outer && Object.ReferenceEquals(_captureOwner, owner) ? _captureLifetime : Volatile.Read(ref owner._managedCapture.CurrentLifetime);
            if (__state.Outer)
            {
                _captureLifetime = __state.CaptureLifetime;
                if (_capturedChores == null)
                    _capturedChores = new byte[32][];
                _capturedCount = 0;
                _captureOverflow = false;
                _captureOwner = owner;
                _captureActive = true;
            }
        }

        // Native detour callback: bounded reference writes ONLY. No parsing,
        // allocation, locks, cryptography, logging, game calls or thrown exceptions.
        // Suppress the official MessagePack decoder for our already framed bytes.
        private static bool CaptureStrictChorePrefix(byte[] __0)
        {
            if (__0 == null || __0.Length < 2 || (__0[0] | (__0[1] << 8)) != SignedPacketId)
                return true;
            if (_captureActive && _capturedChores != null && _captureLifetime != null)
            {
                if (__0.Length > 1200 || __0.Length < 3 || _capturedCount >= _capturedChores.Length)
                    _captureOverflow = true;
                else
                    _capturedChores[_capturedCount++] = __0;
            }
            else
            {
                // Still suppress foreign MessagePack decode. No allocation, parsing,
                // lock, logging, game API, session disposal or fallback in this branch.
                // A single atomic marker is consumed by the existing managed owner.
                var owner = Volatile.Read(ref _orderedBridgeOwner);
                var lifetime = owner == null ? null : Volatile.Read(ref owner._managedCapture.CurrentLifetime);
                if (lifetime != null)
                    Interlocked.Exchange(ref lifetime.RevokeRequested, 1);
                else
                    Interlocked.Exchange(ref _unownedCaptureFault, 1);
            }

            return false;
        }

        private static void EndOrderedEngineCall(OrderedCall __state)
        {
            if (!__state.Entered || !__state.Outer)
                return;
            _captureActive = false;
            try
            {
                __state.Owner.DrainOrderedChores(__state);
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
                if (__state.OriginalMilitaryRun)
                    __state.Owner.ServiceAutonomousMilitaryAtOriginalTick(__state);
#endif
            }
            catch (Exception e)
            {
                ManagedOriginalChoreCapture.RequestRevoke(__state.CaptureLifetime);
                __state.Owner.ProtocolNote("status=ordered_stream_revoke_requested|reason=" + e.GetType().Name);
            }
        }

        private static Exception FinishOrderedEngineCall(Exception __exception, OrderedCall __state)
        {
            if (!__state.Entered)
                return __exception;
            try
            {
                if (__exception != null)
                    ManagedOriginalChoreCapture.RequestRevoke(__state.CaptureLifetime);
                if (__state.Outer)
                {
                    _captureActive = false;
                    if (_capturedChores != null)
                        Array.Clear(_capturedChores, 0, _capturedCount);
                    _capturedCount = 0;
                    _captureOwner = null;
                    _captureLifetime = null;
                }
            }
            finally
            {
                _engineCallDepth--;
                Monitor.Exit(__state.Monitor);
            }

            return __exception;
        }

#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
        // The earliest existing EngineInterface.run registration, shared with
        // the ordered bridge. Retains the RunBegin/RunEnd human-order cleanup
        // without a later second Patch.
        private static void BeginCombinedRun(
            MethodBase __originalMethod, out OrderedCall __state)
        {
            RunBegin();
            BeginOrderedEngineCall(__originalMethod, out __state);
        }

        private static Exception FinishCombinedRun(
            Exception __exception, OrderedCall __state)
        {
            try { return FinishOrderedEngineCall(__exception, __state); }
            finally { RunEnd(__exception); }
        }
#endif

        private void InstallSignedEngineBridge()
        {
            if (_signedBridgeInstalled)
                return;
            string why;
            // Private exact-process runtime validation includes the original managed
            // ReceiveChore/run/Save and rawra strict queue and dispatch method bodies.
            if (!DisposableGameScope.IsEnabled(out why))
                return;
            try
            {
                Type engine = typeof(MainViewModel).Assembly.GetType("EngineInterface", true);
                _headlessRunTarget = engine.GetMethod("run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                _receiveTarget = engine.GetMethod("ReceiveChore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int), typeof(byte[]), typeof(int) }, null);
                _dispatchTarget = typeof(GameNetworkAPI).GetMethod("DispatchReceivedScriptExtenderPayload", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]) }, null);
                _strictQueueTarget = typeof(GameNetworkAPI).GetMethod("SendScriptExtenderChorePayload", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]) }, null);
                if (_headlessRunTarget == null || _receiveTarget == null || _dispatchTarget == null || _strictQueueTarget == null || _strictQueueTarget.ReturnType != typeof(bool))
                    throw new MissingMethodException("Pinned ordered engine boundary unavailable");
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
                // DisposableGameScope already pins the untouched PE and
                // canonical loaded Mono IL. A foreign transpiler could
                // change the run body after that verification.
                var existingRunPatches = Harmony.GetPatchInfo(_headlessRunTarget);
                _originalMilitaryRunVerified = PrivateMilitarySupportRequested &&
                    _headlessRunTarget.ReturnType == typeof(int) &&
                    CanonicalManagedIL.Digest(_headlessRunTarget) == VerifiedManagedSurface.OriginalRunDigest &&
                    (existingRunPatches == null || existingRunPatches.Transpilers.Count == 0);
                if (PrivateMilitarySupportRequested)
                    Log("autonomous_military", "stage=managed_run_contract|verified=" +
                        BoolText(_originalMilitaryRunVerified));
#endif
                var type = typeof(DiplomacyFeature);
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                var prefix = new HarmonyMethod(type.GetMethod("BeginOrderedEngineCall", flags));
                var postfix = new HarmonyMethod(type.GetMethod("EndOrderedEngineCall", flags));
                var finalizer = new HarmonyMethod(type.GetMethod("FinishOrderedEngineCall", flags));
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                OriginalPreWorkerRegistration.RegisterFirstRunPatch(
                    _harmony, _headlessRunTarget, prefix, postfix, finalizer,
                    new HarmonyMethod(type.GetMethod("BeginCombinedRun", flags)),
                    new HarmonyMethod(type.GetMethod("FinishCombinedRun", flags)));
#else
                _harmony.Patch(_headlessRunTarget, prefix: prefix, postfix: postfix, finalizer: finalizer);
#endif
                _harmony.Patch(_receiveTarget, prefix: prefix, postfix: postfix, finalizer: finalizer);
                _harmony.Patch(_dispatchTarget, prefix: new HarmonyMethod(type.GetMethod("CaptureStrictChorePrefix", flags)));
                _signedBridgeInstalled = true;
                Volatile.Write(ref _orderedBridgeOwner, this);
                ProtocolNote("status=private_ordered_engine_adapter_installed");
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                // Refusal diagnostics are passive; they never grant native work.
                ProtocolNote("status=original_run_preworker_window|result=" +
                    OriginalPreWorkerRegistration.LastFinding);
#endif
            }
            catch (Exception e)
            {
                UninstallSignedEngineBridge();
                ProtocolNote("status=ordered_adapter_unavailable|reason=" + e.GetType().Name);
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                // A partially installed native entry is never safe to undo,
                // and the feature must not proceed to another run patch.
                if (OriginalPreWorkerRegistration.MustRetainRunPatch)
                    throw;
#endif
            }
        }

        private void UninstallSignedEngineBridge()
        {
            if (Object.ReferenceEquals(Volatile.Read(ref _orderedBridgeOwner), this))
                Interlocked.CompareExchange(ref _orderedBridgeOwner, null, this);
            ResetSignedSession();
            _signedBridgeInstalled = false;
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
            if (OriginalPreWorkerRegistration.MustRetainRunPatch)
                OriginalPreWorkerRegistration.DisableAndDetach(this, _gate);
#endif
            var type = typeof(DiplomacyFeature);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var target in new[]
            {
                _headlessRunTarget,
                _receiveTarget
            }

            )
                if (target != null && _harmony != null)
                    foreach (string name in new[]
                    {
                        "BeginOrderedEngineCall",
                        "EndOrderedEngineCall",
                        "FinishOrderedEngineCall"
                    }

                    )
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY && ORIGINAL_POSTTICK_INSTALL_EXPLICITLY_AUTHORIZED
                        if (!Object.ReferenceEquals(target, _headlessRunTarget) ||
                            !OriginalPreWorkerRegistration.MustRetainRunPatch)
#endif
                            _harmony.Unpatch(target, type.GetMethod(name, flags));
            if (_dispatchTarget != null && _harmony != null)
                _harmony.Unpatch(_dispatchTarget, type.GetMethod("CaptureStrictChorePrefix", flags));
            _headlessRunTarget = null;
            _receiveTarget = null;
            _dispatchTarget = null;
            _strictQueueTarget = null;
        }
    }
}
