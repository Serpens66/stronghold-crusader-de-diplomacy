// authoritative human/AI slot classification for autonomous AI plans.
// Reject missing/ambiguous identities, never treat a remote human as an AI.
// No Steam IDs or player names are used or persisted in policy results.
using System;
using System.Linq;
using System.Collections.Generic;

namespace StrongholdEuropeDE.Runtime
{
    internal static class HumanRosterPolicy
    {
        internal static bool TryResolve(IEnumerable<int> activeSlots, int local, bool[] realHumans, bool[] skirmishAis, out byte[] humans, out string reason)
        {
            humans = null;
            reason = "roster_unavailable";
            if (activeSlots == null || realHumans == null || skirmishAis == null || realHumans.Length != 9 || skirmishAis.Length != 9 || local < 1 || local > 8)
                return false;
            var roster = activeSlots.ToArray();
            if (roster.Length < 2 || roster.Length > 8 || roster.Any(x => x < 1 || x > 8) || roster.Distinct().Count() != roster.Length || !roster.Contains(local))
            {
                reason = "invalid_live_roster";
                return false;
            }

            var confirmed = new List<byte>();
            foreach (var slot in roster.OrderBy(x => x))
            {
                bool human = realHumans[slot], ai = skirmishAis[slot];
                if (human == ai)
                {
                    reason = "unresolved_or_conflicting_role:" + slot;
                    return false;
                }

                if (human)
                    confirmed.Add((byte)slot);
            }

            if (!confirmed.Contains((byte)local))
            {
                reason = "local_not_confirmed_human";
                return false;
            }

            humans = confirmed.ToArray();
            reason = "all_human_slots_classified";
            return true;
        }
    }
}
