// Stable source identity belongs to the checked ExtendedState save envelope. No source
// paths, translated names, live economy or uninitialised slot order are saved.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.API.Components.Archive;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class PersistentMapIdentity
    {
        private readonly byte[] _source, _layout;
        internal readonly long SourceLength;
        internal PersistentMapIdentity(byte[] source, byte[] layout, long length)
        {
            if (source == null || source.Length != 32 || layout == null || layout.Length != 32 || length < 35000 || length > 9000000)
                throw new InvalidDataException("Invalid original map identity");
            _source = (byte[])source.Clone();
            _layout = (byte[])layout.Clone();
            SourceLength = length;
        }

        internal PersistentMapIdentity Copy()
        {
            return new PersistentMapIdentity(_source, _layout, SourceLength);
        }

        internal bool HasLayout(byte[] layout)
        {
            return layout != null && _layout.SequenceEqual(layout);
        }

        internal bool HasSameSourceContent(PersistentMapIdentity other)
        {
            return other != null && SourceLength == other.SourceLength && _source.SequenceEqual(other._source);
        }

        internal bool HasSameStartLayout(PersistentMapIdentity other)
        {
            return other != null && _layout.SequenceEqual(other._layout);
        }

        internal bool Same(PersistentMapIdentity other)
        {
            return other != null && SourceLength == other.SourceLength && _source.SequenceEqual(other._source) && _layout.SequenceEqual(other._layout);
        }

        internal byte[] ScopeHash()
        {
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m))
            {
                w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.OriginalMapFingerprintDomain);
                w.Write(SourceLength);
                w.Write(_source);
                w.Write(_layout);
                w.Flush();
                using (var h = SHA256.Create())
                    return h.ComputeHash(m.ToArray());
            }
        }

        internal string Fingerprint
        {
            get
            {
                return BitConverter.ToString(ScopeHash()).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static void Write(BinaryWriter w, PersistentMapIdentity identity)
        {
            w.Write(identity != null);
            if (identity == null)
                return;
            w.Write((byte)1);
            w.Write(identity.SourceLength);
            w.Write(identity._source);
            w.Write(identity._layout);
        }

        internal static PersistentMapIdentity Read(BinaryReader r)
        {
            if (!r.ReadBoolean())
                return null;
            if (r.ReadByte() != 1)
                throw new InvalidDataException("Unknown original map identity version");
            long length = r.ReadInt64();
            return new PersistentMapIdentity(r.ReadBytes(32), r.ReadBytes(32), length);
        }
    }

    internal sealed partial class DiplomacyFeature
    {
        private PersistentMapIdentity _mapIdentity;
        private string _loadedSavePath;
        private string _previousMapFingerprint;
        // Called while the original engine monitor is held, once per map bootstrap.
        // RestartSkirmishMapInfo re-resolves the actual original map after a load.
        // FileHeader.crc is often zero and length can be a truncated preview read;
        // neither is used as content evidence. Hash the full original .map instead.
        private PersistentMapIdentity CaptureMapIdentity()
        {
            RequireNativeDiplomacySection();
            var data = GameData.Instance;
            if (data == null || data.Keep_Locations == null || data.Keep_Locations.GetLength(0) != 8 || data.Keep_Locations.GetLength(1) != 2)
                throw new InvalidDataException("Native map layout unavailable");
            var menu = MainViewModel.Instance == null ? null : MainViewModel.Instance.HUDIngameMenu;
            var restart = menu == null ? null : menu.restartSkirmishMapInfo;
            var header = restart == null ? null : restart.selectedHeader;
            string path = header == null ? null : header.filePath;
            var archiveApi = GameMapArchiveManagerAPI.Instance;
            if (string.IsNullOrEmpty(path) && archiveApi != null && !archiveApi.IsCurrentFileSaveFile())
                path = archiveApi.GetCurrentFilePath();
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".map", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original source map path unavailable");
            byte[] source;
            long length;
            // Refuse concurrent writers while hashing; an edited source cannot silently
            // take over the identity of a running map or of a saved trade archive.
            using (var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                length = input.Length;
                if (length < 35000 || length > 9000000)
                    throw new InvalidDataException("Original source map size invalid");
                using (var h = SHA256.Create())
                    source = h.ComputeHash(input);
                if (input.Length != length || input.Position != length)
                    throw new IOException("Original map changed while reading");
            }

            return new PersistentMapIdentity(source, NativeStartLayoutHash(), length);
        }

        private byte[] NativeStartLayoutHash()
        {
            RequireNativeDiplomacySection();
            var data = GameData.Instance;
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m, Encoding.UTF8, true))
            {
                w.Write(StrongholdEuropeDE.Runtime.CompatibilityContracts.NativeStartLayoutDomain);
                w.Write((int)data.mapType);
                w.Write(GameMap.tilemapSize);
                w.Write(data.SkirmishGameType);
                w.Write(data.coopTrailID);
                for (int i = 0; i < 8; i++)
                {
                    w.Write(data.Keep_Locations[i, 0]);
                    w.Write(data.Keep_Locations[i, 1]);
                }

                w.Flush();
                byte[] captured = m.ToArray();
                byte[] layoutHash;
                using (var h = SHA256.Create())
                    layoutHash = h.ComputeHash(captured);
#if DIPLOMACY_PRIVATE_TESTS
                // Diagnose only the bytes already hashed above. Formatting and
                // logging cannot prevent the real identity hash from returning.
                try
                {
                    // Hash and byte buffer are already finalized. This private
                    // trace is best-effort and uses only the monitor already
                    // required for the original hash; it never adds fields to
                    // the saved layout hash or changes the return value.
                    TraceOriginalLivePhase("native_start_layout_hash_completed");
                    string diagnostic;
                    if (TryFormatCapturedNativeStartLayout(captured, layoutHash, out diagnostic))
                        Log("trade_session", diagnostic);
                }
                catch (Exception)
                {
                }

#endif
                return layoutHash;
            }
        }

#if DIPLOMACY_PRIVATE_TESTS
        private static bool TryFormatCapturedNativeStartLayout(byte[] captured, byte[] layoutHash, out string diagnostic)
        {
            diagnostic = null;
            if (captured == null || captured.Length < 80 || captured.Length > 512 || layoutHash == null || layoutHash.Length != 32)
                return false;
            using (var m = new MemoryStream(captured, false))
            using (var r = new BinaryReader(m, Encoding.UTF8))
            {
                if (r.ReadString() != StrongholdEuropeDE.Runtime.CompatibilityContracts.NativeStartLayoutDomain || m.Length - m.Position != 80)
                    return false;
                var b = new StringBuilder(512);
                b.Append("status=original_start_layout_capture");
                b.Append("|map_type=").Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                b.Append("|tilemap_size=").Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                b.Append("|skirmish_game_type=").Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                b.Append("|coop_trail_id=").Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                b.Append("|keep_locations=");
                for (int i = 0; i < 8; i++)
                {
                    if (i != 0)
                        b.Append(';');
                    b.Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    b.Append(',').Append(r.ReadInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                b.Append("|layout_sha256=").Append(BitConverter.ToString(layoutHash).Replace("-", "").ToLowerInvariant());
                diagnostic = b.ToString();
                return true;
            }
        }

#endif
        private void InitializeMapIdentity(PersistentMapIdentity provenOriginal = null)
        {
            _mapIdentity = null;
            _previousMapFingerprint = Fingerprint(MapMaterial());
            try
            {
                _mapIdentity = provenOriginal == null ? CaptureMapIdentity() : provenOriginal.Copy();
                Log("trade_session", "status=original_map_identity_captured|fingerprint=" + _mapIdentity.Fingerprint);
            }
            catch (Exception e)
            {
                // Legacy diplomacy can still operate. A fingerprint mismatch will never
                // be repaired from names or partial headers when source proof is missing.
                Log("trade_session", "status=original_map_identity_unavailable|reason=" + Safe(e.GetType().Name));
            }
        }

        private bool PayloadCameFromLoadedArchive(byte[] payload)
        {
            if (payload == null || string.IsNullOrEmpty(_loadedSavePath))
                return false;
            MapArchive archive;
            if (!MapArchive.TryLoad(Path.GetFullPath(_loadedSavePath), out archive) || archive == null)
                return false;
            using (archive)
            {
                byte[] primary = archive.TryReadBinaryFile("_SE_ModData_" + GeneratedDiplomacyContract.SaveDataIdentifier + ".msgpack", true);
                return primary != null && primary.SequenceEqual(payload);
            }
        }

        private void RestoreMapIdentity(LegacyDiplomacySave saved, byte[] payload)
        {
            if (saved.MapIdentity == null)
            {
                // Preserve the previous codec's exact-match compatibility. No name-only
                // migration is permitted if its transient old material no longer matches.
                if (saved.MapFingerprint == _previousMapFingerprint && saved.RosterFingerprint == _rosterFingerprint && saved.LocalPlayerId == _localPlayer)
                    _mapFingerprint = saved.MapFingerprint;
#if DIPLOMACY_PRIVATE_TESTS
                TryAdoptVerifiedLegacyBaseline(saved, payload);
#endif
                return;
            }

            bool sameIdentity = saved.MapIdentity.Same(_mapIdentity);
            bool sameRoster = saved.RosterFingerprint == _rosterFingerprint;
            bool sameLocalPlayer = saved.LocalPlayerId == _localPlayer;
            // Keep the existing short-circuit rule: do not open a second
            // archive when an earlier identity prerequisite already failed.
            bool archiveChecked = sameIdentity && sameRoster && sameLocalPlayer;
            bool sameArchive = archiveChecked && PayloadCameFromLoadedArchive(payload);
            if (!sameIdentity || !sameRoster || !sameLocalPlayer || !sameArchive)
            {
                Log("trade_session", "status=original_map_restore_mismatch" + "|source_content_matches=" + saved.MapIdentity.HasSameSourceContent(_mapIdentity) + "|native_start_layout_matches=" + saved.MapIdentity.HasSameStartLayout(_mapIdentity) + "|roster_matches=" + sameRoster + "|local_player_matches=" + sameLocalPlayer + "|archive_payload_checked=" + archiveChecked + "|archive_payload_matches=" + (archiveChecked ? sameArchive.ToString() : "not_checked") + "|current_identity_available=" + (_mapIdentity != null) + "|saved_identity_sha256=" + saved.MapIdentity.Fingerprint + "|current_identity_sha256=" + (_mapIdentity == null ? "unavailable" : _mapIdentity.Fingerprint));
                throw new InvalidDataException("Original map content, native start layout or loaded archive differs");
            }

            // The historical map token also scopes mailbox/recovery/archive records.
            // Keep that token intact instead of rebinding or rewriting saved history.
            _mapFingerprint = saved.MapFingerprint;
        }
    }
}
