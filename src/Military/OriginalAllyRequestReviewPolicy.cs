// PRIVATE, source-only, UNARMED original AI ally-REQUEST eligibility.
// MOD authors the proposal: consulted donor AI != defended requester AI.
// Unlike a DIRECT tribe order, this request does NOT claim empty CPU queues.
// The original native evaluator alone owns busy/strength/acceptance; no call here.
// Caller must attest every observation from ORIGINAL engine under its true monitor.
// This source never registers a hook, writes a Chore, invokes SDK/native, or sends.
#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal enum OriginalAllyRequestReviewStage : byte
    {
        Waiting = 0,
        EligibleForIndependentOriginalEvaluatorReview = 1
    }

    // Detached, source-attributed facts. These cannot attest themselves and
    // MUST NOT be populated from a human HUD event, caller-provided host flag
    // or fabricated OriginalPlayerId. All nullable facts fail closed.
    internal sealed class OriginalAllyRequestReviewFacts
    {
        internal readonly AiSupportSessionFacts Session;
        internal readonly AiSupportSoloSessionFacts Solo;
        internal readonly AiSupportRoleFacts Roles;
        internal readonly bool? OriginalMonitorSnapshotVerified;
        internal readonly bool? OriginalSaveFenceClear;
        internal readonly bool? OriginalNativeConsumerAbiVerified;
        internal readonly bool? IndependentRequestOwnerSeamAvailable;
        // Additional MOD priority policy: typed original donor's ally mode
        // must be exactly idle. NOT a claim that all CPU tribe queues are empty.
        internal readonly bool? OriginalAllyModeIdleForDonor;

        internal OriginalAllyRequestReviewFacts(AiSupportSessionFacts session,
            AiSupportSoloSessionFacts solo, AiSupportRoleFacts roles,
            bool? originalMonitorSnapshotVerified, bool? originalSaveFenceClear,
            bool? originalNativeConsumerAbiVerified, bool? independentRequestOwnerSeamAvailable,
            bool? originalAllyModeIdleForDonor)
        {
            Session = session;
            Solo = solo;
            Roles = roles;
            OriginalMonitorSnapshotVerified = originalMonitorSnapshotVerified;
            OriginalSaveFenceClear = originalSaveFenceClear;
            OriginalNativeConsumerAbiVerified = originalNativeConsumerAbiVerified;
            IndependentRequestOwnerSeamAvailable = independentRequestOwnerSeamAvailable;
            OriginalAllyModeIdleForDonor = originalAllyModeIdleForDonor;
        }
    }

    internal sealed class OriginalAllyRequestReview
    {
        internal readonly OriginalAllyRequestReviewStage Stage;
        internal readonly string Reason;
        internal bool OriginalCpuRequested { get { return false; } }
        internal bool NativeDispatchAuthorized { get { return false; } }
        internal bool NativeAck { get { return false; } }
        internal bool MilitarySuccess { get { return false; } }
        internal int FavorCredit { get { return 0; } }

        internal OriginalAllyRequestReview(OriginalAllyRequestReviewStage stage, string reason)
        {
            Stage = stage;
            Reason = reason;
        }
    }

    // This is a REVIEW policy, not a native request transport, evaluator or
    // approval. A positive review is provisional; the future independent
    // original-simulation-lane owner MUST recheck every live fact and let the
    // original 4-arg consumer execute its OWN evaluator/refusal/priority.
    internal static class OriginalAllyRequestReviewPolicy
    {
        internal const string HonestAuthor = ModSupportIntent.ModAuthor;
        internal const bool OriginalCpuRequested = false;
        internal const bool NativeOrderAuthority = false;
        internal const bool DirectTribeOrderWriter = false;
        internal const bool HasInstalledNativeRequestBridge = false;
        internal const int OriginalDefendOpcode = 11;
        internal const int OriginalChoreRequestKind = 0;

        private static OriginalAllyRequestReview Wait(string reason)
        {
            return new OriginalAllyRequestReview(
                OriginalAllyRequestReviewStage.Waiting, reason);
        }
        private static bool Proven(bool? b)
        {
            return b.HasValue && b.Value;
        }

        internal static OriginalAllyRequestReview Review(
            ModSupportIntent intent, OriginalAllyRequestReviewFacts f, long now)
        {
            if (intent == null || f == null || f.Session == null ||
                f.Solo == null || f.Roles == null)
                return Wait("defer_original_request_facts_unavailable");
            AiSupportSessionFacts s = f.Session;
            if (intent.Nonce == 0 || intent.ReliefEventId == 0 ||
                intent.Kind != AiMilitaryCooperationKind.DefendAlly ||
                intent.Priority < 1 || intent.TribePower < 1 ||
                intent.ConsultedDonorAI < 1 || intent.ConsultedDonorAI > 8 ||
                intent.RequesterDefendedAI < 1 || intent.RequesterDefendedAI > 8 ||
                intent.ConsultedDonorAI == intent.RequesterDefendedAI)
                return Wait("refuse_invalid_mod_authored_defend_intent");
            if (s.Generation < 1 || s.MapEpoch < 1 || s.CancellationSerial < 0 ||
                now < 0 || s.Tick != now || now < intent.IssuedTick ||
                now > intent.ExpiresAfterTick || intent.ReliefEdgeTick < 0 ||
                intent.ReliefEdgeTick > intent.IssuedTick ||
                intent.Generation != s.Generation || intent.MapEpoch != s.MapEpoch ||
                intent.CancellationSerial != s.CancellationSerial ||
                intent.Day != s.Day || !s.MapActive || s.MapDisabled || s.Cancelled ||
                !String.Equals(intent.MapIdentitySha256, s.CurrentMapIdentitySha256,
                               StringComparison.Ordinal) ||
                !Proven(s.OriginalLiveMapIdentityVerified))
                return Wait("defer_request_original_world_or_tick_changed");
            // SHA/MVID here is an installed-SDK pin; the actual ORIGINAL native
            // 4-argument consumer ABI needs its own independent runtime proof.
            if (!String.Equals(s.InstalledShcdeseSha256,
                    OriginalAiTribeSupportPreparation.OriginalShcdeseSha256,
                    StringComparison.Ordinal) ||
                !String.Equals(s.InstalledShcdeseMvid,
                    OriginalAiTribeSupportPreparation.OriginalShcdeseMvid,
                    StringComparison.Ordinal))
                return Wait("defer_original_extender_abi_unmatched");
            AiSupportSoloSessionFacts solo = f.Solo;
            if (solo.SkirmishGameType != 0 || solo.CooperativeTrailId != 0 ||
                !Proven(solo.LocalHostVerified) ||
                !Proven(solo.OriginalGameContextReady))
                return Wait("defer_actual_solo_context_unverified");
            string rosterReason;
            if (!SoloSessionPolicy.Allows(solo.NativeLocalPlayer,
                    solo.SnapshotLocalPlayer, solo.HumanSnapshot(),
                    solo.ManagedAiSnapshot(), solo.NativeAiSnapshot(),
                    solo.PeerSnapshot(), solo.Networked, out rosterReason))
                return Wait("defer_actual_solo_roster_" + rosterReason);
            if (intent.ConsultedDonorAI == solo.NativeLocalPlayer ||
                intent.RequesterDefendedAI == solo.NativeLocalPlayer ||
                !solo.OriginalRegistersAgreeOnAi(intent.ConsultedDonorAI) ||
                !solo.OriginalRegistersAgreeOnAi(intent.RequesterDefendedAI))
                return Wait("refuse_nonoriginal_ai_donor_or_requester");
            AiSupportRoleFacts roles = f.Roles;
            if (roles.Donor != intent.ConsultedDonorAI ||
                roles.Recipient != intent.RequesterDefendedAI || roles.Enemy != 0 ||
                !Proven(roles.DonorAi) || !Proven(roles.RecipientAi) ||
                !Proven(roles.DonorAlive) || !Proven(roles.RecipientAlive) ||
                !Proven(roles.MutualAlliance) || !Proven(roles.TreatyAllows) ||
                !Proven(roles.RouteSafe))
                return Wait("defer_original_ai_roles_reciprocity_or_mod_policy");
            // These are corroborated OWNER observations, never request payload.
            // The originating MOD does not impersonate UI/human/CPU actors.
            if (!Proven(f.OriginalMonitorSnapshotVerified) ||
                !Proven(f.OriginalSaveFenceClear))
                return Wait("defer_original_owner_snapshot_or_save_fence");
            // The original evaluator's busy flag is distinct from its ally
            // mode. Do not overwrite an existing ORIGINAL human/AI ally order.
            // Future owner must read typed donor GamePlayerResources.r_AIAllyOrderMode
            // under the actual original monitor at the approved sim phase.
            if (!Proven(f.OriginalAllyModeIdleForDonor))
                return Wait("defer_original_ally_mode_busy_or_unreadable");
            if (!Proven(f.OriginalNativeConsumerAbiVerified) ||
                !Proven(f.IndependentRequestOwnerSeamAvailable))
                return Wait("defer_uninstalled_original_request_owner_bridge");

            // Deliberately NO OriginalOrderSlotUncontested/CPU-queue-empty gate.
            // This does not sample, bypass or set the original evaluator's
            // own busy flag / army threshold, nor arm any original/native call.
            return new OriginalAllyRequestReview(
                OriginalAllyRequestReviewStage.EligibleForIndependentOriginalEvaluatorReview,
                "review_mod_authored_solo_ai_request_original_evaluator_owns_decision");
        }
    }
}
#endif
