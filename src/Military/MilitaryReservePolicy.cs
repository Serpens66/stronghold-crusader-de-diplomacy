// Shared pure castle-defence reserve calculation for planning and preparation.
// Combat scores are nonnegative Int32 observations. Long intermediates ensure
// the integer ceiling of 1.5 x reserve cannot overflow at Int32.MaxValue.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal static class MilitaryReservePolicy
    {
        internal static long RequiredAtHome(int castleDefenseReserve, int threatStrength)
        {
            if (castleDefenseReserve < 0)
                throw new ArgumentOutOfRangeException("castleDefenseReserve");
            if (threatStrength < 0)
                throw new ArgumentOutOfRangeException("threatStrength");

            long reserve = castleDefenseReserve;
            long threat = threatStrength;
            long halfAgainCeiling = (3L * reserve + 1L) / 2L;
            return Math.Max(halfAgainCeiling, reserve + threat);
        }
    }
}
