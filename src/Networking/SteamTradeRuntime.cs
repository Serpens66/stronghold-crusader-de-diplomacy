//  transport wiring uses the official 2.14.3 authenticated Steam packet
// sender, NOT the sender-less Chore 106 or Steam fallback. No native goods writes.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using System.Threading;
using SHCDESE.API;
using SHCDESE.API.Components.Network;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.Network;
using R3;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private const short SteamPacketId = 1901;
        private const string Official2143Sha256 = "5cb1c33be5463086505a17f5c48aaa357d733cfff7240e101c821f0e3c668498";
        private static bool AttestedOfficialSteamSenderApi()
        {
            try
            {
                string disposableReason;
                if (DisposableGameScope.IsEnabled(out disposableReason))
                    return true;
                var assembly = typeof(GameNetworkAPI).Assembly;
                if (assembly.GetName().Name != "SHCDESE" || assembly.GetName().Version != new Version(2, 14, 3, 0) || string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
                    return false;
                using (var source = File.OpenRead(assembly.Location))
                using (var sha = SHA256.Create())
                {
                    string digest = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                    return digest == Official2143Sha256;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Only the per-subscription transport queue accepts Steam callback work.
        // It is never serialized and never updates canonical lockstep state.
        private SteamIngress _steamInbox;
        private readonly TransportObserver _steamObserver = new TransportObserver();
        private int _steamRejected;
        private IDisposable _steamSubscriber;
        private R3PacketEventHook<byte[]> _steamHook;
        private static ulong SteamIdFromBoxed(object boxed)
        {
            if (boxed == null)
                return 0;
            var f = boxed.GetType().GetField("m_SteamID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null || f.FieldType != typeof(ulong))
                return 0;
            return (ulong)f.GetValue(boxed);
        }

        private void ReceiveAuthenticatedSteamPacket(SteamIngress ingress, ReceiveCustomPacketEventArgs<byte[]> e)
        {
            try
            {
                if (ingress == null || e == null || e.Packet == null)
                    return;
                // The sender is supplied by official rawra's Steam receive metadata.
                // Sender-less Chore traffic is not accepted on this path.
                ulong sender = e.SenderSteamId.HasValue ? e.SenderSteamId.Value.m_SteamID : 0;
                if (sender <= 1000)
                    return;
                long mapEpoch = Interlocked.Read(ref _mapEpoch);
                long generation = Interlocked.Read(ref _sessionGeneration);
                if (!ingress.TryEnqueue(e.Packet, sender, mapEpoch, generation))
                    Interlocked.Increment(ref _steamRejected);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _steamRejected);
            }
        }

        private void StartSteamTransport()
        {
            if (_steamSubscriber != null)
                return;
            try
            {
                // The active game must load the exact 2.14.3 sender-providing extension.
                if (!AttestedOfficialSteamSenderApi())
                {
                    Log("trade_admission", "status=steam_receive_off|cause=unverified_rawra_binary");
                    return;
                }

                if (!GameNetworkAPI.IsNetworkedEnvironment() || !GameNetworkAPI.IsMultiplayerGame() || OriginalSoloWarehouseCapacityReader.IsSoloHumanHostSkirmish() || !GameNetworkAPI.IsPlayerSlotMappingAvailable())
                    return;
                // Pin a fixed packet ID across clients. Public dynamic registration is
                // order-dependent and MUST NOT be used for authoritative game actions.
                var method = typeof(GameNetworkAPI).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo matched = null;
                foreach (var m in method)
                    if (m.Name == "GetPacketEventFor" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CustomNetworkPacketType))
                    {
                        if (matched != null)
                            throw new InvalidOperationException("ambiguous fixed packet API");
                        matched = m;
                    }

                if (matched == null)
                    throw new MissingMethodException("rawra fixed packet registry unavailable");
                var hook = matched.MakeGenericMethod(typeof(byte[])).Invoke(GameNetworkAPI.Instance, new object[] { (CustomNetworkPacketType)SteamPacketId }) as R3PacketEventHook<byte[]>;
                if (hook == null || hook.GetPacketId() != SteamPacketId)
                    throw new InvalidOperationException("rawra packet type/ID mismatch");
                var ingress = new SteamIngress(32);
                _steamInbox = ingress;
                _steamSubscriber = ObservableSubscribeExtensions.Subscribe(hook.GetBaseHook().Observable, e => ReceiveAuthenticatedSteamPacket(ingress, e));
                _steamHook = hook;
                Log("trade_transport", "status=steam_origin_metadata_observation_only|goods_release=false");
            }
            catch (Exception e)
            {
                var oldInbox = Interlocked.Exchange(ref _steamInbox, null);
                if (oldInbox != null)
                    oldInbox.Close();
                _steamHook = null;
                if (_steamSubscriber != null)
                {
                    _steamSubscriber.Dispose();
                    _steamSubscriber = null;
                }

                _steamObserver.Reset();
                Log("trade_transport", "status=steam_transport_unavailable|reason=" + Safe(e.GetType().Name));
            }
        }

        private void StopSteamTransport()
        {
            // Close FIRST: any callback already in flight still holds ONLY its own
            // closed ingress, never the newly registered subscription queue.
            var oldInbox = Interlocked.Exchange(ref _steamInbox, null);
            if (oldInbox != null)
                oldInbox.Close();
            if (_steamSubscriber != null)
            {
                _steamSubscriber.Dispose();
                _steamSubscriber = null;
            }

            _steamHook = null;
            _steamObserver.Reset();
        }

        private void ClearSteamInbox()
        {
            var inbox = Volatile.Read(ref _steamInbox);
            if (inbox != null)
                inbox.Clear();
            _steamObserver.Reset();
            ResetSignedSession();
        }

        private SteamDeliveryEvidence ReadSteamEvidence(ulong sender, TradeWireMessage m)
        {
            var x = new SteamDeliveryEvidence
            {
                AuthenticatedSenderSteamId = sender,
                NetworkEnvironment = GameNetworkAPI.IsNetworkedEnvironment(),
                Multiplayer = GameNetworkAPI.IsMultiplayerGame(),
                LocalHost = GameNetworkAPI.IsLocalHost(),
                LocalSlot = GameNetworkAPI.GetLocalPlayerId(),
                ExactLiveRoster = _roster.Contains(m.Requester) && _roster.Contains(m.Recipient) && _roster.Contains(m.HostSlot) && _mapActive && !_mapDisabled,
                ExactEngineSession = _mapActive && !_mapDisabled && !_mapBootstrapPending && _mailbox != null && _mapFingerprint == m.Map && _rosterFingerprint == m.Roster,
                // The original game SaveData has no combined native+mod durable commit witness.
                OriginalEngineSaveDurable = false,
                NativeFourCellsAttested = false
            };
            var getHost = typeof(GameNetworkAPI).GetMethod("GetHostSteamId", BindingFlags.Public | BindingFlags.Static);
            object boxedHost = getHost == null ? null : getHost.Invoke(null, null);
            x.AuthenticatedLobbyHostSteamId = SteamIdFromBoxed(boxedHost);
            if (!GameNetworkAPI.IsPlayerSlotMappingAvailable())
                return x;
            for (int id = 1; id <= 8; id++)
            {
                var player = GameNetworkAPI.GetPlayerById(id);
                if (player != null && !player.kicked && !player.skirmishAI && player.steamID > 1000UL)
                    x.SlotSteamIds[id] = player.steamID;
            }

            return x;
        }

        private void PumpAuthenticatedSteamPackets()
        {
            var inbox = Volatile.Read(ref _steamInbox);
            if (inbox == null || inbox.Count == 0)
                return;
            for (int count = 0; count < 16; count++)
            {
                InboundObservation eventData;
                if (!inbox.TryTake(out eventData))
                    break;
                if (!Object.ReferenceEquals(inbox, Volatile.Read(ref _steamInbox)))
                    return;
                try
                {
                    if (!DeliveryEpoch.Matches(eventData.MapEpoch, eventData.SessionGeneration, _mapEpoch, _sessionGeneration))
                        continue;
                    if (_steamHook == null || !_mapActive || _mapDisabled || !GameNetworkAPI.IsMultiplayerGame() || _mailbox == null)
                        continue;
                    var packet = TradeWireMessage.Decode(eventData.CopyWire());
                    var facts = ReadSteamEvidence(eventData.VerifiedSenderSteamId, packet);
                    string why;
                    ulong trustedTerm = _mailbox.SteamReplay.HostTerm;
                    if (trustedTerm == 0)
                    {
                        Log("trade_transport", "status=unbootstrapped_host_term_refused");
                        continue;
                    }

                    // Unknown packets must not advance the sender high-water, even with a
                    // valid unkeyed SHA256 and a genuine Steam sender.
                    var known = _archive == null ? null : _archive.Record(packet.QuoteId);
                    CanonicalQuote saved = known == null ? null : new CanonicalQuote
                    {
                        Id = known.Id,
                        RootId = known.RootId,
                        Requester = known.Requester,
                        Recipient = known.Recipient,
                        OfferedGood = known.OfferedGood,
                        RequestedGood = known.RequestedGood,
                        OfferedAmount = known.OfferedAmount,
                        RequestedAmount = known.RequestedAmount,
                        IssuedDay = known.IssuedDay,
                        ExpiresDay = known.ExpiryDay
                    };
                    // The actual mailbox replay ledger is READ-ONLY. Steam delivery order
                    // differs by peer; writing that ledger here would diverge saved games.
                    if (!_steamObserver.TryObserve(packet, saved, Math.Max(1, _lastProcessedDay), facts, _mapFingerprint, _rosterFingerprint, _mailbox.SteamReplay, out why))
                    {
                        Log("trade_transport", "status=steam_packet_denied|reason=" + Safe(why));
                        continue;
                    }

                    // Verified inbound identity and replay do not prove native goods or a
                    // matching original GameSave. No material action is authorized here.
                    Log("trade_ingress", "status=steam_packet_observed_only|id=" + packet.QuoteId + "|kind=" + packet.Kind + "|canonical_replay_unchanged=true|native_commit=not_authorized");
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref _steamRejected);
                    Log("trade_transport", "status=steam_packet_parse_refused|reason=" + Safe(e.GetType().Name));
                }
            }
        }

        private bool SendAuthenticatedSteamPacket(TradeWireMessage packet, int targetSlot)
        {
            if (packet == null || targetSlot < 1 || targetSlot > 8 || _steamHook == null || !_mapActive || _mapDisabled || !GameNetworkAPI.IsMultiplayerGame() || !GameNetworkAPI.IsPlayerSlotMappingAvailable() || GameNetworkAPI.GetLocalPlayerId() != packet.SenderSlot)
                return false;
            try
            {
                packet.Validate();
                if (_mailbox == null || _mailbox.SteamReplay.HostTerm == 0 || packet.HostTerm != _mailbox.SteamReplay.HostTerm || packet.Map != _mapFingerprint || packet.Roster != _rosterFingerprint)
                    return false;
                bool hostIssued = packet.Kind == WireKind.HostDecision || packet.Kind == WireKind.HostCommitReceipt || packet.Kind == WireKind.HostAbort || packet.Kind == WireKind.HostAutonomousOffer;
                if (hostIssued && !GameNetworkAPI.IsLocalHost())
                    return false;
                var recipient = GameNetworkAPI.GetPlayerById(targetSlot);
                if (recipient == null || recipient.kicked || recipient.skirmishAI || recipient.steamID <= 1000)
                    return false;
                // Do not transmit fictitious commits: only a verified native+SaveData
                // witness would authorize a HostCommitReceipt in a future engine version.
                if (packet.Kind == WireKind.HostCommitReceipt)
                    return false;
                GameNetworkAPI.SendPacketToPlayerId<byte[]>(targetSlot, TradeWireMessage.Encode(packet), SteamPacketId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
