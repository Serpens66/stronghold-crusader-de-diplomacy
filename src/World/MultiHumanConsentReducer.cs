//  Canonical multiplayer reduction with independently verified publication.
// Additive method on the existing ONE Format3 CanonicalTradeReducer.
// Safe MP admission/consent + AI-human offer model; deliberately NO
// ProcessDue, PrepareAcceptedHuman, / journal or native goods port.
// This code does not register any hook, SaveData callback or network packet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    // The exclusive coordinator owns the actual EngineInterface.threadLock
    // and constructs this witness AFTER its original post-native Chore return.
    // No caller may substitute UI/R3/Steam-derived tick/slot evidence.
    internal interface IObservedOriginalConsentFrame : ICanonicalTradeSnapshotSource
    {
        bool HoldsOriginalEngineMonitor { get; }

        bool VerifiedPostNativeOrderedChoreBoundary { get; }

        long OriginalMapGeneration { get; }

        long OriginalSharedSimulationTick { get; }

        TrustedContext PinnedOriginalNetworkContext { get; }

        ulong[] CurrentOriginalSteamBySlot { get; }

        byte CurrentOriginalLocalHumanSlot { get; }
    }

    internal sealed partial class CanonicalTradeReducer
    {
        // This is an ALTERNATIVE to AdvanceSharedTick, not a wrapper around it.
        // The coordinator must call exactly one variant per shared tick.
        // Do not attach the regular CanonicalTradeRuntimeSettlement to this.
        private byte[] AdvanceVerifiedMpWithoutNative(AuthenticatedTradeTick engineOrderedInput, IObservedOriginalConsentFrame originalFrame)
        {
            if (engineOrderedInput == null || originalFrame == null)
                throw new ArgumentNullException("MP original frame");
            if (!originalFrame.HoldsOriginalEngineMonitor || !originalFrame.VerifiedPostNativeOrderedChoreBoundary || originalFrame.OriginalMapGeneration < 1 || originalFrame.OriginalSharedSimulationTick < 1)
                throw new InvalidOperationException("MP reduction requires the original managed engine/Chore owner");
            if (_pendingWorldPublication != null || _prepared != null || _model.Quarantined || _model.Archive.PendingNativeId != 0 || _model.Mailbox.Intents.Any(i => i.State == TradeIntentStatus.Processing) || _model.Archive.Records.Any(r => r.State == TradeDisposition.PreparingNative))
                throw new InvalidOperationException("Unresolved native transaction forbids MP no-native reduction");
            long tick = originalFrame.OriginalSharedSimulationTick;
            CanonicalTradeSnapshot world = originalFrame.Capture(tick);
            if (world == null || world.SharedTick != tick || world.MapGeneration != originalFrame.OriginalMapGeneration || world.LocalOfflineHumanSlot != 0)
                throw new InvalidDataException("No genuine original MP frame");
            CanonicalPlayer[] players = world.Players();
            var humans = players.Where(p => p.Human).ToArray();
            if (humans.Length < 2 || humans.Any(p => p.SteamId <= 1000) || humans.Select(p => p.SteamId).Distinct().Count() != humans.Length || players.Any(p => !p.Human && p.SteamId != 0))
                throw new InvalidDataException("Genuine complete multi-human Steam roster is required");
            OriginalMpRosterBinding.VerifyExactPinnedOriginal(world, originalFrame.PinnedOriginalNetworkContext, originalFrame.CurrentOriginalSteamBySlot, originalFrame.CurrentOriginalLocalHumanSlot);
            VerifyWorld(_model, world, true);
            // Only the one original post-Chore authenticated identity can
            // produce a TrustedHumanInput. No unsigned input constructor is
            // accepted here, including synthetic local-solo provenance.
            OrderedHumanTrade[] actions = engineOrderedInput.Requests();
            var admitted = new CanonicalTrustedHumanInput[actions.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                admitted[i] = engineOrderedInput.TrustedAt(i);
                if (admitted[i] == null || admitted[i].EngineVerifiedSoloLocal || !VerifiedHumanActor(world, actions[i], admitted[i]))
                    throw new InvalidDataException("Unsigned or falsely attributed multiplayer actor");
            }

            var next = _model.Copy();
            next.LastSharedTick = tick;
            next.Mailbox.Observe(tick);
            next.Revision = checked(next.Revision + 1);
            AdvanceDay(next, world.Day);
            if (engineOrderedInput.Activation != null && (next.InputEpoch >= ulong.MaxValue - 1 || engineOrderedInput.Activation.Epoch != next.InputEpoch + 1))
                throw new InvalidDataException("Signed MP session activation must increment persisted epoch exactly once");
            Activate(next, engineOrderedInput.Activation);
            // A malformed/missing/replayed Chore aborts the ENTIRE tick.
            // A correctly ordered but nonviable business offer still consumes
            // its canonical actor cursor in Receive(), as before.
            ulong[] cursor = (ulong[])next.InputSequence.Clone();
            ulong[] nonce = (ulong[])next.InputNonce.Clone();
            foreach (var action in actions)
            {
                byte actor = action.Requester;
                if (action.Epoch != next.InputEpoch || next.InputEpoch == 0 || cursor[actor] >= ulong.MaxValue - 1 || action.Sequence != cursor[actor] + 1 || action.ClientNonce == 0 || action.ClientNonce <= nonce[actor])
                    throw new InvalidDataException("MP human action epoch, sequence or nonce invalid");
                cursor[actor] = action.Sequence;
                nonce[actor] = action.ClientNonce;
            }

            ExpireHumanOffers(next);
            CompactTerminalHumanOffers(next);
            ProcessAiConsentReplies(next, world);
            for (int i = 0; i < actions.Length; i++)
                Receive(next, world, actions[i], admitted[i]);
            // Save-safe pure expiry: no due trade is ever prepared as native.
            // Existing delayed AI/human requests are retained while viable;
            // once their original saved expiry is crossed, close them
            // deterministically without goods/goodwill or fake receipts.
            foreach (var intent in next.Mailbox.Intents.Where(i => i.State == TradeIntentStatus.Waiting && i.ExpiryClock < next.Mailbox.Clock).OrderBy(i => i.Id))
            {
                CancelBoundCounter(next, intent);
                Finish(intent, "mp_native_closed_expired");
            }

            // AI->human pending offers are pure consent proposals; there is
            // no auto-accept for humans and no inventory write. Prevent more
            // than one original-day initiative from being generated.
            // AI->AI goods requests remain suspended while MP native release
            // is false: they cannot accumulate unprocessable due journals.
            if (next.InputEpoch != 0 && next.Archive.LastAutonomousDay != next.Day)
            {
                next.Archive.LastAutonomousDay = next.Day;
                // Multi-human worlds without independently attested ORIGINAL AI
                // gold, lord identity and pending purchases cannot invent
                // economic authority. Saved consent ledger stays intact.
                ScheduleAiHumanOffers(next, world, SavedAiBudget(next, world));
            }

            // The original  archive,  mailbox, human offer ledger,
            // global ID allocator and input replay cursors are ONE Format3
            // model. Publish atomically only after Codec validation succeeds.
            // Returned bytes are a candidate, NOT durable SaveData proof.
            Publish(next);
            return SaveBytes();
        }
    }
}
