// – strict original-engine Native goods / game-save / host authority.
// Only the official current Steam PE and rawra 2.14.3 are research references.
// DO NOT enable real goods until all three *actual* runtime authorities exist.
// The rawra public viaChore API silently falls back to Steam packets.
// The internal Chore queue method only acknowledges enqueue, not delivery.
// Inbound Chore 106 HandleRawPacket lacks a trusted Steam sender identity.
using System;
using System.Reflection;
using SHCDESE.API;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        // A loaded unverified PREPARED barter must not be auto-committed or
        // auto-refunded by its untrusted journal. This flag remains until a
        // new map session, preserving the native state for manual review.
        private bool _unattestedSavedNativeTrade;
    }

    internal enum StrictChoreResult
    {
        Refused = 0,
        NotHost = 1,
        Unavailable = 2,
        QueuedNotCommitted = 3
    }

    internal sealed class HostCommitWitness
    {
        internal string MapFingerprint { get; set; }
        internal string RosterFingerprint { get; set; }
        internal string SavedReceiptDigest { get; set; }
        internal ulong QuoteId { get; set; }
        internal ulong HostTerm { get; set; }
        internal ulong LastAcceptedQuoteId { get; set; }
        internal int HostSlot { get; set; }
        internal int OriginSlot { get; set; }
        internal bool NetworkGame { get; set; }
        internal bool LocalHost { get; set; }
        internal bool HostSteamIdentityAuthenticated { get; set; }
        internal bool SenderIdentityAuthenticated { get; set; }
        internal bool ChoreOnlyDeliveryAcknowledged { get; set; }
        internal bool EngineSaveReceiptDurable { get; set; }
        internal bool FourCellsRechecked { get; set; }
    }

    internal static class StrictNativeTradeAuthorities
    {
        // Native Engine SaveData exposes no durable original-engine commit
        // acknowledgement for BOTH native goods legs plus the mod journal.
        // SaveContext.IsSaveFile alone is NOT that acknowledgement.
        internal const bool NativeGoodsAndSaveCommitAuthority = false;
        // The real rawra Chore 106 receiver calls HandleRawPacket without a
        // sender; neither queue-success nor a claimed origin field proves
        // that the packet was sent by the actual multiplayer host.
        internal const bool AuthenticatedStrictChoreOriginAuthority = false;
        // A loaded PREPARED record is evidence of intent, NOT of past goods
        // mutations. Never award success or autonomously refund stocks based
        // only on a replayable SHA256-checked ModSaveData journal.
        internal static bool CanResumeSavedPreparedTrade(bool originalSaveAcknowledged, bool originalFourCellsAttested, bool exactHostEpochProven)
        {
            return NativeGoodsAndSaveCommitAuthority && originalSaveAcknowledged && originalFourCellsAttested && exactHostEpochProven;
        }

        // This pure comparison verifies a hypothetical host-issued receipt.
        // It deliberately cannot manufacture any of the input attestations.
        // A test-only synthetic witness may be used to test state logic.
        internal static bool IsConsistentHostWitness(HostCommitWitness w, string expectedMap, string expectedRoster, ulong expectedQuote, ulong expectedTerm, int trustedHostSlot)
        {
            if (w == null || !w.NetworkGame || !w.LocalHost || !w.HostSteamIdentityAuthenticated || !w.SenderIdentityAuthenticated || !w.ChoreOnlyDeliveryAcknowledged || !w.EngineSaveReceiptDurable || !w.FourCellsRechecked || w.QuoteId < 1001 || w.QuoteId <= w.LastAcceptedQuoteId || w.HostTerm == 0 || w.HostSlot < 1 || w.HostSlot > 8 || w.OriginSlot != w.HostSlot || w.HostSlot != trustedHostSlot || w.QuoteId != expectedQuote || w.HostTerm != expectedTerm || !string.Equals(w.MapFingerprint, expectedMap, StringComparison.Ordinal) || !string.Equals(w.RosterFingerprint, expectedRoster, StringComparison.Ordinal) || !IsExactSha256Hex(w.SavedReceiptDigest))
                return false;
            return true;
        }

        private static bool IsExactSha256Hex(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            return true;
        }

        // Only strict direct Chore 106 may be used for future host votes.
        // Do NOT use SendPacketToAllEx2(viaChore:true): it falls back to Steam.
        // Even strict queue success is NEVER a durable host acknowledgment.
        private static bool CanAttemptHostSend()
        {
            return SerpsRawraCompat.CanClaimRealHostQuorum && AuthenticatedStrictChoreOriginAuthority && NativeGoodsAndSaveCommitAuthority;
        }

        internal static StrictChoreResult TryQueueStrictHostVote(short packetId, byte[] canonicalPayload)
        {
            if (!CanAttemptHostSend())
                return StrictChoreResult.Refused;
            if (packetId < 1100 || canonicalPayload == null || canonicalPayload.Length < 1 || canonicalPayload.Length > 1198)
                return StrictChoreResult.Refused;
            if (!GameNetworkAPI.IsNetworkedEnvironment() || !GameNetworkAPI.IsMultiplayerGame() || !GameNetworkAPI.IsLocalHost() || !GameNetworkAPI.IsPlayerSlotMappingAvailable())
                return StrictChoreResult.NotHost;
            try
            {
                var m = typeof(GameNetworkAPI).GetMethod("SendScriptExtenderChorePayload", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(byte[]) }, null);
                if (m == null || m.ReturnType != typeof(bool))
                    return StrictChoreResult.Unavailable;
                // 2-byte wire ID + the exact canonical guarded payload.
                byte[] wire = new byte[canonicalPayload.Length + 2];
                wire[0] = (byte)(packetId & 255);
                wire[1] = (byte)((packetId >> 8) & 255);
                Buffer.BlockCopy(canonicalPayload, 0, wire, 2, canonicalPayload.Length);
                bool enqueued = (bool)m.Invoke(null, new object[] { wire });
                return enqueued ? StrictChoreResult.QueuedNotCommitted : StrictChoreResult.Unavailable;
            }
            catch (Exception)
            {
                // A native enqueue may have happened before a fault; retry is
                // prohibited until an authenticated one-shot receipt exists.
                return StrictChoreResult.Unavailable;
            }
        }
    }
}
