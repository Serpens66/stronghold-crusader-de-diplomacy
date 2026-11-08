// persistent, day-bounded vassal-pressure observations.
// Simulation-data modelling only. No unit orders, goods, native or SaveData writes.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal static class RebellionObservation
    {
        internal static void Observe(TradeRebellionMemory m, int day, bool strong, bool poor)
        {
            if (m == null)
                throw new ArgumentNullException("memory");
            if (day < 1 || m.LastDay < -1 || m.StrengthDays < 0 || m.PoorDays < 0 || m.StrengthDays > 100000 || m.PoorDays > 100000)
                throw new InvalidOperationException("Invalid rebellion observation chronology");
            if (day < m.LastDay)
            {
                // A reversed native day may be a load or rebase. A prior run cannot
                // be counted as current consecutive evidence in an earlier world.
                m.StrengthDays = 0;
                m.PoorDays = 0;
                m.AtRisk = false;
                m.LastDay = day;
                return;
            }

            if (day > m.LastDay)
            {
                bool consecutive = (long)day - m.LastDay == 1;
                m.StrengthDays = strong ? (consecutive ? Math.Min(100000, m.StrengthDays + 1) : 1) : 0;
                m.PoorDays = poor ? (consecutive ? Math.Min(100000, m.PoorDays + 1) : 1) : 0;
                m.LastDay = day;
                return;
            }

            // Observations can change *within* one game day. Any negative sample
            // breaks that category's purported five uninterrupted daily samples.
            // A later positive sample on the SAME day must not restore the lost
            // earlier days or add another day of evidence.
            if (!strong)
                m.StrengthDays = 0;
            if (!poor)
                m.PoorDays = 0;
        }
    }
}
