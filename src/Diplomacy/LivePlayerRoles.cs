// fail-closed live role evidence from the original engine registers.
// Human intent may not be answered by the AI quote engine automatically.
// Read-only: no native inventory mutation, Steam message or permission grant.
using System;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private bool TryReadHumanSlots(out byte[] humanSlots, out string reason)
        {
            humanSlots = null;
            reason = "player_state_not_ready";
            if (!_mapActive || _mapDisabled || _mapBootstrapPending || _roster == null || _localPlayer < 1 || _localPlayer > 8)
                return false;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
            {
                reason = "engine_monitor_busy";
                return false;
            }

            using (lease)
            {
                var game = GameData.Instance;
                var state = game == null ? null : game.lastGameState;
                if (state == null)
                    return false;
                var api = SHCDESE.API.GamePlayerManagerAPI.Instance;
                if (api == null || api.GetLocalPlayerId() != _localPlayer || game.playerID != _localPlayer)
                {
                    reason = "native_local_role_disagrees";
                    return false;
                }

                var active = _roster.Distinct().OrderBy(x => x).ToArray();
                var humans = new bool[9];
                var ais = new bool[9];
                foreach (int id in active)
                {
                    if (id < 1 || id > 8)
                    {
                        reason = "invalid_original_player_slot";
                        return false;
                    }

                    humans[id] = state.is_valid_player(id);
                    ais[id] = state.is_skirmish_player(id);
                    if ((humans[id] || ais[id]) && api.IsAIPlayer(id) != ais[id])
                    {
                        reason = "native_and_snapshot_role_disagree";
                        return false;
                    }
                }

                return HumanRosterPolicy.TryResolve(active, _localPlayer, humans, ais, out humanSlots, out reason);
            }
        }

        private bool MayAutoAnswerTrade(byte recipient, out string reason)
        {
            reason = "recipient_role_not_authoritative";
            byte[] humans;
            if (!TryReadHumanSlots(out humans, out reason))
                return false;
            if (recipient < 1 || recipient > 8 || !_roster.Contains(recipient))
            {
                reason = "recipient_not_in_live_game";
                return false;
            }

            if (humans.Contains(recipient))
            {
                reason = "human_recipient_requires_explicit_consent";
                return false;
            }

            // TryReadHumanSlots requires EVERY other role in _roster be an
            // original-register AI. No unclassified network member is auto-answered.
            reason = "confirmed_engine_ai_recipient";
            return true;
        }
    }
}
