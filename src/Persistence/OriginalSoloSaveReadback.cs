// original save readback – Release-compiled PASSIVE original solo SaveData readback witness.
// Existing original EngineInterface.SaveSaveGame retains its native path.
// No change to __result, __0, NativeWrite, atomic/journal, Steam or SaveData
// return values. An observed original file alone NEVER authorizes trade.
using System;
using System.IO;
using System.Threading;
using SHCDESE.API;
using SHCDESE.API.Components.SaveData;
using SHCDESE.API.Components.Archive;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private sealed class OriginalSaveWitness
        {
            internal string Destination;
            internal int Thread;
            internal long Generation;
            internal string Map, Roster;
            internal byte[] IssuedPrimary;
            internal bool CallbackFailed;
        }

        private OriginalSaveWitness _passiveSave;
        private long _originalReadbackGeneration = -1;
        private string _originalReadbackMap, _originalReadbackRoster;
        private string _originalReadbackOriginalPath;
        private string _readbackStatus = "not_observed";

        private void ClearOriginalSaveEvidence()
        {
            _originalReadbackGeneration = -1;
            _originalReadbackMap = null;
            _originalReadbackRoster = null;
            _originalReadbackOriginalPath = null;
            _readbackStatus = "not_observed";
        }

        // Called only from the original SaveSaveGame Harmony prefix for
        // the normal solo path, never the MP or private atomic-promotion path.
        // Failure only declines diagnostics; the original save proceeds.
        private void ObserveOriginalSoloSavePrefix(string path, bool isMapExport)
        {
            if (_privateSaveScope || _mpConsentMode || _mpBootstrapRequested ||
                _mpQuarantined || _pendingOriginalMpLoadBytes != null)
                return;
            if (!Monitor.TryEnter(_gate))
                return;
            try
            {
                // ANY new original SaveSaveGame attempt invalidates the previous
                // readback, including map export or an unsuccessful original save.
                // This is a diagnostic only, never a trade/save authority.
                ClearOriginalSaveEvidence();
                _passiveSave = null;
                _readbackStatus = "normal_solo_original_save_prefix_refused";
                if (isMapExport || !_mapActive || _mapDisabled ||
                    _mapBootstrapPending || _saveSuppressed ||
                    _saveFailurePending != null || !_saveBridgeInstalled ||
                    !_saveHandlerRegistered || !OriginalMpSaveFenceReady ||
                    _pendingPackage != null || _nativeTransferProbe != null ||
                    _pendingTeamTransition != null || _tradeFaulted ||
                    _unattestedSavedNativeTrade || _inventoryRestorePending ||
                    _goodsTransfersUnavailable)
                    return;
                if (string.IsNullOrWhiteSpace(path) ||
                    !string.Equals(Path.GetExtension(path),".sav",
                                   StringComparison.OrdinalIgnoreCase))
                    return;
                string full = Path.GetFullPath(path);
                if (!Path.IsPathRooted(full) ||
                    (File.Exists(full) &&
                     (File.GetAttributes(full) & FileAttributes.ReparsePoint)!=0))
                    return;
                if (string.IsNullOrEmpty(_mapFingerprint) ||
                    string.IsNullOrEmpty(_rosterFingerprint) ||
                    _sessionGeneration <= 0)
                    return;
                // The real original game owns all saving. This passive
                // probe cannot manufacture an original callback or Save ACK.
                _passiveSave = new OriginalSaveWitness {
                    Destination=full,
                    Thread=Environment.CurrentManagedThreadId,
                    Generation=_sessionGeneration,
                    Map=_mapFingerprint, Roster=_rosterFingerprint
                };
                _readbackStatus = "original_save_callback_pending";
            }
            catch(Exception)
            {
                _passiveSave = null;
                _readbackStatus = "normal_save_prefix_probe_failed";
            }
            finally { Monitor.Exit(_gate); }
        }

        // Observe the EXACT array returned by the existing primary SaveData
        // callback. No second SaveData owner and no encoder override.
        // Called inside the original SaveData owner lock.
        private void ObservePrimarySaveCallback(SaveContext context, byte[] payload)
        {
            try
            {
                var w = _passiveSave;
                if (w == null) return;
                if (context == null || !context.IsSaveFile ||
                    context.IsMapEditorSave || payload == null ||
                    payload.Length == 0 || payload.Length > 4194304 ||
                    w.Thread != Environment.CurrentManagedThreadId ||
                    w.Generation != _sessionGeneration ||
                    !string.Equals(w.Map,_mapFingerprint,StringComparison.Ordinal) ||
                    !string.Equals(w.Roster,_rosterFingerprint,StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFullPath(context.FilePath),
                        w.Destination,StringComparison.OrdinalIgnoreCase))
                {
                    w.CallbackFailed = true;
                    return;
                }
                if (w.IssuedPrimary != null)
                {
                    w.CallbackFailed = true; // duplicated primary callbacks not trusted
                    return;
                }
                w.IssuedPrimary = (byte[])payload.Clone();
            }
            catch(Exception)
            {
                if (_passiveSave != null)
                    _passiveSave.CallbackFailed = true;
            }
        }

        // Original Harmony postfix observes the actual original Save result
        // and physical .sav archive, but never alters original result/path.
        private void ObserveOriginalSoloSavePostfix(string path,bool originalResult)
        {
            if (!Monitor.TryEnter(_gate))
                return;
            try
            {
                OriginalSaveWitness w=_passiveSave;
                _passiveSave=null;
                if(w==null)return;
                _readbackStatus="original_save_unverified";
                if (!originalResult || w.CallbackFailed ||
                    w.IssuedPrimary==null ||
                    w.Thread!=Environment.CurrentManagedThreadId ||
                    w.Generation!=_sessionGeneration ||
                    _mapDisabled || _saveSuppressed ||
                    _saveFailurePending!=null ||
                    _pendingPackage!=null || _nativeTransferProbe!=null ||
                    _pendingTeamTransition!=null ||
                    !string.Equals(w.Map,_mapFingerprint,StringComparison.Ordinal) ||
                    !string.Equals(w.Roster,_rosterFingerprint,StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFullPath(path),w.Destination,
                        StringComparison.OrdinalIgnoreCase))
                    return;
                var fi=new FileInfo(w.Destination);
                if (!fi.Exists || fi.Length < 1000000L || fi.Length > 128000000L ||
                    (fi.Attributes & FileAttributes.ReparsePoint)!=0)
                    return;
                MapArchive archive;
                if (!MapArchive.TryLoad(w.Destination,out archive) ||
                    archive==null)
                    return;
                using(archive)
                {
                    byte[] saved=archive.TryReadBinaryFile(
                        "_SE_ModData_"+GeneratedDiplomacyContract.SaveDataIdentifier+
                        ".msgpack",true);
                    if (!ExistingSaveReceiptGate.ExactBytes(saved,w.IssuedPrimary))
                    {
                        _readbackStatus="original_persisted_callback_bytes_mismatch";
                        return;
                    }
                    LegacyDiplomacySave decoded=
                        DiplomacyStateCodec.Deserialize(saved,_rules);
                    if(decoded==null ||
                       decoded.LocalPlayerId!=_localPlayer ||
                       !string.Equals(decoded.MapFingerprint,w.Map,StringComparison.Ordinal) ||
                       !string.Equals(decoded.RosterFingerprint,w.Roster,
                                      StringComparison.Ordinal))
                    {
                        _readbackStatus="original_save_map_or_roster_mismatch";
                        return;
                    }
                }
                // A separately witnessed original SaveData owner, limited
                // to the current map; NOT native goods durability/authority.
                _originalReadbackGeneration=w.Generation;
                _originalReadbackMap=w.Map;
                _originalReadbackRoster=w.Roster;
                _originalReadbackOriginalPath=w.Destination;
                _readbackStatus="original_solo_save_callback_archive_match";
                Log("original_solo_save",
                    "status=normal_original_callback_archive_bytes_verified" +
                    "|goods_authorized=false|mp_authorized=false");
            }
            catch(Exception e)
            {
                _readbackStatus="original_save_probe_exception_"+e.GetType().Name;
            }
            finally { Monitor.Exit(_gate); }
        }

        internal bool HasPassiveOriginalSaveReadback()
        {
            // The original save evidence pure contract can never grant native goods or MP. Its
            // observed fields come ONLY from our actual original Save hooks.
            // In original solo release, genuine one-human engine ownership is checked separately.
            var facts = new OriginalSoloSaveFacts {
                OriginalCallbackArchiveExact =
                    _readbackStatus=="original_solo_save_callback_archive_match",
                OriginalSaveHooksOwned = _saveBridgeInstalled &&
                    _saveHandlerRegistered && OriginalMpSaveFenceReady,
                ActiveCurrentMap = _mapActive && !_mapDisabled &&
                    !_mapBootstrapPending && !_saveSuppressed,
                NoKnownExplicitMpState = !_mpConsentMode &&
                    !_mpBootstrapRequested && !_mpQuarantined &&
                    _pendingOriginalMpLoadBytes == null,
                NoSaveOrTradeOrRecoveryInProgress = _activeSave == null &&
                    _pendingPackage == null && _pendingTeamTransition == null &&
                    _nativeTransferProbe == null && !_tradeFaulted &&
                    !_unattestedSavedNativeTrade && !_inventoryRestorePending &&
                    !_goodsTransfersUnavailable && _saveFailurePending == null,
                CurrentGeneration = _sessionGeneration,
                ReadbackGeneration = _originalReadbackGeneration,
                CurrentMap = _mapFingerprint,
                ReadbackMap = _originalReadbackMap,
                CurrentRoster = _rosterFingerprint,
                ReadbackRoster = _originalReadbackRoster,
                ReadbackOriginalPath = _originalReadbackOriginalPath
            };
            string diagnostic;
            bool observedOnly = OriginalSoloSaveEvidence.TryAcceptReadOnly(
                facts, out diagnostic);
            if (OriginalSoloSaveEvidence.AuthorizeNativeGoodsCommit(facts) ||
                OriginalSoloSaveEvidence.AuthorizeMultiplayer(facts))
                throw new InvalidOperationException("Unexpected save evidence write/MP grant");
            return observedOnly;
        }

#if DIPLOMACY_PRIVATE_TESTS
        // Explicit disposable QA: exercise the **normal** original SaveData
        // callback path for one newly named isolated save, without the
        // private AtomicSaveFile promotion. This toggles only save routing
        // for the synchronous original SaveSaveGame invocation, NEVER
        // native goods or multiplayer authority.
        private static float _qaStartedAt=-1f;
        private static bool _qaSaveIssued;
        internal static bool TryQaOriginalNormalSave(out bool complete,out string note)
        {
            complete=false; note=null;
            if (Environment.GetEnvironmentVariable(
                "DIPLOMACY_QA_ORIGINAL_SAVE_READBACK")!="1" ||
                Environment.GetEnvironmentVariable("DIPLOMACY_HEADLESS_TEST")!="1")
                return false;
            string scope;
            if (!DisposableGameScope.IsEnabled(out scope))
            {
                complete=true;note="status=refused|reason=private_test_scope";
                return true;
            }
            var owner=QaOwner();
            if (owner==null || !owner._mapActive || owner._mapBootstrapPending)
                return false;
            if(owner._mapDisabled || owner._tradeFaulted ||
               owner._unattestedSavedNativeTrade)
            {
                complete=true;note="status=refused|reason=original_map_unavailable";
                return true;
            }
            if (!owner._saveBridgeInstalled || !owner._privateSaveScope ||
                !owner._saveHandlerRegistered)
            {
                complete=true;note="status=refused|reason=private_original_save_bridge_missing";
                return true;
            }
            if(_qaStartedAt<0f)
                _qaStartedAt=UnityEngine.Time.realtimeSinceStartup;
            if(UnityEngine.Time.realtimeSinceStartup-_qaStartedAt>65f)
            {
                complete=true;note="status=refused|reason=normal_original_callback_timeout";
                return true;
            }
            string filename="OriginalSaveCallback.sav";
            string absolute=Path.Combine(DisposableGameScope.ProfileRoot,"Saves",filename);
            if(owner.HasPassiveOriginalSaveReadback() &&
                File.Exists(absolute))
            {
                complete=true;
                note="status=normal_original_save_readback_verified" +
                    "|original_callback_bytes_equal_disk=true" +
                    "|normal_release_native_goods_authorized=false" +
                    "|private_test_profile_only=true" +
                    "|native_goods_writes=0";
                return true;
            }
            if (_qaSaveIssued) return false; // one-shot disposable save, no repeated disk writes
            string reason;
            if (!IsSoloEngineSession(out reason))return false;
            // The original headless SavePause handshake needs TWO phases:
            // first call acquires a safe engine pause and returns false, without
            // attempting SaveSaveGame. Only arm the one-shot marker once the
            // genuine original game reports its paused engine is ready.
            if (!HeadlessQaDriver.PrepareSave()) return false;
            bool saveScope=owner._privateSaveScope;
            _qaSaveIssued=true;
            try
            {
                owner._privateSaveScope=false;
                owner.QaSave(filename); // Existing original engine method
            }
            finally { owner._privateSaveScope=saveScope; }
            return false;
        }
#endif
    }
}
