// native image readonly release-compatible READ-ONLY observation of a pinned native function
// and its managed/native layout. No function is invoked and no pointer
// is written to. Never an authority for SaveData, goods or multiplayer.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CrusaderDE;
using SHCDESE.API;
using SHCDESE.GameGlobals;
using SHCDESE.Interop;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalNativeImageReadOnly
    {
        private const int CapacityRva = 0xC5AD0;
        private const int BuildingStride = 0x32c, BuildingOffset = 0x5c;
        private const int BuildingCount = 4000;
        private const uint MemCommit = 0x1000, PageGuard = 0x100, PageNoAccess = 1;
        private const string EntryBytes =
            "895424105357415541574883EC384D63E88BFA418BD54C8BF9BBFFFFFFFF";
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInformation
        {
            internal IntPtr BaseAddress, AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr RegionSize;
            internal uint State, Protect, Type;
        }
        private sealed class PeSection
        {
            internal uint Rva, RawSize, RawOffset;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQuery(
            IntPtr address, out MemoryInformation info, UIntPtr length);

        private static bool Query(IntPtr address, out MemoryInformation info)
        {
            return VirtualQuery(address,out info,
                (UIntPtr)Marshal.SizeOf(typeof(MemoryInformation))).ToUInt64() != 0;
        }

        // Guard against unreadable/guard pages and address-region overflow
        // BEFORE accessing ANY loaded original native memory.
        internal static bool Readable(IntPtr start,long length)
        {
            if (IntPtr.Size != 8 || start.ToInt64() <= 0 || length <= 0)
                return false;
            ulong cursor=(ulong)start.ToInt64();
            ulong end=cursor+(ulong)length;
            if (end <= cursor || end > long.MaxValue) return false;
            while(cursor<end)
            {
                MemoryInformation info;
                if(!Query(new IntPtr((long)cursor),out info) ||
                   info.State!=MemCommit ||
                   (info.Protect&(PageGuard|PageNoAccess))!=0)
                    return false;
                uint code=info.Protect & 0xff;
                if(code!=2 && code!=4 && code!=8 &&
                   code!=0x20 && code!=0x40 && code!=0x80)
                    return false;
                ulong baseAddress=(ulong)info.BaseAddress.ToInt64();
                ulong upper=baseAddress+info.RegionSize.ToUInt64();
                if(baseAddress>cursor || upper<=cursor) return false;
                cursor=Math.Min(upper,end);
            }
            return true;
        }

        internal static bool MatchesMappedBytes(IntPtr image, int rva, int length, string expectedSha256)
        {
            IntPtr entry = IntPtr.Add(image, rva);
            if (!OriginalNativeImageReadOnly.Readable(entry, length))
                return false;
            var bytes = new byte[length];
            Marshal.Copy(entry, bytes, 0, bytes.Length);
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                return String.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static long FileOffset(PeSection[] sections,
            uint rva,uint size,long fileLength)
        {
            foreach(var section in sections)
            {
                if(rva<section.Rva)continue;
                ulong delta=(ulong)rva-section.Rva;
                if(delta+size>section.RawSize)continue;
                ulong at=section.RawOffset+delta;
                if(at+size<=(ulong)fileLength)
                    return checked((long)at);
            }
            throw new InvalidDataException("native PE RVA outside raw section");
        }

        // Derive the COMPLETE function extent from pinned original PE
        // exception/unwind metadata. The constant first bytes alone
        // must not count as a correct mapped original implementation.
        private static byte[] ExactOriginalFunction(string path)
        {
            using(var stream=File.OpenRead(path))
            using(var reader=new BinaryReader(stream))
            {
                if(stream.Length<1000000L || stream.Length>16000000L)
                    throw new InvalidDataException("unexpected native file length");
                stream.Position=0x3c;
                int pe=reader.ReadInt32();
                if(pe<0x40 || pe>stream.Length-0x100)
                    throw new InvalidDataException("PE offset invalid");
                stream.Position=pe;
                if(reader.ReadUInt32()!=0x4550 || reader.ReadUInt16()!=0x8664)
                    throw new InvalidDataException("native x64 PE required");
                ushort count=reader.ReadUInt16();
                if(count==0 || count>96)
                    throw new InvalidDataException("PE section count");
                stream.Position=pe+20;
                ushort optionalSize=reader.ReadUInt16();
                long optional=pe+24;
                stream.Position=optional;
                if(reader.ReadUInt16()!=0x20b || optionalSize<144)
                    throw new InvalidDataException("PE32+ optional header");
                stream.Position=optional+112+3*8;
                uint exceptionRva=reader.ReadUInt32();
                uint exceptionSize=reader.ReadUInt32();
                if(exceptionSize<12 || exceptionSize%12!=0 ||
                   exceptionSize>2000000)
                    throw new InvalidDataException("PE unwind directory");
                var sections=new PeSection[count];
                for(int i=0;i<count;i++)
                {
                    stream.Position=optional+optionalSize+i*40+12;
                    sections[i]=new PeSection {
                        Rva=reader.ReadUInt32(),
                        RawSize=reader.ReadUInt32(),
                        RawOffset=reader.ReadUInt32()
                    };
                }
                long table=FileOffset(sections,exceptionRva,exceptionSize,stream.Length);
                uint endRva=0;int matches=0;
                for(uint i=0;i<exceptionSize/12;i++)
                {
                    stream.Position=table+i*12;
                    uint begin=reader.ReadUInt32(),end=reader.ReadUInt32();
                    if(begin==(uint)CapacityRva)
                    {
                        matches++;
                        endRva=end;
                    }
                }
                if(matches!=1 || endRva<=CapacityRva ||
                   endRva-CapacityRva>0x4000)
                    throw new InvalidDataException("native function unwind extent");
                uint bytes=endRva-CapacityRva;
                stream.Position=FileOffset(sections,(uint)CapacityRva,bytes,stream.Length);
                byte[] function=reader.ReadBytes((int)bytes);
                if(function.Length!=bytes)
                    throw new EndOfStreamException("native function image truncated");
                for(int i=0;i<EntryBytes.Length/2;i++)
                {
                    byte expected=Convert.ToByte(EntryBytes.Substring(i*2,2),16);
                    if(function[i]!=expected)
                        throw new InvalidDataException("pinned original function entry mismatch");
                }
                return function;
            }
        }

        // PROOF SCOPE: loaded native PE/whole bounded function bytes,
        // original managed ABI and building-manager memory layout.
        // It does NOT prove an atomic Save+two-leg goods commit.
        // It does NOT call the native capacity function or any setter.
        internal static unsafe bool TryAttestReadOnly(out string reason)
        {
            reason="original_native_readonly_abi_not_ready";
            try
            {
                if(!DiplomacyFeature.IsNativeDiplomacySectionHeld)
                {
                    reason="original_engine_monitor_not_owned";
                    return false;
                }
                if(IntPtr.Size!=8 || sizeof(GameBuilding)!=BuildingStride)
                {
                    reason="original_native_struct_stride_or_x64_mismatch";
                    return false;
                }
                string detail;
                if(!SerpsRawraCompat.AttestedLoadedNativeEngine(out detail) ||
                   !VerifiedManagedSurface.VerifyGameAssembly(out detail) ||
                   !VerifiedManagedSurface.VerifyRawraAssembly(out detail))
                {
                    reason="native_or_managed_original_methods_not_pinned";
                    return false;
                }
                if(!OriginalSoloWarehouseCapacityReader.IsSoloHumanHostSkirmish(out detail))
                {
                    reason="original_native_readonly_not_solo";
                    return false;
                }
                using(var process=Process.GetCurrentProcess())
                {
                    ProcessModule native=null;
                    int matches=0;
                    foreach(ProcessModule m in process.Modules)
                        if(String.Equals(m.ModuleName,"CrusaderDE.dll",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            matches++;
                            native=m;
                        }
                    if(matches!=1 || native==null || native.BaseAddress==IntPtr.Zero)
                    {
                        reason="original_native_loaded_module_not_unique";
                        return false;
                    }
                    IntPtr baseAddress=native.BaseAddress;
                    IntPtr functionAddress=IntPtr.Add(baseAddress,CapacityRva);
                    byte[] original=ExactOriginalFunction(native.FileName);
                    MemoryInformation image;
                    if(!Query(functionAddress,out image) ||
                       image.AllocationBase!=baseAddress ||
                       !Readable(functionAddress,original.Length))
                    {
                        reason="mapped_native_original_function_unreadable";
                        return false;
                    }
                    for(int i=0;i<original.Length;i++)
                        if(Marshal.ReadByte(functionAddress,i)!=original[i])
                        {
                            reason="mapped_native_original_function_changed";
                            return false;
                        }
                }
                // No native delegate invocation. Confirm only same pinned
                // managed pointer and contiguous 3,999 original building cells.
                var globals=GameGlobalsManager.Instance;
                var buildings=GameBuildingManagerAPI.Instance;
                if(globals==null || buildings==null)
                {
                    reason="building_manager_api_unavailable";
                    return false;
                }
                IntPtr manager=(IntPtr)globals.GameBuildingManagerVA;
                if(manager==IntPtr.Zero ||
                   !Readable(manager,checked(BuildingOffset+(long)BuildingCount*BuildingStride)))
                {
                    reason="building_manager_original_memory_not_readable";
                    return false;
                }
                int allocated=Marshal.ReadInt32(manager,0x50);
                if(allocated<1 || allocated>BuildingCount)
                {
                    reason="original_building_count_unverified";
                    return false;
                }
                var span=buildings.GetBuildingsAsSpan();
                if(span.Length!=BuildingCount-1)
                {
                    reason="original_managed_building_span_length_changed";
                    return false;
                }
                fixed(GameBuilding* first=&span.GetPinnableReference())
                {
                    if((IntPtr)first!=IntPtr.Add(manager,
                        BuildingOffset+BuildingStride))
                    {
                        reason="original_building_span_pointer_changed";
                        return false;
                    }
                    GameBuilding* last=first+span.Length-1;
                    if((IntPtr)last!=IntPtr.Add(manager,
                       BuildingOffset+(BuildingCount-1)*BuildingStride) ||
                       !Readable((IntPtr)last,BuildingStride))
                    {
                        reason="original_building_memory_endpoint_changed";
                        return false;
                    }
                }
                reason="original_loaded_native_function_and_building_layout_readonly";
                return true;
            }
            catch(Exception e)
            {
                reason="original_native_readonly_abi_exception_"+e.GetType().Name;
                return false;
            }
        }
    }
}
