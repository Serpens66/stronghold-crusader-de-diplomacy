// THIN reference wrapper over WorldAI's one and only original frame history.
// No digest implementation and no frame cache/dictionary in controller lane.
using System;
using System.IO;
using StrongholdEuropeDE.Runtime.Networking;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class ObservedOriginalConsentFrame
    {
        private readonly CapturedConsentWorldFrame witness;
        internal ObservedOriginalConsentFrame(CapturedConsentWorldFrame original)
        {
            if (original == null || original.OriginalObservation == null || original.ActualOriginalMapTick < 1)
                throw new InvalidDataException("No actual WorldAI retained original frame");
            witness = original;
        }

        internal OriginalMultiplayerConsentFrame Original
        {
            get
            {
                return witness.OriginalObservation;
            }
        }

        internal long Tick
        {
            get
            {
                return witness.ActualOriginalMapTick;
            }
        }

        internal long NativeGeneration
        {
            get
            {
                return witness.OriginalObservation.LocalOriginalMapGeneration;
            }
        }

        internal int Day
        {
            get
            {
                return witness.ActualNativeDay;
            }
        }

        internal ulong SavedCanonicalInputEpoch
        {
            get
            {
                return witness.SavedCanonicalInputEpoch;
            }
        }

        internal Bytes SessionId
        {
            get
            {
                return new Bytes(witness.OriginalObservation.OriginalPinnedSignedSessionId.Copy());
            }
        }

        internal ulong HostSteam
        {
            get
            {
                return witness.OriginalObservation.OriginalHostSteamId;
            }
        }

        internal byte[] ConsentDigest()
        {
            return witness.ConsentOnlyDigest();
        }

        internal bool SameWorldHash(byte[] signed)
        {
            return ConsentWorldDigest.IsSameConsentOnlyDigest(witness.ConsentOnlyDigest(), signed);
        }
    }
}
