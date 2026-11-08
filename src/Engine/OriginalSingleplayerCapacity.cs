// exact original Steam 24816905 free-warehouse-capacity native reader.
// READ-ONLY native call, ONLY while holding original EngineInterface.threadLock.
// Strictly private disposable singleplayer game copy, never original install.
// All goods movement uses the official rawra TryAddGood/RemoveGood hooks,
// with exact 4-cell precommit and same-monitor synchronous rollback.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SHCDESE.API;
using SHCDESE.Interop;
using SHCDESE.GameGlobals;
using CrusaderDE;
using StrongholdEuropeDE.TradeModels;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class OriginalSoloWarehouseCapacityReader : OriginalInventoryEvidence.IOriginalWarehouseCapacityAttestation
    {
        internal const int NativeFreeCapacityRva = 0xC5AD0;
        private const string NativeEntryBytes = "895424105357415541574883EC384D63E88BFA418BD54C8BF9BBFFFFFFFF";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeOriginalFreeCapacity(IntPtr manager, int owner, int good);
        private static readonly object BindingGate = new object ();
        private static NativeOriginalFreeCapacity _nativeFree;
        private static IntPtr _provenImageBase;
        private static string _reason;
        // A solo Crusader DE skirmish is normally networked=true because
        // the game uses the MP host/simulation plumbing for local AI lords.
        // Distinguish actual other human participants, not the network bit.
        internal static bool IsSoloHumanHostSkirmish()
        {
            string reason;
            return IsSoloHumanHostSkirmish(out reason);
        }

        // preserve the gate while measuring its actual denial.
        // No names or Steam identities are written to diagnostics.
        internal static bool IsSoloHumanHostSkirmish(out string reason)
        {
            return DiplomacyFeature.IsSoloEngineSession(out reason);
        }

        internal static bool ProvenanceReady(out string diagnostic)
        {
            diagnostic = " exact singleplayer original-engine binding not established";
            try
            {
                // Do not allow the image-bound adapter to run native writes
                // in either the Steam install or other concurrently used mods.
                using (var process = Process.GetCurrentProcess())
                {
                    var main = process.MainModule;
                    if (main == null || !string.Equals(Path.GetFileName(main.FileName), "Stronghold Crusader Definitive Edition.exe", StringComparison.OrdinalIgnoreCase))
                        return false;
                    string folder = Path.GetFullPath(Path.GetDirectoryName(main.FileName)).TrimEnd('\\');
                    bool exactPrivate = false;
#if DIPLOMACY_PRIVATE_TESTS
                    string privateReason;
                    exactPrivate = string.Equals(folder, DisposableGameScope.GameRoot, StringComparison.OrdinalIgnoreCase) && DisposableGameScope.IsEnabled(out privateReason);
#endif
                    if (!exactPrivate)
                        return false;
                }

                if (!IsSoloHumanHostSkirmish())
                    return false;
                // Hash the true loaded game's pinned original PE exactly
                // once, not 152 times per world snapshot. Subsequent reads
                // still demand the correct running process and SP scope.
                if (_nativeFree != null && _provenImageBase != IntPtr.Zero)
                {
                    diagnostic = " native original capacity already attested";
                    return true;
                }

                string loaded;
                if (!SerpsRawraCompat.AttestedLoadedNativeEngine(out loaded))
                    return false;
                lock (BindingGate)
                {
                    if (_nativeFree != null)
                    {
                        diagnostic = " pinned original native free storage bound";
                        return true;
                    }

                    // The native function's initial bytes are verified against
                    // untouched Steam PE, AND against its *loaded* executable
                    // mapping. Failure to match is a hard refusal.
                    using (var process = Process.GetCurrentProcess())
                    {
                        ProcessModule engine = null;
                        int count = 0;
                        foreach (ProcessModule module in process.Modules)
                        {
                            if (!string.Equals(module.ModuleName, "CrusaderDE.dll", StringComparison.OrdinalIgnoreCase))
                                continue;
                            count++;
                            engine = module;
                        }

                        if (count != 1 || engine == null || engine.BaseAddress == IntPtr.Zero)
                            return false;
                        IntPtr entry = IntPtr.Add(engine.BaseAddress, NativeFreeCapacityRva);
                        byte[] exact = FromExactHex(NativeEntryBytes);
                        for (int i = 0; i < exact.Length; i++)
                            if (Marshal.ReadByte(entry, i) != exact[i])
                                return false;
                        // The PE image hash was checked by the official
                        // compatibility reader just above. The function bytes
                        // are exact at the runtime mapping, not merely disk.
                        _nativeFree = (NativeOriginalFreeCapacity)Marshal.GetDelegateForFunctionPointer(entry, typeof(NativeOriginalFreeCapacity));
                        _provenImageBase = engine.BaseAddress;
                    }
                }

                diagnostic = "Steam-pinned original native free capacity bound in private skirmish";
                return true;
            }
            catch (Exception e)
            {
                _reason = e.GetType().Name;
                return false;
            }
        }

        private static byte[] FromExactHex(string text)
        {
            if (text == null || text.Length < 32 || (text.Length & 1) != 0)
                throw new InvalidOperationException("Unattested machine-code anchor");
            var bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return bytes;
        }

        public unsafe bool TryReadWarehouseCapacity(int owner, ushort good, out int exactMax)
        {
            exactMax = 0;
            if (owner < 1 || owner > 8 || GoodCatalog.Find(good) == null || !DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return false;
            string diagnostic;
            if (!ProvenanceReady(out diagnostic) || _nativeFree == null || _provenImageBase == IntPtr.Zero)
                return false;
            try
            {
                // rawra's exact official GetBuildingManager returns a native
                // pointer to the original GameBuildingManager (NOT an array).
                var buildingApi = GameBuildingManagerAPI.Instance;
                if (buildingApi == null)
                    return false;
                // Official rawra constructor uses this exact managed
                // native pointer source. It avoids undeclared RedBird.Core
                // ABI and is checked against the original 3999-building
                // API array, which begins at manager+0x5c+0x32c.
                GameBuildingManager* manager = (GameBuildingManager*)GameGlobalsManager.Instance.GameBuildingManagerVA;
                if (manager == null)
                    return false;
                IntPtr nativeManager = (IntPtr)manager;
                int allocated = Marshal.ReadInt32(nativeManager, 0x50);
                var nativeBuildings = buildingApi.GetBuildingsAsSpan();
                if (allocated < 1 || allocated > 4000 || nativeBuildings.Length != 3999)
                    return false;
                fixed (GameBuilding* first = &nativeBuildings.GetPinnableReference())
                {
                    if ((IntPtr)first != IntPtr.Add(nativeManager, 0x5c + 0x32c))
                        return false;
                }

                int amount = GamePlayerManagerAPI.Instance.GetGoodAmount(owner, (eGoods)good);
                if (amount < 0 || amount > int.MaxValue - 200000)
                    return false;
                int free = _nativeFree(nativeManager, owner, good);
                if (free < -1 || free > 5000000)
                    return false;
                if (free < 0 && amount != 0)
                    return false;
                long maximum = (long)amount + Math.Max(0, free);
                if (maximum > int.MaxValue - 200000)
                    return false;
                exactMax = (int)maximum;
                return true;
            }
            catch (Exception)
            {
                // Reading is never a goods write, never trust a partial value.
                exactMax = 0;
                return false;
            }
        }
    }
}
