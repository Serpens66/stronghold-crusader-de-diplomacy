// one shot admission - Pure READ-ONLY one-shot original Singleplayer barter readiness.
// This is NOT an authority: no native setter, save writer or Steam/MP calls.
// Facts must later be obtained from genuine original events UNDER the same
// original EngineInterface monitor. Synthetic all-true is never a goods grant.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalOneShotFacts
    {
        // 20 independently required original-game observations. Neither
        // UI command, old save, peer packet nor synthetically true bits qualify.
        internal bool OriginalEngineAndManagedAbiAttested;
        internal bool ExactSingleHumanAndNoRemotePeer;
        internal bool OriginalEngineMonitorHeld;
        internal bool OriginalGameThreadProven;
        internal bool CurrentOriginalSaveDataReadbackProven;
        internal bool AllThreeOriginalSaveHooksOwned;
        internal bool CurrentMapRosterOriginalIdentityProven;
        internal bool OriginalAiOfferProven;
        internal bool OriginalHumanAcceptedCurrentTerms;
        internal bool OriginalQuoteFromActualAcceptedProductionDecision;
        internal bool OriginalQuoteAndSavedRecordExactMatch;
        internal bool OriginalLivePricePairRecheckedUnderLease;
        internal bool AllFourOriginalGoodsAndCapacityCellsRechecked;
        internal bool OriginalSourceSafetyReservesCurrent;
        internal bool AtomicSaveAndMutationBoundaryIdle;
        internal bool NoPendingOrQuarantinedNativeJournal;
        internal bool NoPreviouslyCommittedOrReservedTradeId;
        internal bool OriginalTradeOneShotConsumptionTracked;
        internal bool ExactPrecommitAndCrashRecoveryReady;
        internal bool SameQuoteSaveColdReplayGuardProven;
        internal long CurrentMapGeneration, OriginalSaveGeneration;
        internal string CurrentMapSha, OriginalQuoteMapSha;
        internal string CurrentRosterSha, OriginalQuoteRosterSha;
        internal int LocalHumanSlot, RequesterSlot, RecipientSlot;
        internal ushort OfferedGood, RequestedGood;
        internal int OfferedAmount, RequestedAmount;
        internal ulong QuoteId, AcceptedQuoteId;
        internal int IssuedDay, CurrentDay, ExpiresDay;
        internal long OriginalDecisionSequence;
        // Four distinct native cells: requester offered, recipient offered,
        // recipient requested, requester requested. Never infer capacities.
        internal int[] Before, OriginalCapacities;
        internal int RequesterSourceReserve, RecipientSourceReserve;
    }

    internal static class OriginalOneShotAdmission
    {
        private static bool Reject(out string reason,string cause)
        { reason=cause;return false; }

        private static bool Digest(string value)
        {
            if(value==null||value.Length!=64)return false;
            foreach(char c in value)
                if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))
                    return false;
            return true;
        }

        private static bool Good(ushort value)
        {
            return value==2||value==3||value==4||value==6||value==7||
                   (value>=9&&value<=24);
        }

        // Success is a pure set-of-conditions classification only.
        // It is NEVER a capability or permission token.
        internal static bool TryClassifyReadOnly(OriginalOneShotFacts f,
            out string reason)
        {
            reason="one_shot_original_facts_missing";
            if(f==null)return false;
            if(!f.OriginalEngineAndManagedAbiAttested)
                return Reject(out reason,"original_native_managed_abi_unproven");
            if(!f.ExactSingleHumanAndNoRemotePeer)
                return Reject(out reason,"not_exact_original_solo");
            if(!f.OriginalEngineMonitorHeld)
                return Reject(out reason,"original_engine_monitor_not_held");
            if(!f.OriginalGameThreadProven)
                return Reject(out reason,"original_game_thread_not_proven");
            if(!f.CurrentOriginalSaveDataReadbackProven)
                return Reject(out reason,"current_original_save_callback_unproven");
            if(!f.AllThreeOriginalSaveHooksOwned)
                return Reject(out reason,"original_save_hooks_not_owned");
            if(!f.CurrentMapRosterOriginalIdentityProven)
                return Reject(out reason,"original_map_roster_not_attested");
            if(!f.OriginalAiOfferProven)
                return Reject(out reason,"original_ai_offer_not_proven");
            if(!f.OriginalHumanAcceptedCurrentTerms)
                return Reject(out reason,"original_human_consent_not_proven");
            if(!f.OriginalQuoteFromActualAcceptedProductionDecision)
                return Reject(out reason,"accepted_production_quote_not_proven");
            if(!f.OriginalQuoteAndSavedRecordExactMatch)
                return Reject(out reason,"quote_and_record_not_exact");
            if(!f.OriginalLivePricePairRecheckedUnderLease)
                return Reject(out reason,"original_market_or_pair_unverified");
            if(!f.AllFourOriginalGoodsAndCapacityCellsRechecked)
                return Reject(out reason,"four_original_cells_not_attested");
            if(!f.OriginalSourceSafetyReservesCurrent)
                return Reject(out reason,"original_source_reserves_unproven");
            if(!f.AtomicSaveAndMutationBoundaryIdle)
                return Reject(out reason,"original_atomic_boundary_not_idle");
            if(!f.NoPendingOrQuarantinedNativeJournal)
                return Reject(out reason,"pending_or_quarantined_original_journal");
            if(!f.NoPreviouslyCommittedOrReservedTradeId)
                return Reject(out reason,"trade_id_may_be_duplicate");
            if(!f.OriginalTradeOneShotConsumptionTracked)
                return Reject(out reason,"one_shot_trade_consumption_unproven");
            if(!f.ExactPrecommitAndCrashRecoveryReady)
                return Reject(out reason,"native_crash_atomicity_not_proven");
            if(!f.SameQuoteSaveColdReplayGuardProven)
                return Reject(out reason,"same_quote_save_cold_replay_unproven");
            if(f.CurrentMapGeneration<=0||
               f.OriginalSaveGeneration!=f.CurrentMapGeneration)
                return Reject(out reason,"original_save_generation_stale");
            if(!Digest(f.CurrentMapSha)||!Digest(f.OriginalQuoteMapSha)||
               !String.Equals(f.CurrentMapSha,f.OriginalQuoteMapSha,
                              StringComparison.Ordinal))
                return Reject(out reason,"quote_map_digest_missing_or_foreign");
            if(!Digest(f.CurrentRosterSha)||!Digest(f.OriginalQuoteRosterSha)||
               !String.Equals(f.CurrentRosterSha,f.OriginalQuoteRosterSha,
                              StringComparison.Ordinal))
                return Reject(out reason,"quote_roster_digest_missing_or_foreign");
            if(f.LocalHumanSlot<1||f.LocalHumanSlot>8||
               f.RequesterSlot<1||f.RequesterSlot>8||
               f.RecipientSlot<1||f.RecipientSlot>8||
               f.RequesterSlot==f.RecipientSlot||
               (f.LocalHumanSlot!=f.RequesterSlot&&
                f.LocalHumanSlot!=f.RecipientSlot))
                return Reject(out reason,"not_local_original_human_barter");
            if(f.QuoteId<1001||f.AcceptedQuoteId!=f.QuoteId||
               f.OriginalDecisionSequence<1||
               f.IssuedDay<1||f.CurrentDay<f.IssuedDay||
               f.CurrentDay>f.ExpiresDay||
               f.ExpiresDay-f.IssuedDay>365)
                return Reject(out reason,"quote_id_decision_or_expiry_invalid");
            if(!Good(f.OfferedGood)||!Good(f.RequestedGood)||
               f.OfferedGood==f.RequestedGood||
               f.OfferedAmount<1||f.RequestedAmount<1||
               f.OfferedAmount>100000||f.RequestedAmount>100000)
                return Reject(out reason,"untrusted_or_invalid_quote_terms");
            if(f.Before==null||f.OriginalCapacities==null||
               f.Before.Length!=4||f.OriginalCapacities.Length!=4||
               f.RequesterSourceReserve<0||f.RecipientSourceReserve<0)
                return Reject(out reason,"missing_original_four_cell_values");
            for(int i=0;i<4;i++)
                if(f.Before[i]<0||f.OriginalCapacities[i]<f.Before[i]||
                   f.OriginalCapacities[i]>int.MaxValue-200000)
                    return Reject(out reason,"invalid_native_cell_"+i);
            if((long)f.Before[0]-f.OfferedAmount<f.RequesterSourceReserve||
               (long)f.Before[2]-f.RequestedAmount<f.RecipientSourceReserve)
                return Reject(out reason,"original_goods_reserve_or_stock_short");
            if((long)f.Before[1]+f.OfferedAmount>f.OriginalCapacities[1]||
               (long)f.Before[3]+f.RequestedAmount>f.OriginalCapacities[3])
                return Reject(out reason,"original_receiver_capacity_changed");
            reason="readiness_classified_only_never_native_write";
            return true;
        }

        // The ONLY goods/MP grants in this file are unconditional false.
        internal static bool AuthorizeNativeGoodsCommit(
            OriginalOneShotFacts ignored) { return false; }
        internal static bool AuthorizeSteamOrMultiplayer(
            OriginalOneShotFacts ignored) { return false; }
    }

#if !ORIGINAL_TRADE_ADMISSION_POLICY_TEST
    internal sealed partial class DiplomacyFeature
    {
        // Release-compiled snapshot, deliberately incapable of claiming
        // original production consent/quote/4-cell/one-shot SaveCold proof.
        // It is called from the actual gameplay owner or a private QA observer.
        // No API, native setter, file write, network command or save is invoked.
        private bool ObserveUnapprovedOneShotReadOnly(out string reason)
        {
            var facts=new OriginalOneShotFacts {
                OriginalEngineAndManagedAbiAttested = false,
                ExactSingleHumanAndNoRemotePeer = false,
                OriginalEngineMonitorHeld = IsNativeDiplomacySectionHeld,
                OriginalGameThreadProven = false,
                CurrentOriginalSaveDataReadbackProven =
                    HasPassiveOriginalSaveReadback(),
                AllThreeOriginalSaveHooksOwned = OriginalMpSaveFenceReady,
                CurrentMapRosterOriginalIdentityProven =
                    _mapActive&&!_mapDisabled&&!_mapBootstrapPending&&
                    !String.IsNullOrEmpty(_mapFingerprint)&&
                    !String.IsNullOrEmpty(_rosterFingerprint),
                // A pending UI or Mod AI intent is NOT original CPU origin,
                // original human acceptance or native transaction proof.
                OriginalAiOfferProven = false,
                OriginalHumanAcceptedCurrentTerms = false,
                OriginalQuoteFromActualAcceptedProductionDecision = false,
                OriginalQuoteAndSavedRecordExactMatch = false,
                OriginalLivePricePairRecheckedUnderLease = false,
                AllFourOriginalGoodsAndCapacityCellsRechecked = false,
                OriginalSourceSafetyReservesCurrent = false,
                AtomicSaveAndMutationBoundaryIdle =
                    _atomicBoundary!=null&&!_atomicBoundary.Quarantined&&
                    !_atomicBoundary.SaveActive&&!_atomicBoundary.MutationActive,
                NoPendingOrQuarantinedNativeJournal =
                    !_tradeFaulted&&!_unattestedSavedNativeTrade&&
                    _pendingPackage==null&&_nativeTransferProbe==null&&
                    !_goodsTransfersUnavailable,
                NoPreviouslyCommittedOrReservedTradeId = false,
                OriginalTradeOneShotConsumptionTracked = false,
                ExactPrecommitAndCrashRecoveryReady = false,
                SameQuoteSaveColdReplayGuardProven = false,
                CurrentMapGeneration = _sessionGeneration,
                OriginalSaveGeneration =
                    HasPassiveOriginalSaveReadback()?_sessionGeneration:-1L,
                CurrentMapSha = _mapFingerprint,
                OriginalQuoteMapSha = null,
                CurrentRosterSha = _rosterFingerprint,
                OriginalQuoteRosterSha = null,
                LocalHumanSlot = _localPlayer,
                // A passive original-game observation has no genuine
                // trade terms or accepted original consent at this point.
                // Explicit zero/null means UNKNOWN, not a default authority.
                RequesterSlot = 0,
                RecipientSlot = 0,
                OfferedGood = 0,
                RequestedGood = 0,
                OfferedAmount = 0,
                RequestedAmount = 0,
                QuoteId = 0,
                AcceptedQuoteId = 0,
                IssuedDay = 0,
                CurrentDay = 0,
                ExpiresDay = 0,
                OriginalDecisionSequence = 0,
                Before = null,
                OriginalCapacities = null,
                RequesterSourceReserve = 0,
                RecipientSourceReserve = 0
            };
            bool observeOnly=OriginalOneShotAdmission.TryClassifyReadOnly(
                facts,out reason);
            if(OriginalOneShotAdmission.AuthorizeNativeGoodsCommit(facts)||
               OriginalOneShotAdmission.AuthorizeSteamOrMultiplayer(facts))
                throw new InvalidOperationException("Unexpected one-shot trade admission native/MP authority");
            return observeOnly;
        }
    }
#endif
}
