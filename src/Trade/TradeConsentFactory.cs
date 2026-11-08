// /capacity-free Format3 origin factory for ONE  controller.
// No CanonicalTradeSnapshot/StockCell is fabricated for multiplayer.
using System;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        private PendingConsentPublication _pendingConsentPublication;
        private static void CheckRoster(CanonicalModel m, OriginalMultiplayerConsentFrame observed)
        {
            if (m == null || observed == null || observed.NativePhysicalSettlementAuthorized || observed.NativeSharedTick < 1 || observed.NativeGameDay < 1 || m.Archive.MapFingerprint != observed.SharedMapFingerprint || m.Archive.RosterFingerprint != observed.SharedRosterFingerprint)
                throw new InvalidDataException("Unattested original MP scope");
            byte roster = 0, human = 0;
            var players = observed.Players();
            if (players.Length < 2 || players.Count(x => x.Human) < 2)
                throw new InvalidDataException("Not a complete human roster");
            foreach (var p in players)
            {
                roster |= Bit(p.Slot);
                if (p.Human)
                    human |= Bit(p.Slot);
                if (p.Human && p.SteamId <= 1000)
                    throw new InvalidDataException("No original human Steam ID");
            }

            if (m.RosterMask != roster || m.HumanMask != human)
                throw new InvalidDataException("Changed original human identities");
        }

        internal static CanonicalTradeReducer CreateOriginalMpConsentOnly(OriginalMultiplayerConsentFrame observed, Guid globalScope, ulong term)
        {
            if (observed == null || globalScope == Guid.Empty || term == 0)
                throw new ArgumentException("No common original match");
            byte roster = 0, human = 0;
            foreach (var p in observed.Players())
            {
                roster |= Bit(p.Slot);
                if (p.Human)
                    human |= Bit(p.Slot);
            }

            var m = new CanonicalModel
            {
                Revision = 1,
                LastSharedTick = 0,
                // Empty ledger genesis is deterministic, not a claimed world capture.
                // First signed original frame advances it to the ACTUAL original day/tick.
                Day = 1,
                MatchScope = globalScope,
                MatchTerm = term,
                RosterMask = roster,
                HumanMask = human,
                WorldSeed = Seed(globalScope, term, observed.SharedMapFingerprint, observed.SharedRosterFingerprint),
                Archive = new TradeHistoryArchive
                {
                    MapFingerprint = observed.SharedMapFingerprint,
                    RosterFingerprint = observed.SharedRosterFingerprint
                },
                Mailbox = new TradeIntentMailbox
                {
                    MapFingerprint = observed.SharedMapFingerprint,
                    RosterFingerprint = observed.SharedRosterFingerprint
                }
            };
            CheckRoster(m, observed);
            m.Mailbox.Rebase(m.LastSharedTick);
            var result = new CanonicalTradeReducer(m);
            result._mpNoNativeSession = true;
            return result;
        }

        internal static CanonicalTradeReducer RestoreOriginalMpConsentOnly(byte[] saved, OriginalMultiplayerConsentFrame original, Guid globalScope, ulong term)
        {
            CanonicalModel m = CanonicalTradeCodec.Decode(saved);
            CheckRoster(m, original);
            if (m.MatchScope != globalScope || m.MatchTerm != term || m.Day > original.NativeGameDay || m.Quarantined || m.Archive.PendingNativeId != 0 || m.Mailbox.Intents.Any(x => x.State == TradeIntentStatus.Processing) || m.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative))
                throw new InvalidDataException("Ambiguous/foreign original MP save");
            if (original.NativeSharedTick < m.LastSharedTick)
                throw new InvalidDataException("Original loaded native clock predates saved consent");
            // Preserve serialized highwaters/revision exactly. Local rejoin timing
            // must never enter common bytes or change mailbox elapsed-time arithmetic.
            m.Mailbox.RestoreAfterLoad(m.LastSharedTick);
            var result = new CanonicalTradeReducer(m);
            result._mpNoNativeSession = true;
            return result;
        }
    }
}
