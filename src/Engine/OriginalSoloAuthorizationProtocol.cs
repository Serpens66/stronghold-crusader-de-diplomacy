// original hud – release-compiled TWO-PHASE, ORIGINAL-SINGLEPLAYER-ONLY
// authorization classification. Classification is NOT a runtime write token.
// An original HUD Noesis callback or an all-true synthetic facts object
// cannot, by itself, authorize native goods. The production release keeps
// its existing hard NativeGoodsAndSaveCommitAuthority and CanMutateGameGoods
// false. No native write, SaveData change, network packet or file I/O here.
using System;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class SoloPrecommitFacts
    {
    internal bool OriginalLoadedNativeImageAndManagedAbiAttested { get; set; }
    internal bool OfficialApiSharedDependencyExactlyMatched { get; set; }
    internal bool ExactOneOriginalLocalHumanAndZeroRemotePeers { get; set; }
    internal bool OriginalUnityGameThread { get; set; }
    internal bool OriginalEngineMonitorExclusive { get; set; }
    internal bool CurrentMapAndRosterBound { get; set; }
    internal bool OriginalCurrentGenerationSaveDataAndArchiveExact { get; set; }
    internal bool OriginalThreeSaveHooksOwned { get; set; }
    internal bool NativeOriginalCapacitySourceVerified { get; set; }
    internal bool CurrentOriginalMarketPricesAndPairRechecked { get; set; }
    internal bool FourNativeCellsAndSourceReservesRechecked { get; set; }
    internal bool NoPendingNativeJournalOrQuarantine { get; set; }
    internal bool OriginalSaveAndMutationBoundaryIdle { get; set; }
    internal bool RecoveryJournalCanCompensateAllFourNativeCells { get; set; }
    internal bool PriorIndependentOriginalColdRecoveryEvidence { get; set; }
    internal bool GenuineOriginalModAiOfferProduced { get; set; }
    internal bool OfferIsActuallyPendingForOriginalHuman { get; set; }
    internal bool OriginalHumanNoesisHudAcceptCallback { get; set; }
    internal bool OriginalLocalHumanInputAuthenticated { get; set; }
    internal bool NotPrivateHeadlessDriverConsent { get; set; }
    internal bool ExactAcceptedImmutableQuoteAndInputCursor { get; set; }
    internal bool QuoteNotAlreadyReservedOrCommitted { get; set; }
    internal bool OneShotQuoteConsumptionTracked { get; set; }
    internal bool NoVanillaGameActionForwardingOrUnverifiedHook { get; set; }
    internal bool OriginalCurrentGameTradeRecordWouldBeSaveable { get; set; }
    internal bool OriginalSoloNativeGoodsPermissionReviewedByPublisher { get; set; }
    internal long MapGeneration { get; set; }
    internal long SavedGeneration { get; set; }
    internal long OriginalTick { get; set; }
    internal int CurrentDay { get; set; }
    internal int IssuedDay { get; set; }
    internal int ExpiryDay { get; set; }
    internal int LocalHumanSlot { get; set; }
    internal int AiSlot { get; set; }
    internal int OfferedAmount { get; set; }
    internal int RequestedAmount { get; set; }
    internal int SourceReserveA { get; set; }
    internal int SourceReserveC { get; set; }
    internal ushort OfferedGood { get; set; }
    internal ushort RequestedGood { get; set; }
    internal ulong OfferId { get; set; }
    internal ulong AcceptedOfferId { get; set; }
    internal ulong ArchiveNextId { get; set; }
    internal ulong InputEpoch { get; set; }
    internal ulong DecisionSequence { get; set; }
    internal ulong DecisionNonce { get; set; }
    internal ulong AcceptedSequenceHighWater { get; set; }
    internal ulong AcceptedNonceHighWater { get; set; }
    internal string CurrentMapFingerprint { get; set; }
    internal string QuoteMapFingerprint { get; set; }
    internal string CurrentRosterFingerprint { get; set; }
    internal string QuoteRosterFingerprint { get; set; }
    internal string OriginalSaveDestination { get; set; }
    internal string OriginalSaveArchiveDigest { get; set; }
    internal int[] Before { get; set; }
    internal int[] NativeWarehouseCapacity { get; set; }
    }

    internal sealed class SoloCommitAndColdFacts
    {
    internal ulong AcceptedOfferId { get; set; }
    internal ulong NativeTradeId { get; set; }
    internal ulong PersistedCommittedId { get; set; }
    internal bool PrecommitWitnessWasActuallyOwnedByOriginalEngine { get; set; }
    internal bool SameOriginalGameThreadAndMonitorAtNativeCommit { get; set; }
    internal bool OriginalFourCellNativeCommitExactlyOnce { get; set; }
    internal bool OriginalQuoteWasConsumedBeforeAnySecondAttempt { get; set; }
    internal bool OriginalManagedArchiveCommitted { get; set; }
    internal bool OriginalGameSaveDataAcknowledgedAndReadBack { get; set; }
    internal bool OriginalSaveFileAndCommittedCanonicalBytesMatch { get; set; }
    internal bool ColdOneOriginalProcessAndNativeGoodsMatch { get; set; }
    internal bool ColdTwoDifferentOriginalProcessAndNativeGoodsMatch { get; set; }
    internal bool BothColdLoadsPerformedZeroNativeGoodsWrites { get; set; }
    internal bool ExactlySameCanonicalMapRosterAndSourceSaveDigest { get; set; }
    internal bool NoPendingNativeJournalOrRecoveryQuarantine { get; set; }
    internal bool NoReplayTradeIdOrAdditionalAcceptedConsent { get; set; }
    internal bool NoSteamOrRemoteHumanPeer { get; set; }
    internal long OriginalFirstGameGeneration { get; set; }
    internal long PersistedGeneration { get; set; }
    internal int OriginalWarmProcessId { get; set; }
    internal int OriginalFirstColdPid { get; set; }
    internal int OriginalSecondColdPid { get; set; }
    internal int[] Before { get; set; }
    internal int[] AfterCommit { get; set; }
    internal int[] Saved { get; set; }
    internal int[] ColdOne { get; set; }
    internal int[] ColdTwo { get; set; }
    internal string OriginalSaveSha256 { get; set; }
    internal string FirstColdSaveSha256 { get; set; }
    internal string SecondColdSaveSha256 { get; set; }
    internal string CanonicalBeforeSha256 { get; set; }
    internal string CanonicalCommittedSha256 { get; set; }
    }

    internal static class OriginalSoloAuthorizationProtocol
    {
        private static bool Refuse(out string reason,string why)
        {
            reason=why;
            return false;
        }

        private static bool Digest(string value)
        {
            if(value==null||value.Length!=64)return false;
            foreach(char c in value)
                if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;
            return true;
        }

        private static bool SupportedGood(ushort g)
        {
            return g==2||g==3||g==4||g==6||g==7||(g>=9&&g<=24);
        }

        // Readiness classification for a *proposed* original one-shot trade.
        // A real original human HUD decision is NOT the same thing as a
        // private QA-driver call into the same production consent method.
        // Existing original SaveData/Crash/Cold protection remains separate.
        internal static bool TryClassifyBeforeAnyNativeWrite(
            SoloPrecommitFacts f,out string reason)
        {
            reason="no_owned_original_facts";
            if(f==null)return false;
            if(!f.OriginalLoadedNativeImageAndManagedAbiAttested)
                return Refuse(out reason,"original_native_managed_abi_not_live_attested");
            if(!f.OfficialApiSharedDependencyExactlyMatched)
                return Refuse(out reason,"official_apishared_runtime_not_exact");
            if(!f.ExactOneOriginalLocalHumanAndZeroRemotePeers)
                return Refuse(out reason,"not_original_one_human_solo");
            if(!f.OriginalUnityGameThread||!f.OriginalEngineMonitorExclusive)
                return Refuse(out reason,"original_game_thread_or_engine_monitor_missing");
            if(!f.CurrentMapAndRosterBound)
                return Refuse(out reason,"original_map_roster_identity_unproven");
            if(!f.OriginalCurrentGenerationSaveDataAndArchiveExact||
               !f.OriginalThreeSaveHooksOwned)
                return Refuse(out reason,"original_save_callback_or_hooks_not_durable");
            if(!f.NativeOriginalCapacitySourceVerified||
               !f.CurrentOriginalMarketPricesAndPairRechecked)
                return Refuse(out reason,"native_capacity_prices_or_pair_not_attested");
            if(!f.FourNativeCellsAndSourceReservesRechecked)
                return Refuse(out reason,"original_four_cells_not_attested_under_monitor");
            if(!f.NoPendingNativeJournalOrQuarantine||
               !f.OriginalSaveAndMutationBoundaryIdle)
                return Refuse(out reason,"native_journal_or_atomic_boundary_busy");
            if(!f.RecoveryJournalCanCompensateAllFourNativeCells||
               !f.PriorIndependentOriginalColdRecoveryEvidence)
                return Refuse(out reason,"recovery_or_independent_cold_test_unproven");
            if(!f.GenuineOriginalModAiOfferProduced||
               !f.OfferIsActuallyPendingForOriginalHuman)
                return Refuse(out reason,"actual_live_mod_ai_human_quote_missing");
            if(!f.OriginalHumanNoesisHudAcceptCallback)
                return Refuse(out reason,"real_original_human_HUD_accept_not_observed");
            if(!f.OriginalLocalHumanInputAuthenticated)
                return Refuse(out reason,"original_local_human_input_not_authenticated");
            if(!f.NotPrivateHeadlessDriverConsent)
                return Refuse(out reason,"private_test_driver_is_not_manual_hud_consent");
            if(!f.ExactAcceptedImmutableQuoteAndInputCursor)
                return Refuse(out reason,"immutable_quote_or_authenticated_input_cursor_mismatch");
            if(!f.QuoteNotAlreadyReservedOrCommitted||
               !f.OneShotQuoteConsumptionTracked)
                return Refuse(out reason,"quote_replay_or_one_shot_consumption_unproven");
            if(!f.NoVanillaGameActionForwardingOrUnverifiedHook)
                return Refuse(out reason,"unverified_gameaction_callback_or_forwarding");
            if(!f.OriginalCurrentGameTradeRecordWouldBeSaveable)
                return Refuse(out reason,"original_native_quote_not_saveable");
            if(!f.OriginalSoloNativeGoodsPermissionReviewedByPublisher)
                return Refuse(out reason,"normal_native_goods_permission_not_reviewed");
            if(f.MapGeneration<1||f.SavedGeneration!=f.MapGeneration||
               f.OriginalTick<1||f.CurrentDay<1||
               f.IssuedDay<1||f.IssuedDay>f.CurrentDay||
               f.CurrentDay>f.ExpiryDay||f.ExpiryDay-f.IssuedDay>15)
                return Refuse(out reason,"saved_game_generation_tick_or_offer_day_mismatch");
            if(!Digest(f.CurrentMapFingerprint)||
               !String.Equals(f.CurrentMapFingerprint,f.QuoteMapFingerprint,StringComparison.Ordinal)||
               !Digest(f.CurrentRosterFingerprint)||
               !String.Equals(f.CurrentRosterFingerprint,f.QuoteRosterFingerprint,StringComparison.Ordinal)||
               !Digest(f.OriginalSaveArchiveDigest)||
               String.IsNullOrEmpty(f.OriginalSaveDestination)||
               !f.OriginalSaveDestination.EndsWith(".sav",StringComparison.OrdinalIgnoreCase))
                return Refuse(out reason,"current_original_map_roster_or_save_digest_mismatch");
            if(f.LocalHumanSlot<1||f.LocalHumanSlot>8||
               f.AiSlot<1||f.AiSlot>8||f.LocalHumanSlot==f.AiSlot)
                return Refuse(out reason,"original_local_human_or_ai_slot_invalid");
            if(f.OfferId<1001||f.AcceptedOfferId!=f.OfferId||
               f.ArchiveNextId<=f.OfferId||f.InputEpoch==0||
               f.DecisionSequence==0||f.DecisionNonce==0||
               f.DecisionSequence>f.AcceptedSequenceHighWater||
               f.DecisionNonce>f.AcceptedNonceHighWater)
                return Refuse(out reason,"unique_trade_id_or_saved_human_input_invalid");
            if(!SupportedGood(f.OfferedGood)||
               !SupportedGood(f.RequestedGood)||
               f.OfferedGood==f.RequestedGood||
               f.OfferedAmount<1||f.RequestedAmount<1||
               f.OfferedAmount>100000||f.RequestedAmount>100000)
                return Refuse(out reason,"original_quote_goods_or_quantities_invalid");
            if(f.Before==null||f.NativeWarehouseCapacity==null||
               f.Before.Length!=4||f.NativeWarehouseCapacity.Length!=4||
               f.SourceReserveA<0||f.SourceReserveC<0)
                return Refuse(out reason,"original_native_cells_or_source_reserves_missing");
            for(int i=0;i<4;i++)
                if(f.Before[i]<0||
                   f.NativeWarehouseCapacity[i]<f.Before[i]||
                   f.NativeWarehouseCapacity[i]>int.MaxValue-200000)
                    return Refuse(out reason,"original_native_capacity_or_stock_"+i);
            if((long)f.Before[0]-f.OfferedAmount<f.SourceReserveA||
               (long)f.Before[2]-f.RequestedAmount<f.SourceReserveC||
               (long)f.Before[1]+f.OfferedAmount>f.NativeWarehouseCapacity[1]||
               (long)f.Before[3]+f.RequestedAmount>f.NativeWarehouseCapacity[3])
                return Refuse(out reason,"native_four_cell_reserve_or_capacity_violation");

            reason="precommit_facts_classified_only_not_a_write_capability";
            return true;
        }

        // AFTER actual original-game four-cell commit, SaveData readback and
        // two distinct cold processes. A post-hoc replay check cannot
        // authorize the ORIGINAL write that preceded it.
        internal static bool TryClassifyAfterTwoOriginalColdProcesses(
            SoloCommitAndColdFacts f,out string reason)
        {
            reason="no_original_cold_proof";
            if(f==null)return false;
            if(!f.PrecommitWitnessWasActuallyOwnedByOriginalEngine||
               !f.SameOriginalGameThreadAndMonitorAtNativeCommit||
               !f.OriginalFourCellNativeCommitExactlyOnce||
               !f.OriginalQuoteWasConsumedBeforeAnySecondAttempt)
                return Refuse(out reason,"actual_original_precommit_or_one_shot_missing");
            if(!f.OriginalManagedArchiveCommitted||
               !f.OriginalGameSaveDataAcknowledgedAndReadBack||
               !f.OriginalSaveFileAndCommittedCanonicalBytesMatch)
                return Refuse(out reason,"saved_original_canonical_commit_not_durable");
            if(!f.ColdOneOriginalProcessAndNativeGoodsMatch||
               !f.ColdTwoDifferentOriginalProcessAndNativeGoodsMatch||
               !f.BothColdLoadsPerformedZeroNativeGoodsWrites||
               !f.ExactlySameCanonicalMapRosterAndSourceSaveDigest)
                return Refuse(out reason,"two_real_original_cold_loads_or_replay_inconsistent");
            if(!f.NoPendingNativeJournalOrRecoveryQuarantine||
               !f.NoReplayTradeIdOrAdditionalAcceptedConsent||
               !f.NoSteamOrRemoteHumanPeer)
                return Refuse(out reason,"quarantined_replayed_or_remote_owner");
            if(f.AcceptedOfferId<1001||
               f.NativeTradeId!=f.AcceptedOfferId||
               f.PersistedCommittedId!=f.AcceptedOfferId||
               f.OriginalWarmProcessId<1||
               f.OriginalFirstColdPid<1||
               f.OriginalSecondColdPid<1||
               f.OriginalWarmProcessId==f.OriginalFirstColdPid||
               f.OriginalWarmProcessId==f.OriginalSecondColdPid||
               f.OriginalFirstColdPid==f.OriginalSecondColdPid||
               f.OriginalFirstGameGeneration<1||
               f.PersistedGeneration<1)
                return Refuse(out reason,"original_trade_id_process_identity_or_generation_invalid");
            if(!Digest(f.OriginalSaveSha256)||
               !String.Equals(f.OriginalSaveSha256,f.FirstColdSaveSha256,StringComparison.Ordinal)||
               !String.Equals(f.OriginalSaveSha256,f.SecondColdSaveSha256,StringComparison.Ordinal)||
               !Digest(f.CanonicalBeforeSha256)||
               !Digest(f.CanonicalCommittedSha256)||
               String.Equals(f.CanonicalBeforeSha256,f.CanonicalCommittedSha256,StringComparison.Ordinal))
                return Refuse(out reason,"original_cold_save_sha_or_canonical_commit_digest_mismatch");
            if(f.Before==null||f.AfterCommit==null||f.Saved==null||
               f.ColdOne==null||f.ColdTwo==null||
               f.Before.Length!=4||f.AfterCommit.Length!=4||
               f.Saved.Length!=4||f.ColdOne.Length!=4||f.ColdTwo.Length!=4)
                return Refuse(out reason,"original_four_native_cell_arrays_missing");
            bool changed=false;
            for(int i=0;i<4;i++)
            {
                if(f.Before[i]<0||f.AfterCommit[i]<0||
                   f.AfterCommit[i]!=f.Saved[i]||
                   f.AfterCommit[i]!=f.ColdOne[i]||
                   f.AfterCommit[i]!=f.ColdTwo[i])
                    return Refuse(out reason,"native_four_cell_save_or_cold_mismatch_"+i);
                if(f.Before[i]!=f.AfterCommit[i])changed=true;
            }
            if(!changed)
                return Refuse(out reason,"no_original_native_goods_mutation_observed");
            reason="postcommit_two_original_colds_readonly_classified";
            return true;
        }

        // No facts, classifier, normal GUI callback or private QA receipt
        // is itself the missing AUTHORIZATION CAPABILITY. The actual normal
        // release still needs trusted evidence+review and explicit permission.
        internal static bool AuthorizeNativeGoodsWrite(
            SoloPrecommitFacts ignored) { return false; }
        internal static bool AuthorizeSteamOrMultiplayer(
            SoloPrecommitFacts ignored) { return false; }
        internal static bool PromotePastOriginalSaveColdAsPrecommitPermission(
            SoloCommitAndColdFacts ignored) { return false; }
    }
}
