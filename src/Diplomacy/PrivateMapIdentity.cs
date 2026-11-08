#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private void TryAdoptVerifiedLegacyBaseline(LegacyDiplomacySave saved, byte[] payload)
        {
            if (saved.MapFingerprint == _mapFingerprint)
                return;
            string why;
            if (!DisposableGameScope.IsEnabled(out why) || _mapIdentity == null || string.IsNullOrEmpty(_loadedSavePath) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(_loadedSavePath)), Path.Combine(DisposableGameScope.ProfileRoot, "Saves"), StringComparison.OrdinalIgnoreCase) || saved.RosterFingerprint != _rosterFingerprint || saved.LocalPlayerId != _localPlayer || !IsSoloEngineSession(out why) || !PayloadCameFromLoadedArchive(payload))
                return;
            if (DisposableGameScope.HashFile(_loadedSavePath) != "01732528743451e3d97e277fe9d371874a3585823a7123d6b7abae7d4387adbc" || saved.MapFingerprint != "5e940b00232aaf0c450875c2564aa54506c0d9fbc908938670aa24f2c626debf" || _rosterFingerprint != "0203823b86089b18713b22cd909da2ba20d819e1c268c0bc86fd061bf0945cb1" || _localPlayer != 1 || (saved.TradeArchive != null && saved.TradeArchive.Records.Count != 0) || saved.PendingTransfer != null || saved.PendingPackage != null)
                return;
            _mapFingerprint = saved.MapFingerprint;
            Log("trade_session", "status=verified_legacy_baseline_identity_migrated|source_identity=" + _mapIdentity.Fingerprint + "|native_trade_history_empty=true");
        }
    }
}
#endif
