// Real existing Schema19 ExtendedState envelope: no new SaveData callback and no
// fake legacy coalition/team snapshot. The save owner must supply a complete,
// observed native valid outer save seed; otherwise no MP SaveData is written.
using System;
using DiplomacyPersistenceCodec = StrongholdEuropeDE.Runtime.DiplomacyStateCodec;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private byte[] _confirmedOuterSaveBytes;
        private byte[] _pendingOriginalMpLoadBytes;
        private bool _mpConsentMode;
        private bool _originalOwnerProjectionStable;
        private LegacyDiplomacySave ProjectToExistingOriginalSaveEnvelope(LegacyDiplomacySave actuallyObservedOuter, PendingConsentPublication staged, OriginalMultiplayerConsentFrame world, out byte[] serializedOuter)
        {
            serializedOuter = null;
            if (actuallyObservedOuter == null || staged == null || world == null || !IsNativeDiplomacySectionHeld || actuallyObservedOuter.LocalPlayerId != world.LocalOriginalHumanSlot || actuallyObservedOuter.MapFingerprint != world.SharedMapFingerprint || actuallyObservedOuter.RosterFingerprint != world.SharedRosterFingerprint || actuallyObservedOuter.LastProcessedDay > world.NativeGameDay || actuallyObservedOuter.PendingTransfer != null || actuallyObservedOuter.PendingPackage != null || actuallyObservedOuter.PendingTeamTransition != null || actuallyObservedOuter.MapIdentity == null || !actuallyObservedOuter.MapIdentity.Same(world.FullOriginalMapIdentity))
                throw new InvalidDataException("Missing native-proven outer coalition/team/price projection");
            // This validates exact ORIGINAL full native team/coalition and 21-price
            // legacy compatibility. It will fail if the old solo-only bootstrap left
            // an incomplete roster. Never fabricate empty/neutral fake teams.
            byte[] originalOuter = DiplomacyPersistenceCodec.Serialize(actuallyObservedOuter, _rules);
            LegacyDiplomacySave stagedOuter = DiplomacyPersistenceCodec.Deserialize(originalOuter, _rules);
            CanonicalModel stagedModel = CanonicalTradeCodec.Decode(staged.CandidateBytes());
            stagedOuter.LastProcessedDay = stagedModel.Day;
            stagedOuter.NativeDayAnchor = world.NativeGameDay;
            foreach (var relation in stagedOuter.Relations)
                relation.LastProcessedDay = stagedModel.Day;
            if (stagedModel.Day != world.NativeGameDay || stagedModel.Archive.MapFingerprint != stagedOuter.MapFingerprint || stagedModel.Archive.RosterFingerprint != stagedOuter.RosterFingerprint)
                throw new InvalidDataException("Original SaveData/canonical mismatch");
            stagedOuter.CanonicalTradeState = staged.CandidateBytes();
            stagedOuter.TradeArchive = stagedModel.Archive.Copy();
            stagedOuter.TradeMailbox = stagedModel.Mailbox.Copy();
            CanonicalTradeStatePersistence.Validate(stagedOuter);
            byte[] bytes = DiplomacyPersistenceCodec.Serialize(stagedOuter, _rules);
            var observed = DiplomacyPersistenceCodec.Deserialize(bytes, _rules);
            CanonicalTradeStatePersistence.Validate(observed);
            if (!ExistingSaveReceiptGate.ExactBytes(observed.CanonicalTradeState, staged.CandidateBytes()))
                throw new InvalidDataException("Serialized REAL outer SaveData changed the canonical bytes");
            serializedOuter = bytes;
            return observed;
        }
    }
}
