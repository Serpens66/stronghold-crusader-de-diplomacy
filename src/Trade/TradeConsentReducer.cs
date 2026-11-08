// Pure, ACTUAL Format3 consent-only state advancement on a detached clone.
// Does not require, invent or inspect any multiplayer warehouse capacity.
using System;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class CanonicalTradeReducer
    {
        private byte[] ReduceVerifiedOriginalConsentOnly(AuthenticatedTradeTick signedInputs, OriginalMultiplayerConsentFrame world)
        {
            if (signedInputs == null || world == null || _prepared != null || _pendingWorldPublication != null || _pendingConsentPublication != null || _model.Quarantined || _model.Archive.PendingNativeId != 0)
                throw new InvalidOperationException("MP native/pending state is not safe");
            CheckRoster(_model, world);
            if (world.NativeSharedTick < _model.LastSharedTick || (world.NativeSharedTick == _model.LastSharedTick && signedInputs.Activation == null && signedInputs.Requests().Length == 0) || world.NativeGameDay < _model.Day)
                throw new InvalidDataException("Original shared frame tick/day moved");
            var after = _model.Copy();
            after.LastSharedTick = world.NativeSharedTick;
            after.Mailbox.Observe(world.NativeSharedTick);
            after.Revision = checked(after.Revision + 1);
            AdvanceDay(after, world.NativeGameDay);
            if (signedInputs.Activation != null && (after.InputEpoch >= ulong.MaxValue - 1 || signedInputs.Activation.Epoch != after.InputEpoch + 1))
                throw new InvalidDataException("Host did not sign the next saved epoch");
            Activate(after, signedInputs.Activation);
            var actions = signedInputs.Requests();
            var seq = (ulong[])after.InputSequence.Clone();
            var nonce = (ulong[])after.InputNonce.Clone();
            // Two-phase preflight: reject entire batch without allocating IDs if
            // a single Chore actor is mismapped, replayed or wrong-epoch.
            for (int i = 0; i < actions.Length; i++)
            {
                var x = actions[i];
                CanonicalTrustedHumanInput proof = signedInputs.TrustedAt(i);
                if (proof == null || !Object.ReferenceEquals(proof.Command, x) || proof.EngineVerifiedSoloLocal || proof.VerifiedMapGeneration != world.LocalOriginalMapGeneration || proof.VerifiedControllerSlot != x.Requester || !world.TryAuthoritativeHumanActor(x.Requester, proof.VerifiedSteamId) || x.Epoch != after.InputEpoch || after.InputEpoch == 0 || seq[x.Requester] >= ulong.MaxValue - 1 || x.Sequence != seq[x.Requester] + 1 || x.ClientNonce == 0 || x.ClientNonce <= nonce[x.Requester])
                    throw new InvalidDataException("Original signed human actor invalid or replayed");
                seq[x.Requester] = x.Sequence;
                nonce[x.Requester] = x.ClientNonce;
            }

            ExpireHumanOffers(after);
            CompactTerminalHumanOffers(after);
            foreach (var x in actions)
            {
                after.InputSequence[x.Requester] = x.Sequence;
                after.InputNonce[x.Requester] = x.ClientNonce;
                if (x.Action >= CanonicalTradeAction.ProposeHuman && x.Action <= CanonicalTradeAction.WithdrawHuman)
                    ReceiveConsentWithoutStock(after, world, x);
                else
                    ReceiveAiWithoutCapacity(after, world, x);
            // AI bids and AI counter/decline requests consume signed
            // per-actor highwater but cannot perform economic AI decisions
            // until ACTUAL original MP storage capacity is attested.
            }

            foreach (var intent in after.Mailbox.Intents.Where(x => x.State == TradeIntentStatus.Waiting && x.ExpiryClock < after.Mailbox.Clock).OrderBy(x => x.Id))
            {
                CancelBoundCounter(after, intent);
                Finish(intent, "mp_native_closed_expired");
            }

            if (after.Archive.PendingNativeId != 0 || after.Archive.Records.Any(x => x.State == TradeDisposition.PreparingNative))
                throw new InvalidDataException("No multiplayer native journal");
            // NO ProcessDue, ScheduleAiHumanOffers,
            // ProcessAiConsentReplies, PrepareAcceptedHuman or stock Read().
            Publish(after);
            return SaveBytes();
        }
    }
}
