// original singleplayer saves synthesize MPGameMember objects.
// The engine's player/computer registers and native local slot own the identity.
using System;
using System.Collections.Generic;
using System.Linq;
using CrusaderDE;
using SHCDESE.API;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class SessionMember
    {
        internal int Slot;
        internal bool Self, RealIdentity;
    }

    internal static class SoloSessionPolicy
    {
        internal static bool Allows(int local, int snapshotLocal, bool[] humans, bool[] ais, bool[] nativeAis, SessionMember[] members, bool networked, out string reason)
        {
            reason = "session_registers_unavailable";
            if (local < 1 || local > 8 || local != snapshotLocal || humans == null || ais == null || nativeAis == null || humans.Length != 9 || ais.Length != 9 || nativeAis.Length != 9)
                return false;
            int humanCount = 0;
            for (int id = 1; id <= 8; id++)
            {
                if (humans[id] && ais[id])
                {
                    reason = "ambiguous_player_register:" + id;
                    return false;
                }

                if (humans[id])
                {
                    humanCount++;
                    if (id != local)
                    {
                        reason = "other_registered_human:" + id;
                        return false;
                    }
                }

                if ((humans[id] || ais[id]) && nativeAis[id] != ais[id])
                {
                    reason = "native_and_managed_ai_disagree:" + id;
                    return false;
                }
            }

            if (humanCount != 1 || !humans[local] || ais[local])
            {
                reason = "local_human_not_proven";
                return false;
            }

            if (networked)
            {
                if (members == null || members.Length == 0 || members.Length > 8)
                    return false;
                var slots = new HashSet<int>();
                foreach (var member in members)
                {
                    if (member == null || member.Slot < 1 || member.Slot > 8 || !slots.Add(member.Slot))
                    {
                        reason = "unresolved_or_duplicate_network_slot";
                        return false;
                    }

                    if (member.Self && member.Slot != local)
                    {
                        reason = "network_self_slot_mismatch";
                        return false;
                    }

                    if (member.RealIdentity && (!member.Self || member.Slot != local))
                    {
                        reason = "other_network_identity:" + member.Slot;
                        return false;
                    }
                }

                if (!slots.Contains(local))
                {
                    reason = "local_network_slot_absent";
                    return false;
                }
            }

            reason = "one_engine_local_human_no_remote_peer";
            return true;
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        internal static bool IsSoloEngineSession(out string reason)
        {
            reason = "session_not_ready";
            try
            {
                var game = GameData.Instance;
                var live = game == null ? null : game.lastGameState;
                if (game == null || live == null || game.SkirmishGameType != 0 || game.coopTrailID != 0 || MainViewModel.Instance == null || Director.instance == null)
                    return false;
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "session_engine_monitor_busy";
                    return false;
                }

                using (lease)
                {
                    var api = GamePlayerManagerAPI.Instance;
                    if (api == null)
                        return false;
                    int local = api.GetLocalPlayerId();
                    var humans = new bool[9];
                    var ais = new bool[9];
                    var nativeAis = new bool[9];
                    for (int id = 1; id <= 8; id++)
                    {
                        humans[id] = live.is_valid_player(id);
                        ais[id] = live.is_skirmish_player(id);
                        if (humans[id] || ais[id])
                            nativeAis[id] = api.IsAIPlayer(id);
                    }

                    if (!ais.Any(x => x))
                    {
                        reason = "no_live_ai_opponent";
                        return false;
                    }

                    bool networked = GameNetworkAPI.IsNetworkedEnvironment();
                    var players = networked ? GameNetworkAPI.GetPlayers() : null;
                    var members = players == null ? null : players.Select(p => p == null ? null : new SessionMember { Slot = p.playerID, Self = p.isSelf, RealIdentity = p.steamID > 1000UL }).ToArray();
                    return SoloSessionPolicy.Allows(local, game.playerID, humans, ais, nativeAis, members, networked, out reason);
                }
            }
            catch (Exception e)
            {
                reason = "session_probe_exception:" + e.GetType().Name;
                return false;
            }
        }
    }
}
