// One checked original native day read in the existing managed monitor.
// Zero remains not-ready; no clamping or invented day observation.
using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private int ReadOriginalNativeDay()
        {
            RequireNativeDiplomacySection();
            long actual = ScriptExtenderNativeBridge.CurrentDay();
            if (actual < 0 || actual > int.MaxValue)
                throw new InvalidDataException("Original native day out of supported range");
            return checked((int)actual);
        }
    }
}
