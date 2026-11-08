// trade preview: four-line, bounded, read-only original market preview for the
// real Noesis 185x57 UI field. No policy/consent/SaveData/native writer.
using System;
using System.Globalization;
namespace StrongholdEuropeDE.Runtime
{
    internal static class TradePreviewLayout
    {
        private static string Clip(string value)
        {
            if (String.IsNullOrEmpty(value)) return "?";
            string v=value.Replace("\r"," ").Replace("\n"," ").Trim();
            return v.Length<=12?v:v.Substring(0,9)+"...";
        }

        internal static string FourLines(
            int humanGross, int aiGross, int humanGives, int humanReceives,
            string offeredGood, string wantedGood,
            BarterPreviewState state,bool german)
        {
            // No synthetic source and no false success color/permission.
            // Original quantities are gross, never invented reserves.
            string note;
            switch(state)
            {
                case BarterPreviewState.OriginalHumanRawStockInsufficient:
                    note=german?"Eigenbestand zu klein":"Your gross stock too low";
                    break;
                case BarterPreviewState.OriginalAiRawStockInsufficient:
                    note=german?"KI-Bestand zu klein":"AI gross stock too low";
                    break;
                case BarterPreviewState.OriginalGrossStocksOnly:
                    note=german?"Überschuss/Platz/Bedarf offen":"Surplus/space/need unproved";
                    break;
                default:
                    note=german?"Kein Handel freigegeben":"Trading not authorized";
                    break;
            }
            // The UI control has four bounded lines, no free-form
            // original/private usernames, native pointer strings, or tags.
            return (german?"Dein Rohbestand: ":"Your gross: ")+
                   Math.Max(0,humanGross).ToString(CultureInfo.InvariantCulture)+
                   " "+Clip(offeredGood)+"\n"+
                   (german?"KI-Rohbestand: ":"AI gross: ")+
                   Math.Max(0,aiGross).ToString(CultureInfo.InvariantCulture)+
                   " "+Clip(wantedGood)+"\n"+
                   (german?"Tauschmengen: ":"Trade units: ")+
                   Math.Max(0,humanGives).ToString(CultureInfo.InvariantCulture)+
                   "/"+Math.Max(0,humanReceives).ToString(CultureInfo.InvariantCulture)+"\n"+
                   note;
        }

        internal static bool AuthorizeNativeGoodsWrite() {return false;}
        internal static bool AuthorizeMultiplayer() {return false;}
    }
}
