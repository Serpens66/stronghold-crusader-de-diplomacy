// deterministic, bounded, evidence-based AI reply timing.
// Pure algorithm; scheduled under the existing saved  simulation clock.
// No native goods, Steam transport, wall time, Unity frames or save authority.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ReplyContext
    {
        internal ulong Id;
        internal int Day, Requester, Recipient, Action, RequestedAmount;
        internal int Trust, Grudge, Threat, Urgency, Caution, Commerce, PendingNegotiations;
        internal bool RecipientAtWar;
    }

    internal static class NegotiationTiming
    {
        internal const int MinimumTicks = 32, MaximumTicks = 210;
        private static bool Percentage(int value)
        {
            return value >= 0 && value <= 100;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        // A fixed-width cross-platform mixer. Never depend on string hash,
        // Collection enumeration order or the wall clock.
        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }

        internal static int Delay(ReplyContext c)
        {
            if (c == null || c.Id == 0 || c.Id >= ulong.MaxValue - 8 || c.Day < 1 || c.Requester < 1 || c.Requester > 8 || c.Recipient < 1 || c.Recipient > 8 || c.Requester == c.Recipient || c.Action < 0 || c.Action > 3 || c.RequestedAmount < 1 || c.RequestedAmount > 100000 || !Percentage(c.Trust) || !Percentage(c.Grudge) || !Percentage(c.Threat) || !Percentage(c.Urgency) || !Percentage(c.Caution) || !Percentage(c.Commerce) || c.PendingNegotiations < 0 || c.PendingNegotiations > 28)
                throw new ArgumentException("Untrusted trade response timing context");
            ulong seed = unchecked(c.Id ^ ((ulong)(uint)c.Day << 32) ^ ((ulong)(uint)c.Requester << 24) ^ ((ulong)(uint)c.Recipient << 16) ^ 0x565f3131355f4149UL);
            int jitter = (int)(Mix(seed) % 37UL) - 18;
            int delay = 86 + jitter;
            // Careful, slow or overburdened rulers answer later.
            delay += (c.Caution - c.Commerce) / 3;
            delay += (50 - c.Trust) / 6;
            delay += c.Grudge / 10;
            delay += c.PendingNegotiations * 2;
            // An observed emergency merits an earlier answer, not a
            // fabricated stock entitlement or automatic consent.
            delay -= c.Urgency / 4;
            if (c.RecipientAtWar)
                delay += 9;
            if (c.RequestedAmount >= 500)
                delay += 12;
            else if (c.RequestedAmount >= 100)
                delay += 5;
            if (c.Action == 1)
                delay -= 16; // answer an offered counter
            else if (c.Action == 2)
                delay -= 20; // rejection should not stall
            else if (c.Action == 3)
                delay += 8; // independent AI re-evaluation
            return Clamp(delay, MinimumTicks, MaximumTicks);
        }
    }
}
