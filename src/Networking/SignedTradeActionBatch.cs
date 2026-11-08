// Deferred authorized Chore work for the existing ONE Format3/Schema19
// CanonicalTradeReducer. Native callbacks only capture references;
// ChoreSession RSA verification happens post-native; THIS collector then
// stages verified immutable actions until the NEXT original shared tick.
// It neither touches game memory nor owns SaveData. The coordinator calls
// AcknowledgePublished ONLY after its single canonical SaveData owner readback.
// In-memory readback does not prove a physical durable game .sav.
// Block original saves while an unacknowledged batch is pending.
using System;
using System.Collections.Generic;
using System.Linq;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class SignedTradeTickBatch
    {
        internal readonly AuthenticatedTradeTick Input;
        internal readonly int ActionCount;
        internal readonly ulong LastHostSequence;
        internal readonly Bytes Session;
        internal readonly object Owner;
        internal SignedTradeTickBatch(AuthenticatedTradeTick input, int count, ulong seq, Bytes session, object owner)
        {
            Input = input;
            ActionCount = count;
            LastHostSequence = seq;
            Session = session;
            Owner = owner;
        }
    }

    internal sealed class SignedTradeActionBatchCollector
    {
        private const int MaximumBatchActions = 64;
        private readonly object owner = new object ();
        private readonly Bytes session;
        private readonly List<HumanTradeActionEnvelope> actions = new List<HumanTradeActionEnvelope>();
        private AuthenticatedHumanActionActivation activation;
        private OrderedHostWorldWitness sealedOriginalWorld;
        private ulong lastHostSequence;
        private SignedTradeTickBatch staged;
        private bool invalid;
        internal SignedTradeActionBatchCollector(Bytes pinnedSessionId)
        {
            if (pinnedSessionId == null || pinnedSessionId.Length != 32)
                throw new ArgumentException("Exact signed session is required");
            session = new Bytes(pinnedSessionId.Copy());
        }

        internal int PendingCount
        {
            get
            {
                return actions.Count + (activation == null ? 0 : 1) + (sealedOriginalWorld == null ? 0 : 1);
            }
        }

        internal bool MustDelayOriginalSave
        {
            get
            {
                return invalid || PendingCount != 0 || staged != null;
            }
        }

        internal ulong EffectiveSignedInputEpochHint
        {
            get
            {
                return activation == null ? 0 : activation.InputEpoch;
            }
        }

        internal ulong LastIncludedVerifiedHostSequence
        {
            get
            {
                return lastHostSequence;
            }
        }

        internal bool HasSealedWorld
        {
            get
            {
                return sealedOriginalWorld != null || staged != null;
            }
        }

        internal void Invalidate()
        {
            invalid = true;
            actions.Clear();
            activation = null;
            sealedOriginalWorld = null;
            staged = null;
        }

        private void NoteGlobalSequence(ulong sequence, Bytes signedSession)
        {
            Require.That(!invalid && staged == null && signedSession != null && signedSession.Same(session) && sequence > lastHostSequence && sequence < ulong.MaxValue, AdmissionReason.session_invalidated);
            lastHostSequence = sequence;
        }

        // Both methods may ONLY be called by the corresponding callbacks of
        // ChoreSession.ConsumeMixed, after the signature is already verified.
        // They are intentionally not methods on a Steam receive handler.
        internal void OnVerifiedActivation(AuthenticatedHumanActionActivation value)
        {
            if (value == null || activation != null || actions.Count > 0 || sealedOriginalWorld != null)
            {
                Invalidate();
                throw new AdmissionException(AdmissionReason.session_invalidated);
            }

            NoteGlobalSequence(value.HostSequence, value.SessionId);
            activation = value;
        }

        internal void OnVerifiedHumanAction(HumanTradeActionEnvelope value)
        {
            if (value == null || !value.IsHostSigned || sealedOriginalWorld != null || actions.Count >= MaximumBatchActions)
            {
                Invalidate();
                throw new AdmissionException(AdmissionReason.session_invalidated);
            }

            NoteGlobalSequence(value.HostSequence, value.SessionId);
            if (activation != null && value.Epoch != activation.InputEpoch)
            {
                Invalidate();
                throw new AdmissionException(AdmissionReason.wrong_scope);
            }

            actions.Add(value);
        }

        // Only the RSA-verified existing ChoreSession Kind8 callback may call
        // this. A decoded WorldFrame body/Steam peer echo alone is NOT authority.
        internal void OnVerifiedWorldFrame(OrderedHostWorldWitness verified)
        {
            if (verified == null || sealedOriginalWorld != null || invalid || staged != null)
            {
                Invalidate();
                throw new AdmissionException(AdmissionReason.session_invalidated);
            }

            NoteGlobalSequence(verified.SignedHostChoreSequence, verified.OriginalSessionId);
            sealedOriginalWorld = verified;
        }

        // Unlike the old BuildForNextSharedTick this consumes ONLY a contemporaneous
        // ORIGINAL frame retained BEFORE receipt of the later signed Kind8 seal.
        internal SignedTradeTickBatch BuildForAuthenticatedConsentFrame(ObservedOriginalConsentFrame frame, ulong savedInputEpoch)
        {
            Require.That(!invalid && staged == null && sealedOriginalWorld != null, AdmissionReason.bootstrap_incomplete);
            if (frame == null || frame.Tick != sealedOriginalWorld.OriginalSharedTick || frame.HostSteam != sealedOriginalWorld.OriginalHostSteamId || !frame.SessionId.Same(session) || !frame.SameWorldHash(sealedOriginalWorld.OriginalWorldSha256))
                throw new AdmissionException(AdmissionReason.wrong_scope);
            ulong effectiveEpoch = savedInputEpoch;
            SignedTradeActivation activated = null;
            if (activation != null)
            {
                activated = SignedTradeActionReducerBridge.BindVerifiedActivation(activation, savedInputEpoch);
                effectiveEpoch = activation.InputEpoch;
            }

            if (effectiveEpoch == 0 || effectiveEpoch != sealedOriginalWorld.CanonicalInputEpoch)
                throw new AdmissionException(AdmissionReason.wrong_scope);
            var trusted = new CanonicalTrustedHumanInput[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                trusted[i] = ConsentOnlyChoreAdapter.BindSignedAction(actions[i], frame.Original, effectiveEpoch);
            staged = new SignedTradeTickBatch(new AuthenticatedTradeTick(activated, trusted), trusted.Length, lastHostSequence, session, owner);
            return staged;
        }

        // Legacy MP unframed batching is explicitly NOT permitted. A signed
        // action can never settle against a guessed subsequent local feature tick.
        // This is a READ and validated projection, not a mutation.
        internal SignedTradeTickBatch BuildForNextSharedTick(CanonicalTradeSnapshot world, ulong currentlySavedInputEpoch)
        {
            Require.That(!invalid && staged == null, AdmissionReason.session_invalidated);
            if (world == null || world.MapGeneration < 1 || world.LocalOfflineHumanSlot == 0)
                throw new AdmissionException(AdmissionReason.wrong_scope);
            ulong effectiveEpoch = currentlySavedInputEpoch;
            SignedTradeActivation pendingActivation = null;
            if (activation != null)
            {
                pendingActivation = SignedTradeActionReducerBridge.BindVerifiedActivation(activation, currentlySavedInputEpoch);
                effectiveEpoch = activation.InputEpoch;
            }

            if (actions.Count > 0 && effectiveEpoch == 0)
                throw new AdmissionException(AdmissionReason.bootstrap_incomplete);
            var verified = new CanonicalTrustedHumanInput[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                verified[i] = SignedTradeActionReducerBridge.BindVerified(actions[i], world, effectiveEpoch);
            staged = new SignedTradeTickBatch(new AuthenticatedTradeTick(pendingActivation, verified), verified.Length, lastHostSequence, session, owner);
            return staged;
        }

        // AFTER the original shared tick reduced these exact inputs and the
        // coordinator published the authoritative Schema19 SaveData bytes,
        // acknowledge only the input high-waters actually serialized.
        internal void AcknowledgePublished(SignedTradeTickBatch ticket, CanonicalModel publishedCanonical)
        {
            Require.That(!invalid && ticket != null && Object.ReferenceEquals(ticket, staged) && Object.ReferenceEquals(ticket.Owner, owner) && ticket.Session.Same(session) && publishedCanonical != null, AdmissionReason.session_invalidated);
            if (sealedOriginalWorld != null)
                Require.That(publishedCanonical.LastSharedTick == sealedOriginalWorld.OriginalSharedTick && publishedCanonical.InputEpoch == sealedOriginalWorld.CanonicalInputEpoch, AdmissionReason.session_invalidated);
            if (activation != null)
                Require.That(publishedCanonical.InputEpoch == activation.InputEpoch, AdmissionReason.bootstrap_incomplete);
            foreach (var action in actions)
            {
                Require.That(publishedCanonical.InputEpoch == action.Epoch && publishedCanonical.InputSequence[action.ActorSlot] >= action.ActorSequence && publishedCanonical.InputNonce[action.ActorSlot] >= action.ActorNonce, AdmissionReason.session_invalidated);
            }

            actions.Clear();
            activation = null;
            sealedOriginalWorld = null;
            staged = null;
        }
    }
}
