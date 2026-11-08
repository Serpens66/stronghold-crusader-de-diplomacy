// existing LoadData hook stages bytes; REAL original world/host
// session must be verified again before they can become live inputs.
using System;
using DiplomacyPersistenceCodec = StrongholdEuropeDE.Runtime.DiplomacyStateCodec;
using System.IO;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private void ObserveStagedOriginalLoad(byte[] payload, bool strictMp = false)
        {
            _pendingOriginalMpLoadBytes = null;
            if (payload == null || payload.Length == 0 || DiplomacyPersistenceCodec.IsInactive(payload))
                return;
            try
            {
                var state = DiplomacyPersistenceCodec.Deserialize(payload, _rules);
                if (state == null || state.CanonicalTradeState == null)
                {
                    if (strictMp)
                        throw new InvalidDataException("Active load has no MP canonical owner");
                    return;
                }

                CanonicalTradeStatePersistence.Validate(state);
                CanonicalModel model = CanonicalTradeCodec.Decode(state.CanonicalTradeState);
                if ((model.HumanMask & (model.HumanMask - 1)) == 0)
                {
                    if (strictMp)
                        throw new InvalidDataException("Active load lacks a multi-human canonical scope");
                    return;
                }

                if (model.Quarantined || state.PendingTransfer != null || state.PendingPackage != null || state.PendingTeamTransition != null || model.Archive.PendingNativeId != 0 || model.Mailbox.Intents.Any(x => x.State == TradeIntentStatus.Processing))
                    throw new InvalidDataException("Ambiguous original MP native journal on cold load");
                _pendingOriginalMpLoadBytes = (byte[])payload.Clone();
            }
            catch (Exception)
            {
                _pendingOriginalMpLoadBytes = null;
                if (strictMp || _mpConsentMode || _mpBootstrapRequested)
                    _mpQuarantined = true; // never replace ambiguous MP with a new model
            }
        }

        // MUST be invoked ONLY after complete original Steam roster, native map
        // identity and new single  ChoreSession are live under the old monitor.
        private void RestoreProvenOriginalMpLoad(OriginalMultiplayerConsentFrame observed, Guid globallyStableMatchScope, ulong persistedGlobalTerm)
        {
            if (!IsNativeDiplomacySectionHeld || observed == null || _pendingOriginalMpLoadBytes == null || _signed == null || !_signed.Live || !_signed.Ready)
                throw new InvalidOperationException("Cold-load MP original identity not reattested");
            var old = DiplomacyPersistenceCodec.Deserialize(_pendingOriginalMpLoadBytes, _rules);
            CanonicalTradeStatePersistence.Validate(old);
            var restored = CanonicalTradeReducer.RestoreOriginalMpConsentOnly(old.CanonicalTradeState, observed, globallyStableMatchScope, persistedGlobalTerm);
            if (!old.MapIdentity.Same(observed.FullOriginalMapIdentity) || old.LocalPlayerId != observed.LocalOriginalHumanSlot || old.TeamPartition.Count != observed.Players().Length || old.TeamPartition.Any(x => SHCDESE.API.GamePlayerManagerAPI.Instance.GetPlayerTeam(x.Key) != x.Value))
                throw new InvalidDataException("Cold MP outer native identity/teams differ");
            _originalOuterSeed = old;
            _canonical = restored;
            _canonicalSaved = restored.SaveBytes();
            CanonicalModel model = CanonicalTradeCodec.Decode(_canonicalSaved);
            _archive = model.Archive.Copy();
            _mailbox = model.Mailbox.Copy();
            _mailbox.Rebase(model.LastSharedTick);
            _pendingOriginalMpLoadBytes = null;
            _mpConsentMode = true;
            // The saved original actor highwater is retained; new ordered signed
            // InputEpoch+1 required. Until then NO new .sav replacement can occur.
            _confirmedOuterSaveBytes = null;
        }
    }
}
