// The ONLY boundary from a fully signature-verified and globally
// ordered  Chore to the selected Schema19/Format3 canonical reducer.
// This is pure code; install NOTHING and mutate NO original game state.
// Caller must already be in the coordinator's post-ReceiveChore managed
// engine lane while holding its original EngineInterface.threadLock.
using System;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal static class SignedTradeActionReducerBridge
    {
        // Call ONLY from ChoreSession.ConsumeMixed's humanReducer callback,
        // not from unsigned Steam ingress or a native/R3 detour callback.
        internal static CanonicalTrustedHumanInput BindVerified(HumanTradeActionEnvelope verifiedSignedAction, CanonicalTradeSnapshot exactNativeWorld, ulong currentCanonicalInputEpoch)
        {
            if (verifiedSignedAction == null || !verifiedSignedAction.IsHostSigned || exactNativeWorld == null || exactNativeWorld.MapGeneration < 1 || exactNativeWorld.LocalOfflineHumanSlot != 0 || currentCanonicalInputEpoch == 0 || verifiedSignedAction.Epoch != currentCanonicalInputEpoch)
                throw new AdmissionException(AdmissionReason.wrong_scope);
            CanonicalPlayer from = exactNativeWorld.Player(verifiedSignedAction.ActorSlot);
            CanonicalPlayer to = exactNativeWorld.Player(verifiedSignedAction.RecipientSlot);
            if (from == null || to == null || !from.Human || !from.Alive || !to.Alive || from.SteamId <= 1000 || from.SteamId != verifiedSignedAction.ActorSteamId)
                throw new AdmissionException(AdmissionReason.sender_not_participant);
            CanonicalTradeAction kind = (CanonicalTradeAction)(byte)verifiedSignedAction.Action;
            var command = new OrderedHumanTrade(verifiedSignedAction.ActorSequence, verifiedSignedAction.Epoch, verifiedSignedAction.ActorNonce, verifiedSignedAction.ActorSlot, verifiedSignedAction.RecipientSlot, verifiedSignedAction.WantedGood, verifiedSignedAction.OfferedGood, verifiedSignedAction.RequestedAmount, verifiedSignedAction.Tier, kind, verifiedSignedAction.CounterId, verifiedSignedAction.CounterRootId, verifiedSignedAction.CounterOfferedAmount, verifiedSignedAction.CounterCreatedDay, verifiedSignedAction.CounterExpiresDay, verifiedSignedAction.HumanOfferedAmount, verifiedSignedAction.ReferenceOfferedGood, verifiedSignedAction.ReferenceRequestedGood, verifiedSignedAction.ReferenceRequestedAmount);
            // The original Steam-to-slot mapping is independently observed by each
            // peer and bound to the host-signed Chore actor. Never forge the
            // EngineVerifiedSoloLocal bit in a connected multiplayer match.
            return new CanonicalTrustedHumanInput(command, verifiedSignedAction.ActorSlot, verifiedSignedAction.ActorSteamId, exactNativeWorld.MapGeneration, false);
        }

        // The activation itself is host-signed and globally ordered in the SAME
        // host sequence stream as all other actions. It is not a GUI event.
        internal static SignedTradeActivation BindVerifiedActivation(AuthenticatedHumanActionActivation signedActivation, ulong currentlySavedEpoch)
        {
            if (signedActivation == null || signedActivation.HostSequence == 0 || signedActivation.InputEpoch <= currentlySavedEpoch)
                throw new AdmissionException(AdmissionReason.invalid_command);
            return new SignedTradeActivation(signedActivation.InputEpoch);
        }
    }
}
