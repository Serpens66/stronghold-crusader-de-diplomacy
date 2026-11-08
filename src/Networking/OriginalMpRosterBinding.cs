// Pure check of the already existing  TrustedContext against the
// independently recaptured original game slot/Steam mapping. This does
// not trust Steam IDs embedded in human action bodies or the UI.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalMpRosterBinding
    {
        internal static void VerifyExactPinnedOriginal(CanonicalTradeSnapshot world, TrustedContext actualExistingContext, ulong[] newlyObservedOriginalSteamBySlot, byte originalNativeLocalHumanSlot)
        {
            if (world == null || actualExistingContext == null || actualExistingContext.Generation < 1 || newlyObservedOriginalSteamBySlot == null || newlyObservedOriginalSteamBySlot.Length != 9 || newlyObservedOriginalSteamBySlot[0] != 0)
                throw new InvalidDataException("Missing original pinned human roster");
            CanonicalPlayer[] players = world.Players();
            var human = players.Where(p => p.Human).ToArray();
            if (world.MapGeneration < 1 || world.LocalOfflineHumanSlot != 0 || human.Length < 2 || human.Any(p => p.SteamId <= 1000) || human.Select(p => p.SteamId).Distinct().Count() != human.Length)
                throw new InvalidDataException("Not a genuine multi-human world");
            CanonicalPlayer nativeLocal = world.Player(originalNativeLocalHumanSlot);
            if (originalNativeLocalHumanSlot < 1 || originalNativeLocalHumanSlot > 8 || nativeLocal == null || !nativeLocal.Human || nativeLocal.SteamId != actualExistingContext.LocalSteamId)
                throw new InvalidDataException("Original engine local human slot differs from Steam context");
            var registered = human.Select(p => p.SteamId).OrderBy(x => x).ToArray();
            var signed = actualExistingContext.HumanIds();
            if (!registered.SequenceEqual(signed) || !registered.Contains(actualExistingContext.HostSteamId) || !registered.Contains(actualExistingContext.LocalSteamId))
                throw new InvalidDataException("Signed roster no longer matches game");
            for (int slot = 1; slot <= 8; slot++)
            {
                CanonicalPlayer player = world.Player((byte)slot);
                ulong actual = newlyObservedOriginalSteamBySlot[slot];
                if (player == null || !player.Human)
                {
                    if (actual != 0)
                        throw new InvalidDataException("Unregistered or AI slot owns Steam ID");
                }
                else if (actual != player.SteamId || !actualExistingContext.Contains(actual))
                    throw new InvalidDataException("Original Steam slot changed");
            }
        }
    }
}
