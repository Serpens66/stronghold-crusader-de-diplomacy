// Binds the chosen  host-signed HumanAction directly to the World/AI
// ORIGINAL consent-only interface. Does NOT fabricate a CanonicalTradeSnapshot.
using System;
using System.IO;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal static class ConsentOnlyChoreAdapter
    {
        internal static CanonicalTrustedHumanInput BindSignedAction(HumanTradeActionEnvelope action, OriginalMultiplayerConsentFrame world, ulong canonicalEpoch)
        {
            if (action == null || world == null || !action.IsHostSigned || action.Epoch != canonicalEpoch || canonicalEpoch == 0 || world.NativePhysicalSettlementAuthorized || !world.TryAuthoritativeHumanActor(action.ActorSlot, action.ActorSteamId) || !world.Players().Any(p => p.Slot == action.RecipientSlot && p.Alive))
                throw new AdmissionException(AdmissionReason.sender_not_participant);
            var command = new OrderedHumanTrade(action.ActorSequence, action.Epoch, action.ActorNonce, action.ActorSlot, action.RecipientSlot, action.WantedGood, action.OfferedGood, action.RequestedAmount, action.Tier, (CanonicalTradeAction)(byte)action.Action, action.CounterId, action.CounterRootId, action.CounterOfferedAmount, action.CounterCreatedDay, action.CounterExpiresDay, action.HumanOfferedAmount, action.ReferenceOfferedGood, action.ReferenceRequestedGood, action.ReferenceRequestedAmount);
            return new CanonicalTrustedHumanInput(command, action.ActorSlot, action.ActorSteamId, world.LocalOriginalMapGeneration, false);
        }
    }
}
