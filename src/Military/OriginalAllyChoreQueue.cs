#if DIPLOMACY_PRIVATE_TESTS && HOST_DIRECTED_AI_TRIBE_CANARY
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    // Read-only priority check for the original local/received Chore slots.
    // The managed human-order queue is checked independently by its owner.
    internal sealed class OriginalAllyChoreQueue
    {
        private const int QueueRva = 0x8624F18;
        private const int SlotCount = 500;
        private const int SlotSize = 0x500;
        private const int OpcodeOffset = 8;
        private const int StatusOffset = 9;
        private const int PayloadOffset = 17;
        private const byte AllyOrdersOpcode = 113;

        private readonly IntPtr _slots;

        private OriginalAllyChoreQueue(IntPtr slots)
        {
            _slots = slots;
        }

        internal static bool TryBind(out OriginalAllyChoreQueue queue, out string reason)
        {
            queue = null;
            try
            {
                if (!SerpsRawraCompat.AttestedLoadedNativeEngine(out reason))
                    return false;

                reason = "original_chore_layout_unverified";
                using (Process process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        if (!String.Equals(module.ModuleName, "CrusaderDE.dll",
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                        IntPtr image = module.BaseAddress;
                        if (image == IntPtr.Zero || module.ModuleMemorySize < QueueRva + SlotCount * SlotSize)
                            return false;

                        // The two writers, due scan, consumer and payload decoder
                        // all use this slot layout. These code ranges are relocation-free.
                        if (!OriginalNativeImageReadOnly.MatchesMappedBytes(image, 0x23990, 561, "a231ff2b25c2f58423bd3aefdfd6707c29140e5068531f2fb78f9f835a729cec") ||
                            !OriginalNativeImageReadOnly.MatchesMappedBytes(image, 0x23F10, 417, "3641f36b30366d6cf61fedee17ccec23efa64d4fdd3b0ccff113d1d2d7b3fe03") ||
                            !OriginalNativeImageReadOnly.MatchesMappedBytes(image, 0x21EE0, 319, "9b63f8e5476546cb341fa7a40cc3b045beec536cca9042703280604c7d2c5b1e") ||
                            !OriginalNativeImageReadOnly.MatchesMappedBytes(image, 0x1F7E0, 228, "65bbb81b6503836fe2216bbc762f571e234923985fa2adc7f9e04ca6a8fee2e3") ||
                            !OriginalNativeImageReadOnly.MatchesMappedBytes(image, 0x1A6F0, 608, "4fab7fd4ceb48eeb330b4a67c3cf70020eff7d6a89caacdb47de135b5c981600"))
                            return false;

                        queue = new OriginalAllyChoreQueue(IntPtr.Add(image, QueueRva));
                        reason = null;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception error)
            {
                reason = "original_chore_layout_unavailable_" + error.GetType().Name;
                return false;
            }
        }

        internal bool TryReadPendingMilitaryOrder(out bool pending)
        {
            pending = false;
            if (!DiplomacyFeature.IsNativeDiplomacySectionHeld ||
                !OriginalNativeImageReadOnly.Readable(_slots, SlotCount * SlotSize))
                return false;

            for (int i = 0; i < SlotCount; i++)
            {
                IntPtr slot = IntPtr.Add(_slots, i * SlotSize);
                byte status = Marshal.ReadByte(slot, StatusOffset);
                if (status == 0 || status == 10)
                    continue;
                if (status > 10)
                    return false;
                if (Marshal.ReadByte(slot, OpcodeOffset) != AllyOrdersOpcode)
                    continue;

                short kind = Marshal.ReadInt16(slot, PayloadOffset);
                if (kind < 0 || kind > 5)
                    return false;
                if (kind == 0 || kind == 4 || kind == 5)
                {
                    pending = true;
                    return true;
                }
            }

            return true;
        }

    }
}
#endif
