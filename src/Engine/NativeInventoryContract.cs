//  private integration eligibility. This is not production authority or a
// claim that a completed native trade/reload test has already passed.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.GameGlobals;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeModels;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // A verified baseline save permits the FIRST observed transaction.
        // A previous successful transaction is deliberately not a prerequisite.
        // The caller still owns the existing  four-cell/journal/rollback
        // protocol. This method neither writes goods nor begins a mutation.
        private bool TryAuthorizeNativeInventory(out string reason)
        {
#if DIPLOMACY_PRIVATE_TESTS
            try
            {
                reason = "private_scope_unavailable";
                if (!DisposableGameScope.IsEnabled(out reason))
                    return false;
                if (!_mapActive || _mapDisabled || _mapBootstrapPending)
                {
                    reason = "map_not_ready";
                    return false;
                }

                if (_inventoryRestorePending || _goodsTransfersUnavailable || _unattestedSavedNativeTrade || _tradeFaulted)
                {
                    reason = "inventory_recovery_or_quarantine";
                    return false;
                }

                if (_nativeTransferProbe != null || _pendingPackage != null || _pendingTeamTransition != null)
                {
                    reason = "another_native_operation_pending";
                    return false;
                }

                if (_roster == null || _localPlayer < 1 || _localPlayer > 8 || !_roster.Contains(_localPlayer))
                {
                    reason = "live_roster_unavailable";
                    return false;
                }

                if (!NativeInventoryContract.IsStrictSoloHost(out reason))
                    return false;
                if (!_saveBridgeInstalled || !_saveHandlerRegistered)
                {
                    reason = "save_bridge_not_installed";
                    return false;
                }

                if (_coherentSaveGeneration != _sessionGeneration)
                {
                    reason = "current_generation_baseline_save_required";
                    return false;
                }

                if (_saveSuppressed || _saveFailurePending != null)
                {
                    reason = "save_suppressed_or_failed";
                    return false;
                }

                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    reason = "engine_monitor_busy";
                    return false;
                }

                using (lease)
                {
                    if (!NativeInventoryContract.TryValidateUnderLease(out reason))
                        return false;
                    // Checks the three still-owned Harmony save guards, actual
                    // current-generation save receipt and nonquarantined boundary.
                    if (!ObservedPrivateNativeAuthority())
                    {
                        reason = "current_save_guard_or_atomic_boundary_not_ready";
                        return false;
                    }

                    reason = "private_native_inventory_eligible";
                    return true;
                }
            }
            catch (Exception e)
            {
                reason = "native_eligibility_exception:" + e.GetType().Name;
                return false;
            }
#else
            reason = "normal_release_native_authority_closed";
            return false;
#endif
        }

#if DIPLOMACY_PRIVATE_TESTS
        // Optional diagnostic for the actual production quote, never a grant.
        // Requires the existing caller-owned engine lease and returns data
        // directly from native cells, not a ManagedGoodsSnapshotPort or supplied witness.
        private bool TryCaptureFourNativeCells(int requester, int recipient, ushort offeredGood, ushort requestedGood, out int[] amounts, out int[] capacities, out string reason)
        {
            amounts = null;
            capacities = null;
            if (requester < 1 || requester > 8 || recipient < 1 || recipient > 8 || requester == recipient || offeredGood == requestedGood || GoodCatalog.Find(offeredGood) == null || GoodCatalog.Find(requestedGood) == null)
            {
                reason = "invalid_four_cell_identity";
                return false;
            }

            if (!NativeInventoryContract.TryValidateUnderLease(out reason))
                return false;
            if (!(_capacityAuthority is OriginalSoloWarehouseCapacityReader))
            {
                reason = "unexpected_capacity_provider";
                return false;
            }

            int[] owners =
            {
                requester,
                recipient,
                recipient,
                requester
            };
            ushort[] goods =
            {
                offeredGood,
                offeredGood,
                requestedGood,
                requestedGood
            };
            int[] observed = new int[4], bounds = new int[4];
            var api = GamePlayerManagerAPI.Instance;
            if (api == null)
            {
                reason = "player_api_unavailable";
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                observed[i] = api.GetGoodAmount(owners[i], (eGoods)goods[i]);
                if (observed[i] < 0 || !_capacityAuthority.TryReadWarehouseCapacity(owners[i], goods[i], out bounds[i]) || bounds[i] < observed[i] || bounds[i] > int.MaxValue - 200000)
                {
                    reason = "native_cell_or_capacity_invalid:" + i;
                    return false;
                }
            }

            for (int i = 0; i < 4; i++)
                if (api.GetGoodAmount(owners[i], (eGoods)goods[i]) != observed[i])
                {
                    reason = "native_cells_changed_under_owned_monitor:" + i;
                    return false;
                }

            amounts = observed;
            capacities = bounds;
            reason = "four_native_cells_observed";
            return true;
        }
#endif
    }

#if DIPLOMACY_PRIVATE_TESTS
    internal static class NativeInventoryContract
    {
        private const int BuildingStride = 0x32c, BuildingOffset = 0x5c, BuildingCount = 4000;
        private const uint MemCommit = 0x1000, PageGuard = 0x100, PageNoAccess = 1;
        private static readonly object BindingGate = new object ();
        private static Assembly _gameAssembly, _rawraAssembly;
        private static FieldInfo _engineMonitorField;
        private static IntPtr _nativeBase;
        private static byte[] _capacityOriginal;
        private static string _immutableFailure;
        private static bool _bound;
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInformation
        {
            internal IntPtr BaseAddress, AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr RegionSize;
            internal uint State, Protect, Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInformation info, UIntPtr length);
        internal static bool IsStrictSoloHost(out string reason)
        {
            return OriginalSoloWarehouseCapacityReader.IsSoloHumanHostSkirmish(out reason);
        }

        internal static unsafe bool TryValidateUnderLease(out string reason)
        {
            reason = "engine_monitor_not_owned";
            if (!DiplomacyFeature.IsNativeDiplomacySectionHeld)
                return false;
            try
            {
                if (!TryBind(out reason))
                    return false;
                object actualMonitor = _engineMonitorField.GetValue(null);
                if (actualMonitor == null || !Monitor.IsEntered(actualMonitor))
                {
                    reason = "original_engine_threadLock_not_owned";
                    return false;
                }

                if (!ReferenceEquals(_gameAssembly, typeof(MainViewModel).Assembly) || !ReferenceEquals(_rawraAssembly, typeof(GamePlayerManagerAPI).Assembly))
                {
                    reason = "verified_managed_assembly_identity_changed";
                    return false;
                }

                IntPtr entry = IntPtr.Add(_nativeBase, OriginalSoloWarehouseCapacityReader.NativeFreeCapacityRva);
                MemoryInformation nativeRegion;
                if (!Query(entry, out nativeRegion) || nativeRegion.AllocationBase != _nativeBase || !Readable(entry, _capacityOriginal.Length))
                {
                    reason = "loaded_native_image_mapping_changed";
                    return false;
                }

                // A small byte comparison is cheap; never rehash disk or
                // recompute 41 managed method fingerprints per frame/cell.
                for (int i = 0; i < _capacityOriginal.Length; i++)
                    if (Marshal.ReadByte(entry, i) != _capacityOriginal[i])
                    {
                        reason = "loaded_capacity_function_changed:" + i;
                        return false;
                    }

                var globals = GameGlobalsManager.Instance;
                var buildings = GameBuildingManagerAPI.Instance;
                if (globals == null || buildings == null)
                {
                    reason = "native_managers_unavailable";
                    return false;
                }

                IntPtr manager = (IntPtr)globals.GameBuildingManagerVA;
                if (manager == IntPtr.Zero || !Readable(manager, checked(BuildingOffset + BuildingCount * BuildingStride)))
                {
                    reason = "building_manager_range_unreadable";
                    return false;
                }

                int allocated = Marshal.ReadInt32(manager, 0x50);
                if (allocated < 1 || allocated > BuildingCount)
                {
                    reason = "building_count_out_of_bounds:" + allocated;
                    return false;
                }

                var span = buildings.GetBuildingsAsSpan();
                if (span.Length != BuildingCount - 1)
                {
                    reason = "building_span_length_mismatch:" + span.Length;
                    return false;
                }

                fixed (GameBuilding* first = &span.GetPinnableReference())
                {
                    if ((IntPtr)first != IntPtr.Add(manager, BuildingOffset + BuildingStride))
                    {
                        reason = "building_span_pointer_mismatch";
                        return false;
                    }

                    // Independently check both endpoint addresses and stride
                    // against the original PE loop, not just a Span length.
                    GameBuilding* last = first + span.Length - 1;
                    if ((IntPtr)last != IntPtr.Add(manager, BuildingOffset + (BuildingCount - 1) * BuildingStride) || !Readable((IntPtr)last, BuildingStride))
                    {
                        reason = "building_cell_end_boundary_mismatch";
                        return false;
                    }
                }

                reason = "owned_monitor_verified_runtime_and_native_bounds";
                return true;
            }
            catch (Exception e)
            {
                reason = "native_contract_exception:" + e.GetType().Name;
                return false;
            }
        }

        private static unsafe bool TryBind(out string reason)
        {
            if (_bound)
            {
                reason = "immutable_runtime_contract_cached";
                return true;
            }

            if (_immutableFailure != null)
            {
                reason = _immutableFailure;
                return false;
            }

            lock (BindingGate)
            {
                if (_bound)
                {
                    reason = "immutable_runtime_contract_cached";
                    return true;
                }

                if (_immutableFailure != null)
                {
                    reason = _immutableFailure;
                    return false;
                }

                reason = "runtime_contract_binding";
                try
                {
                    if (IntPtr.Size != 8 || sizeof(GameBuilding) != BuildingStride)
                    {
                        reason = "original_building_stride_or_architecture_mismatch";
                        _immutableFailure = reason;
                        return false;
                    }

                    if (!VerifiedManagedSurface.VerifyGameAssembly(out reason) || !VerifiedManagedSurface.VerifyRawraAssembly(out reason))
                    {
                        if (reason.StartsWith("runtime_semantic_IL_differs:", StringComparison.Ordinal) || reason.StartsWith("missing_type:", StringComparison.Ordinal) || reason.StartsWith("ambiguous_runtime_method:", StringComparison.Ordinal))
                            _immutableFailure = reason;
                        return false;
                    }

                    var game = typeof(MainViewModel).Assembly;
                    var engine = game.GetType("EngineInterface", false);
                    var monitor = engine == null ? null : engine.GetField("threadLock", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (monitor == null || monitor.FieldType != typeof(object))
                    {
                        reason = "original_engine_monitor_field_unavailable";
                        _immutableFailure = reason;
                        return false;
                    }

                    if (!OriginalSoloWarehouseCapacityReader.ProvenanceReady(out reason))
                        return false;
                    using (var process = Process.GetCurrentProcess())
                    {
                        ProcessModule native = null;
                        int count = 0;
                        foreach (ProcessModule m in process.Modules)
                            if (string.Equals(m.ModuleName, "CrusaderDE.dll", StringComparison.OrdinalIgnoreCase))
                            {
                                native = m;
                                count++;
                            }

                        if (count != 1 || native == null || native.BaseAddress == IntPtr.Zero)
                        {
                            reason = "native_module_identity_unavailable";
                            return false;
                        }

                        string expected = Path.GetFullPath(Path.Combine(DisposableGameScope.GameRoot, @"Stronghold Crusader Definitive Edition_Data\Plugins\x86_64\CrusaderDE.dll"));
                        if (!string.Equals(expected, Path.GetFullPath(native.FileName), StringComparison.OrdinalIgnoreCase))
                        {
                            reason = "native_module_outside_private_game";
                            return false;
                        }

                        // PE unwind metadata supplies original function bounds.
                        //  has already pinned this exact loaded file SHA.
                        byte[] exact = ReadOriginalFunction(expected, OriginalSoloWarehouseCapacityReader.NativeFreeCapacityRva);
                        IntPtr entry = IntPtr.Add(native.BaseAddress, OriginalSoloWarehouseCapacityReader.NativeFreeCapacityRva);
                        if (!Readable(entry, exact.Length))
                        {
                            reason = "capacity_code_range_unreadable";
                            return false;
                        }

                        for (int i = 0; i < exact.Length; i++)
                            if (Marshal.ReadByte(entry, i) != exact[i])
                            {
                                reason = "capacity_code_differs_from_original_PE:" + i;
                                _immutableFailure = reason;
                                return false;
                            }

                        _gameAssembly = game;
                        _rawraAssembly = typeof(GamePlayerManagerAPI).Assembly;
                        _engineMonitorField = monitor;
                        _nativeBase = native.BaseAddress;
                        _capacityOriginal = exact;
                        _bound = true;
                    }

                    reason = "original_runtime_contract_bound";
                    return true;
                }
                catch (Exception e)
                {
                    reason = "runtime_contract_binding_failed:" + e.GetType().Name;
                    return false;
                }
            }
        }

        private static bool Query(IntPtr address, out MemoryInformation information)
        {
            return VirtualQuery(address, out information, (UIntPtr)Marshal.SizeOf(typeof(MemoryInformation))).ToUInt64() != 0;
        }

        private static bool Readable(IntPtr start, int length)
        {
            if (start.ToInt64() <= 0 || length <= 0)
                return false;
            ulong cursor = (ulong)start.ToInt64(), end = cursor + (ulong)length;
            if (end <= cursor || end > long.MaxValue)
                return false;
            while (cursor < end)
            {
                MemoryInformation information;
                if (!Query(new IntPtr((long)cursor), out information) || information.State != MemCommit || (information.Protect & (PageGuard | PageNoAccess)) != 0)
                    return false;
                uint basic = information.Protect & 0xff;
                if (basic != 2 && basic != 4 && basic != 8 && basic != 0x20 && basic != 0x40 && basic != 0x80)
                    return false;
                ulong regionStart = (ulong)information.BaseAddress.ToInt64();
                ulong regionEnd = regionStart + information.RegionSize.ToUInt64();
                if (regionStart > cursor || regionEnd <= cursor)
                    return false;
                cursor = Math.Min(regionEnd, end);
            }

            return true;
        }

        private sealed class Section
        {
            internal uint Rva, RawSize, RawOffset;
        }

        private static long FileOffset(Section[] sections, uint rva, uint count, long fileLength)
        {
            foreach (var s in sections)
            {
                if (rva < s.Rva)
                    continue;
                ulong delta = (ulong)rva - s.Rva;
                if (delta + count > s.RawSize)
                    continue;
                ulong offset = s.RawOffset + delta;
                if (offset + count > (ulong)fileLength)
                    break;
                return checked((long)offset);
            }

            throw new InvalidDataException("Native RVA outside original raw section");
        }

        private static byte[] ReadOriginalFunction(string path, int entryRva)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 0x3c;
                int pe = reader.ReadInt32();
                if (pe < 0x40 || pe > stream.Length - 0x100)
                    throw new InvalidDataException("PE offset");
                stream.Position = pe;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
                    throw new InvalidDataException("PE architecture");
                ushort count = reader.ReadUInt16();
                if (count == 0 || count > 96)
                    throw new InvalidDataException("PE sections");
                stream.Position = pe + 20;
                ushort optionalSize = reader.ReadUInt16();
                long optional = pe + 24;
                stream.Position = optional;
                if (reader.ReadUInt16() != 0x20b || optionalSize < 144)
                    throw new InvalidDataException("PE optional header");
                stream.Position = optional + 112 + 3 * 8;
                uint exceptionRva = reader.ReadUInt32(), exceptionSize = reader.ReadUInt32();
                if (exceptionSize < 12 || exceptionSize % 12 != 0 || exceptionSize > 2000000)
                    throw new InvalidDataException("PE unwind directory");
                var sections = new Section[count];
                for (int i = 0; i < count; i++)
                {
                    stream.Position = optional + optionalSize + i * 40 + 12;
                    sections[i] = new Section
                    {
                        Rva = reader.ReadUInt32(),
                        RawSize = reader.ReadUInt32(),
                        RawOffset = reader.ReadUInt32()
                    };
                }

                long table = FileOffset(sections, exceptionRva, exceptionSize, stream.Length);
                int matches = 0;
                uint functionEnd = 0;
                for (uint i = 0; i < exceptionSize / 12; i++)
                {
                    stream.Position = table + i * 12;
                    uint begin = reader.ReadUInt32(), end = reader.ReadUInt32();
                    if (begin == (uint)entryRva)
                    {
                        matches++;
                        functionEnd = end;
                    }
                }

                if (matches != 1 || functionEnd <= (uint)entryRva || functionEnd - (uint)entryRva > 0x4000)
                    throw new InvalidDataException("Unique bounded capacity function absent");
                uint length = functionEnd - (uint)entryRva;
                stream.Position = FileOffset(sections, (uint)entryRva, length, stream.Length);
                byte[] bytes = reader.ReadBytes((int)length);
                if (bytes.Length != (int)length)
                    throw new EndOfStreamException();
                return bytes;
            }
        }
    }
#endif
}
