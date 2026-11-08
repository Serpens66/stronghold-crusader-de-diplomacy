// original save only private QA ONLY: original SaveSaveGame to separate disposable profile.
// NO offer, native good change, multiplayer packet, trade journal or privilege.
// Reuses the previously proven GameSave/Harmony bridge under its own locks.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private static float _saveOnlyStartAt = -1f;
        private const string BaselineName = "OriginalSoloBaseline.sav";

        internal static bool TryOriginalSaveOnly(out bool finished, out string observation)
        {
            finished = false;
            observation = null;
            if (Environment.GetEnvironmentVariable("DIPLOMACY_QA_ORIGINAL_SAVE_ONLY") != "1" ||
                Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST") != "1")
                return false;
            string why;
            if (!DisposableGameScope.IsEnabled(out why))
            {
                finished = true;
                observation = "status=saveonly_refused|reason=private_scope_absent|native_goods_writes=0";
                return true;
            }
            DiplomacyFeature owner = QaOwner();
            if (owner == null || !owner._mapActive || owner._mapBootstrapPending)
                return false;
            if (owner._mapDisabled || owner._saveFailurePending != null ||
                owner._saveSuppressed || owner._unattestedSavedNativeTrade ||
                owner._tradeFaulted || owner._pendingPackage != null)
            {
                finished = true;
                observation = "status=saveonly_refused|reason=active_recovery_or_quarantine|native_goods_writes=0";
                return true;
            }
            if (!owner._saveBridgeInstalled || !owner._saveHandlerRegistered ||
                !owner._privateSaveScope)
            {
                finished = true;
                observation = "status=saveonly_refused|reason=save_bridge_not_private|native_goods_writes=0";
                return true;
            }
            if (!IsSoloEngineSession(out why))
                return false;
            if (_saveOnlyStartAt < 0f)
                _saveOnlyStartAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _saveOnlyStartAt > 85f)
            {
                finished = true;
                observation = "status=saveonly_refused|reason=bounded_original_save_timeout|native_goods_writes=0";
                return true;
            }
            string path = Path.Combine(DisposableGameScope.ProfileRoot,"Saves",BaselineName);
            string exact = Path.GetFullPath(path);
            if (!String.Equals(Path.GetDirectoryName(exact),
                Path.GetFullPath(Path.Combine(DisposableGameScope.ProfileRoot,"Saves")),
                StringComparison.OrdinalIgnoreCase))
            {
                finished = true;
                observation = "status=saveonly_refused|reason=profile_save_boundary|native_goods_writes=0";
                return true;
            }

            if (File.Exists(exact) && owner._coherentSaveGeneration == owner._sessionGeneration &&
                owner._sessionGeneration > 0)
            {
                var file = new FileInfo(exact);
                if (file.Length < 1000000 || file.Length > 25000000)
                {
                    finished = true;
                    observation = "status=saveonly_refused|reason=unexpected_original_save_length|native_goods_writes=0";
                    return true;
                }
                // The original save bridge must already have promoted this
                // exact new filename. File existence without a matching
                // current-generation receipt never reaches this branch.
                finished = true;
                observation = "status=original_save_verified" +
                    "|current_generation_original_save_receipt=true" +
                    "|save_file_in_disposable_profile=true" +
                    "|real_original_save_write=true" +
                    "|native_goods_writes=0" +
                    "|trade_or_consent_performed=false" +
                    "|normal_release_native_write_authority=false";
                return true;
            }
            // QaSave uses Director SafeToSave, the previously installed
            // original managed SaveSaveGame, 3 Harmony owned Save-hooks
            // and shared original engine monitor; no new save hook.
            bool attempt = owner.QaSave(BaselineName);
            if (!attempt)
                return false;
            // Require a second on-loop check of current generation + file.
            return false;
        }
    }
}
#endif
