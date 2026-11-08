// mod ai offer - Explicit MOD-AUTHORED diplomatic AI offer provenance, NEVER an
// original-CPU diplomatic Chore or native goods authority.
// Genuine original AIC numeric demand is a source SIGNAL only.
// The human's exact local original-engine consent is independently bound
// by the existing CanonicalTrustedHumanInput and persisted input highwater.
using System;
using System.Linq;
#if !MOD_AI_OFFER_POLICY_TEST
using StrongholdEuropeDE.TradeModels;
#endif

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ModAiHumanOfferFacts
    {
        internal bool ModAiAuthoredOffer;
        internal bool LiveOriginalAiAicSignalObserved;
        internal bool OriginalAiSlotAlive;
        internal bool OriginalLocalHumanSlot;
        internal bool ExactCanonicalMapAndRoster;
        internal bool SameLiveOriginalMapGeneration;
        internal bool NoOtherNativePreparedOrQuarantined;
        internal bool OfferIdentityNotConsumedInArchive;
        internal bool GoodsAreOriginalCatalogAndQuoteIsWellFormed;
        internal int OriginalLordId;
        internal string OriginalAicSignalSha, MapSha, RosterSha;
        internal byte FromAi, ToHuman, LocalHumanSlot;
        internal int IssuedDay, Day, ExpiresDay, Tier;
        internal long IssuedClock, Clock, ExpiresClock, DecisionClock;
        internal long MapGeneration;
        internal ulong QuoteId, ArchiveNextId, InputEpoch;
        internal ulong DecisionSequence, DecisionNonce, AcceptedInputSequence, AcceptedInputNonce;
        internal byte DecisionSlot;
        internal bool DecisionIsAi;
        internal int OfferedAmount, RequestedAmount;
        internal ushort OfferedGood, RequestedGood;
        internal bool StatePending, StateAccepted;
    }

    internal static class ModAiHumanOfferOrigin
    {
        private static bool Deny(out string why,string reason) {
            why=reason;return false;
        }
        private static bool Sha(string hash) {
            if(hash==null||hash.Length!=64)return false;
            foreach(char c in hash)
                if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;
            return true;
        }
        private static bool Core(ModAiHumanOfferFacts f,out string why)
        {
            why="mod_ai_offer_facts_absent";
            if(f==null)return false;
            if(!f.ModAiAuthoredOffer)
                return Deny(out why,"offer_not_from_mod_ai_scheduler");
            if(!f.LiveOriginalAiAicSignalObserved || f.OriginalLordId<1 ||
               f.OriginalLordId>37 || !Sha(f.OriginalAicSignalSha))
                return Deny(out why,"original_ai_aic_numeric_signal_unobserved");
            if(!f.OriginalAiSlotAlive||!f.OriginalLocalHumanSlot)
                return Deny(out why,"no_current_ai_to_original_local_human");
            if(!f.ExactCanonicalMapAndRoster||!f.SameLiveOriginalMapGeneration||
               !Sha(f.MapSha)||!Sha(f.RosterSha)||f.MapGeneration<=0)
                return Deny(out why,"current_original_map_roster_generation_unbound");
            if(!f.NoOtherNativePreparedOrQuarantined)
                return Deny(out why,"other_original_native_transaction_pending");
            if(!f.OfferIdentityNotConsumedInArchive)
                return Deny(out why,"quote_replay_or_archive_collision");
            if(!f.GoodsAreOriginalCatalogAndQuoteIsWellFormed)
                return Deny(out why,"original_quote_goods_or_terms_invalid");
            if(f.FromAi<1||f.FromAi>8||f.ToHuman<1||f.ToHuman>8||
               f.FromAi==f.ToHuman||f.LocalHumanSlot!=f.ToHuman)
                return Deny(out why,"ai_or_local_human_slot_unbound");
            if(f.QuoteId<1001||f.QuoteId>ulong.MaxValue-4||
               (f.QuoteId-1001UL)%4UL!=0||f.InputEpoch==0)
                return Deny(out why,"canonical_quote_identity_invalid");
            if(f.Tier<0||f.Tier>2||f.OfferedGood==f.RequestedGood||
               f.OfferedGood==0||f.RequestedGood==0||
               f.OfferedAmount<1||f.OfferedAmount>100000||
               f.RequestedAmount<1||f.RequestedAmount>100000)
                return Deny(out why,"current_quote_terms_invalid");
            if(f.IssuedDay<1||f.Day<f.IssuedDay||f.Day>f.ExpiresDay||
               f.ExpiresDay-f.IssuedDay>15||f.IssuedClock<0||
               f.Clock<f.IssuedClock||f.Clock>f.ExpiresClock||
               f.ExpiresClock-f.IssuedClock>2400)
                return Deny(out why,"quote_no_longer_current");
            why="mod_diplomatic_offer_with_original_aic_signal_only";
            return true;
        }

        internal static bool TryClassifyModAiDraft(ModAiHumanOfferFacts f,
            out string reason)
        {
            if(!Core(f,out reason))return false;
            if(!f.StatePending||f.StateAccepted||
               f.QuoteId!=f.ArchiveNextId||
               f.DecisionSlot!=0||f.DecisionIsAi||
               f.DecisionSequence!=0||f.DecisionNonce!=0||
               f.DecisionClock!=0)
                return Deny(out reason,"new_mod_ai_draft_had_foreign_decision_or_id");
            reason="mod_generated_ai_offer_not_original_cpu_chore";
            return true;
        }

        internal static bool TryClassifyAcceptedByOriginalLocalHuman(
            ModAiHumanOfferFacts f,out string reason)
        {
            if(!Core(f,out reason))return false;
            if(!f.StateAccepted||f.StatePending||
               f.QuoteId>=f.ArchiveNextId||
               f.DecisionIsAi||f.DecisionSlot!=f.ToHuman||
               f.DecisionSequence==0||f.DecisionNonce==0||
               f.AcceptedInputSequence<f.DecisionSequence||
               f.AcceptedInputNonce<f.DecisionNonce||
               f.DecisionClock<f.IssuedClock||f.DecisionClock>f.ExpiresClock||
               f.DecisionClock>f.Clock)
                return Deny(out reason,"original_local_human_decision_not_bound_to_mod_ai_offer");
            reason="mod_ai_initiative_and_local_original_human_consent_bound_no_native_grant";
            return true;
        }

        // Neither synthetic success nor a genuine original AIC signal grants
        // vanilla original-CPU offer origin, a native goods write or Steam MP.
        internal static bool IsOriginalCpuDiplomaticChore(
            ModAiHumanOfferFacts ignored) { return false; }
        internal static bool AuthorizeNativeGoodsCommit(
            ModAiHumanOfferFacts ignored) { return false; }
        internal static bool AuthorizeMultiplayer(
            ModAiHumanOfferFacts ignored) { return false; }
    }

#if !MOD_AI_OFFER_POLICY_TEST
    internal sealed partial class CanonicalTradeReducer
    {
        // Called only in the existing canonical reducer from the current
        // original-engine snapshot and canonical stored offer. No new ledger.
        private static ModAiHumanOfferFacts ModAiOriginFacts(
            CanonicalModel model,CanonicalTradeSnapshot snapshot,
            CanonicalHumanOffer offer)
        {
            CanonicalOriginalAiEconomy aic=null;
            bool observed=snapshot!=null&&offer!=null&&
                snapshot.TryOriginalAiEconomy(offer.From,out aic);
            var from=offer==null||snapshot==null?null:snapshot.Player(offer.From);
            var to=offer==null||snapshot==null?null:snapshot.Player(offer.To);
            bool current=model!=null&&snapshot!=null&&model.Archive!=null&&
                model.Mailbox!=null&&model.Day==snapshot.Day&&
                String.Equals(model.Archive.MapFingerprint,snapshot.MapFingerprint,
                    StringComparison.Ordinal)&&
                String.Equals(model.Archive.RosterFingerprint,
                    snapshot.RosterFingerprint,StringComparison.Ordinal);
            return new ModAiHumanOfferFacts {
                ModAiAuthoredOffer=offer!=null&&offer.AiGenerated&&
                    offer.RootId==0&&offer.ParentId==0&&offer.Depth==0,
                LiveOriginalAiAicSignalObserved=observed,
                OriginalLordId=aic==null?0:aic.OriginalLordId,
                OriginalAicSignalSha=aic==null?null:aic.OriginalAicSignalFingerprint,
                OriginalAiSlotAlive=from!=null&&!from.Human&&from.Alive,
                OriginalLocalHumanSlot=to!=null&&to.Human&&to.Alive&&
                    to.SteamId==0&&snapshot.LocalOfflineHumanSlot==offer.To&&
                    Human(model,offer.To),
                ExactCanonicalMapAndRoster=current,
                SameLiveOriginalMapGeneration=snapshot!=null&&
                    snapshot.MapGeneration>0,
                NoOtherNativePreparedOrQuarantined=model!=null&&
                    !model.Quarantined&&model.Archive!=null&&
                    model.Archive.PendingNativeId==0&&
                    !model.Archive.Records.Any(x=>x.State==TradeDisposition.PreparingNative),
                OfferIdentityNotConsumedInArchive=model!=null&&offer!=null&&
                    model.Archive!=null&&model.Mailbox!=null&&
                    model.Archive.Record(offer.Id)==null&&
                    !model.Mailbox.Intents.Any(x=>x.Id==offer.Id),
                GoodsAreOriginalCatalogAndQuoteIsWellFormed=offer!=null&&
                    GoodCatalog.Find(offer.OfferedGood)!=null&&
                    GoodCatalog.Find(offer.RequestedGood)!=null&&
                    offer.OfferedGood!=offer.RequestedGood,
                MapSha=snapshot==null?null:snapshot.MapFingerprint,
                RosterSha=snapshot==null?null:snapshot.RosterFingerprint,
                MapGeneration=snapshot==null?0:snapshot.MapGeneration,
                FromAi=offer==null?(byte)0:offer.From,
                ToHuman=offer==null?(byte)0:offer.To,
                LocalHumanSlot=snapshot==null?(byte)0:snapshot.LocalOfflineHumanSlot,
                IssuedDay=offer==null?0:offer.IssuedDay,
                Day=model==null?0:model.Day,
                ExpiresDay=offer==null?0:offer.ExpiresDay,
                Tier=offer==null?0:offer.Tier,
                IssuedClock=offer==null?0:offer.IssuedClock,
                Clock=model==null||model.Mailbox==null?0:model.Mailbox.Clock,
                ExpiresClock=offer==null?0:offer.ExpiresClock,
                DecisionClock=offer==null?0:offer.DecisionClock,
                QuoteId=offer==null?0:offer.Id,
                ArchiveNextId=model==null||model.Archive==null?0:model.Archive.NextId,
                InputEpoch=model==null?0:model.InputEpoch,
                DecisionSlot=offer==null?(byte)0:offer.DecisionSlot,
                DecisionIsAi=offer!=null&&offer.DecisionIsAi,
                DecisionSequence=offer==null?0:offer.DecisionSequence,
                DecisionNonce=offer==null?0:offer.DecisionNonce,
                AcceptedInputSequence=model==null||offer==null||offer.To<1||offer.To>8||(model.InputSequence==null)?
                    0:model.InputSequence[offer.To],
                AcceptedInputNonce=model==null||offer==null||offer.To<1||offer.To>8||(model.InputNonce==null)?
                    0:model.InputNonce[offer.To],
                OfferedGood=offer==null?(ushort)0:offer.OfferedGood,
                RequestedGood=offer==null?(ushort)0:offer.RequestedGood,
                OfferedAmount=offer==null?0:offer.OfferedAmount,
                RequestedAmount=offer==null?0:offer.RequestedAmount,
                StatePending=offer!=null&&offer.State==CanonicalHumanConsentState.Pending,
                StateAccepted=offer!=null&&offer.State==CanonicalHumanConsentState.Accepted
            };
        }
    }
#endif
}
