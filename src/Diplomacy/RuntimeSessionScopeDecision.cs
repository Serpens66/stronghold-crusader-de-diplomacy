using System.Collections.Generic;

namespace StrongholdEuropeDE.Runtime
{
    // Pure snapshot of the ORIGINAL managed GameNetworkAPI roster and actual
    // native PlayState role bits. One member can be a synthesized MPGameMember
    // in a locally loaded solo skirmish. No Steam ID here grants MP authority.
    internal sealed class ObservedSkirmishMember
    {
        internal int Slot;
        internal bool Self, Kicked, SkirmishAi;
        internal ulong SteamId;
    }
    /// <summary>
    /// Decides whether process-local gameplay mutations are safe for the
    /// current session shape. Crusader DE represents a locally-created
    /// skirmish as a network session too, so the network flag alone cannot
    /// distinguish it from a real multiplayer match.
    /// </summary>
    internal static class RuntimeSessionScopeDecision
    {
        internal static bool AllowsLocalMutation(bool networked, bool localHost, int otherHumanPlayers)
        {
            // A locally loaded SP skirmish can report networked=true and
            // localHost=false. Only verified ORIGINAL human/native roles can
            // distinguish it from actual multiplayer. With networked=true,
            // missing or uncertain network membership must never activate
            // singleplayer gameplay mutation. Offline (-1 unknown) is
            // still accepted only in conjunction with original game/map
            // guards and the later exact 1-human native-roster validation.
            return networked ? otherHumanPlayers == 0 : otherHumanPlayers <= 0;
        }

        internal static bool IsOrdinarySkirmishPath(int skirmishGameType,
            int cooperativeTrailId)
        {
            // SoloSessionPolicy, native capacity and military original routes
            // already require exactly game type 0 and coop trail 0. Treat
            // previously unclassified values (3+, negative) as unknown,
            // not as a new permitted gameplay category.
            return skirmishGameType == 0 && cooperativeTrailId == 0;
        }

        // Called once at map bootstrap, not on a native callback.
        // The native/managed source of truth for EACH slot is PlayState's
        // player_register versus is_skirmish_player, not networked=true or
        // steamID>1000 by itself. Return -1 if the roster is incomplete,
        // contradictory or not safely classifiable.
        internal static int CountOtherOriginalHumanPlayers(
            int local, bool[] originalHumans, bool[] originalAis,
            IList<ObservedSkirmishMember> members, bool actualLobbyActive,
            out string reason)
        {
            reason = "original_network_roster_unavailable";
            // GameNetworkAPI.IsLobbyEnvironment() observes the ACTUAL
            // Platform_Multiplayer.activeLobby pointer (verified official
            // 2.14.3 method). An active real MP lobby cannot be asserted
            // to be a standalone SP skirmish, even if its peer has no
            // assigned original PlayState human slot yet.
            if (actualLobbyActive)
            {
                reason = "actual_multiplayer_lobby_active";
                return -1;
            }
            if (local < 1 || local > 8 || originalHumans == null ||
                originalAis == null || originalHumans.Length != 9 ||
                originalAis.Length != 9 || members == null ||
                members.Count == 0 || members.Count > 8 ||
                !originalHumans[local] || originalAis[local])
                return -1;

            var seen = new bool[9];
            int localSelf = 0, others = 0;
            foreach (ObservedSkirmishMember member in members)
            {
                if (member == null)
                {
                    reason = "network_member_null";
                    return -1;
                }
                if (member.Kicked)
                    continue;
                int slot = member.Slot;
                if (slot < 1 || slot > 8 || seen[slot] ||
                    originalHumans[slot] && originalAis[slot])
                {
                    reason = "network_member_invalid_or_duplicate";
                    return -1;
                }
                seen[slot] = true;
                if (slot == local)
                {
                    // A reloaded SP save can reconstruct MPGameMember without
                    // isSelf, while its ORIGINAL PlayState still proves the
                    // one local human and no actual Lobby remains. Do not
                    // reject that valid native identity for a presentation bit.
                    if (member.SkirmishAi || !originalHumans[slot])
                    {
                        reason = "network_local_human_mismatch";
                        return -1;
                    }
                    ++localSelf;
                }
                else
                {
                    if (member.Self)
                    {
                        reason = "network_other_slot_claimed_self";
                        return -1;
                    }
                    if (originalHumans[slot] && !originalAis[slot])
                    {
                        if (member.SkirmishAi)
                        {
                            reason = "network_remote_human_mislabeled_as_ai";
                            return -1;
                        }
                        // Even an unverified/zero SteamId belongs to a
                        // potential OTHER original human. Never call it solo.
                        ++others;
                    }
                    else if (!originalAis[slot] || originalHumans[slot])
                    {
                        // GetNumActivePlayers() counts all non-kicked
                        // gameMembers, and a saved *solo* can have eight
                        // network members although PlayState only contains
                        // 1 human + 2 AI. Slots without ANY original role
                        // are empty synthesized entries, not actual humans,
                        // ONLY when there is no real active lobby and no
                        // established Steam identity on that slot.
                        if (!originalHumans[slot] && !originalAis[slot] &&
                            member.SteamId <= 1000UL)
                            continue;
                        // A non-skirmish member with a real Steam identity
                        // requires the separate authenticated MP owner path,
                        // never the broad singleplayer map.
                        if (!originalHumans[slot] && !originalAis[slot] &&
                            member.SteamId > 1000UL &&
                            !member.SkirmishAi)
                        {
                            ++others;
                            continue;
                        }
                        reason = "network_other_slot_role_unresolved";
                        return -1;
                    }
                    else
                    {
                        // A remote joining peer may already have a true Steam
                        // identity even while the original PlayState still
                        // retains an AI slot from the prior map. That is
                        // inconsistent with a safe one-human solo session:
                        // route to the separately authenticated MP owner.
                        if (member.SteamId > 1000UL && !member.SkirmishAi)
                            ++others;
                        // A genuine native AI may have a synthetic multiplayer
                        // member lacking skirmishAI after a solo cold load,
                        // but no real other Steam identity.
                    }
                }
            }
            if (localSelf != 1)
            {
                reason = "network_local_self_absent";
                return -1;
            }
            for (int slot = 1; slot <= 8; ++slot)
                if (originalHumans[slot] &&
                    (!seen[slot] || originalAis[slot]))
                {
                    reason = "original_human_missing_from_network_roster";
                    return -1;
                }

            reason = others == 0 ?
                "one_original_local_human_no_remote_peer" :
                "actual_remote_original_human_present";
            return others;
        }

        internal static bool AllowsDiplomacyMap(bool networked, bool localHost, int otherHumanPlayers, bool hasGameData, bool hasLastGameState, bool multiplayerMap, bool hasDirector, bool directorMultiplayerGame, bool hasMainViewModel, bool skirmishModeVisible)
        {
            if (!AllowsLocalMutation(networked, localHost, otherHumanPlayers) || !hasGameData || !hasLastGameState || !hasDirector || !hasMainViewModel || !skirmishModeVisible)
            {
                return false;
            }

            // In a solo skirmish these two engine flags are true even though
            // no second human can observe or desynchronise the local
            // mutations. Offline maps keep the stricter legacy classification
            // so campaigns and non-skirmish maps remain out.
            return networked || (!multiplayerMap && !directorMultiplayerGame);
        }

        internal static string Status(bool networked, bool localHost, int otherHumanPlayers)
        {
            if (!networked)
            {
                return otherHumanPlayers > 0 ? "indeterminate" : "offline";
            }
            if (otherHumanPlayers < 0)
                return "indeterminate";

            if (localHost && otherHumanPlayers == 0)
            {
                return "solo_host";
            }

            if (otherHumanPlayers == 0)
            {
                return "solo_loaded";
            }

            if (!localHost)
            {
                return "network_client";
            }

            if (otherHumanPlayers > 0)
            {
                return "multiplayer";
            }

            return "indeterminate";
        }
    }
}
