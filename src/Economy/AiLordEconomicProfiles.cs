// PRIVATE SAFE STAGE: deterministic profile data only. No Unity, network, native or SaveData I/O.
// Original lord IDs are from SHCDESE.Interop.AILords. Values below are mod design presets,
// NOT claims about internal vanilla AI probabilities.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StrongholdEuropeDE.TradeNegotiation
{
    public sealed class AiLordEconomicProfile
    {
        public int LordId { get; private set; }
        public string Name { get; private set; }
        public int Initiative { get; private set; }
        public int MarketPreference { get; private set; }
        public int Caution { get; private set; }
        public int Greed { get; private set; }
        public int Generosity { get; private set; }
        public int Loyalty { get; private set; }
        public int Honor { get; private set; }
        public int WarFocus { get; private set; }
        public int Patience { get; private set; }
        public int Opportunism { get; private set; }
        public int MinimumRequestIntervalDays { get; private set; }
        public int MaximumTradeVolume { get; private set; }
        public string OverlayId { get; private set; }
        internal AiLordEconomicProfile(int id, string name, int initiative, int market, int caution,
            int greed, int generosity, int loyalty, int honor, int war, int patience,
            int opportunism, int interval, int volume, string overlay = "built-in")
        {
            LordId = id; Name = name; Initiative = initiative; MarketPreference = market;
            Caution = caution; Greed = greed; Generosity = generosity; Loyalty = loyalty;
            Honor = honor; WarFocus = war; Patience = patience; Opportunism = opportunism;
            MinimumRequestIntervalDays = interval; MaximumTradeVolume = volume; OverlayId = overlay;
            if (id < 1 || id > 37 || String.IsNullOrWhiteSpace(name) || interval < 1 || interval > 30 ||
                volume < 1 || volume > 100 ||
                new[] {initiative, market, caution, greed, generosity, loyalty, honor, war, patience, opportunism}.Any(x => x < 0 || x > 100))
                throw new ArgumentOutOfRangeException("profile");
        }

        // Fixed byte/culture-independent representation for a later MP preflight.
        public string PolicyHash(string verifiedAicFingerprint)
        {
            if (verifiedAicFingerprint == null || verifiedAicFingerprint.Length != 64 ||
                verifiedAicFingerprint.Any(c => !((c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f'))))
                throw new ArgumentException("Verified lowercase SHA-256 fingerprint required", "verifiedAicFingerprint");
            string canonical = String.Join("|", new[] {
                "ai-lord-economic-profile-v1", LordId.ToString(CultureInfo.InvariantCulture),
                verifiedAicFingerprint.ToLowerInvariant(), OverlayId,
                Initiative.ToString(CultureInfo.InvariantCulture),
                MarketPreference.ToString(CultureInfo.InvariantCulture), Caution.ToString(CultureInfo.InvariantCulture),
                Greed.ToString(CultureInfo.InvariantCulture), Generosity.ToString(CultureInfo.InvariantCulture),
                Loyalty.ToString(CultureInfo.InvariantCulture), Honor.ToString(CultureInfo.InvariantCulture),
                WarFocus.ToString(CultureInfo.InvariantCulture), Patience.ToString(CultureInfo.InvariantCulture),
                Opportunism.ToString(CultureInfo.InvariantCulture),
                MinimumRequestIntervalDays.ToString(CultureInfo.InvariantCulture),
                MaximumTradeVolume.ToString(CultureInfo.InvariantCulture) });
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }

        internal AiLordEconomicProfile With(IDictionary<string,int> overrides, string overlay)
        {
            Func<string,int,int> pick = (key, current) => overrides.ContainsKey(key) ? overrides[key] : current;
            return new AiLordEconomicProfile(LordId, Name,
                pick("initiative", Initiative), pick("marketPreference", MarketPreference),
                pick("caution", Caution), pick("greed", Greed), pick("generosity", Generosity),
                pick("loyalty", Loyalty), pick("honor", Honor), pick("warFocus", WarFocus),
                pick("patience", Patience), pick("opportunism", Opportunism),
                pick("minimumRequestIntervalDays", MinimumRequestIntervalDays),
                pick("maximumTradeVolume", MaximumTradeVolume), overlay);
        }
    }

    // Contract representation, not a loader. The later external tool must parse JSON in
    // a separate, strictly bounded pre-session input pipeline with no executable patches.
    public sealed class AiLordProfileOverlay
    {
        public int SchemaVersion, BaseProfileSchema, TargetLordId;
        public string OverlayId, OriginalCustomAicFingerprint;
        public IDictionary<string,int> Values;
    }

    public static class AiLordEconomicCatalog
    {
        private static AiLordEconomicProfile P(int id, string name, int i, int m, int c, int g, int ge,
            int l, int h, int w, int pa, int op, int interval, int volume)
        {
            return new AiLordEconomicProfile(id, name, i, m, c, g, ge, l, h, w, pa, op, interval, volume);
        }

        // Explicit original IDs, not player slots, hashes or modulo fallback.
        private static readonly AiLordEconomicProfile[] Originals = {
            P( 1,"Rat",       25,60,80,45,30,45,50,30,75,20,5,10),
            P( 2,"Snake",     65,40,55,72,22,28,30,50,40,78,3,18),
            P( 3,"Pig",       80,25,25,95,12,18,18,70,20,90,2,30),
            P( 4,"Wolf",      40,45,85,65,15,20,35,88,65,50,5,14),
            P( 5,"Saladin",   55,55,45,25,78,90,95,70,62,28,3,20),
            P( 6,"Caliph",    72,35,45,88,12,25,20,80,28,82,3,24),
            P( 7,"Sultan",    50,60,46,52,60,54,58,30,60,44,4,18),
            P( 8,"Richard",   52,52,50,34,60,78,90,92,52,25,4,18),
            P( 9,"Frederick", 48,65,68,48,50,75,87,77,72,32,4,16),
            P(10,"Phillip",   35,66,73,50,38,63,62,44,78,37,5,12),
            P(11,"Wazir",     65,51,48,76,26,30,37,53,42,72,3,19),
            P(12,"Emir",      72,80,43,62,48,56,61,40,54,64,3,23),
            P(13,"Nizar",     40,47,72,70,24,28,25,52,72,78,5,12),
            P(14,"Sheriff",   44,69,67,47,45,78,81,58,72,28,4,15),
            P(15,"Marshal",   57,45,54,40,46,67,79,92,43,36,4,19),
            P(16,"Abbot",     28,83,78,31,53,82,86,25,88,14,6,10),
            P(17,"Jewel",     72,88,40,68,50,60,63,28,49,62,3,22),
            P(18,"Sentinel",  31,58,87,42,35,76,81,78,84,25,5,11),
            P(19,"Nomad",     60,54,45,46,51,48,54,62,40,61,3,20),
            P(20,"Kahin",     38,62,78,56,28,51,48,42,75,43,5,13),
            P(21,"Canary",    59,62,48,36,64,88,83,35,62,31,4,16),
            P(22,"Trader",    79,92,36,70,44,56,64,24,40,71,2,26),
            P(23,"Sergeant",  52,55,59,43,40,68,76,84,58,40,4,17),
            P(24,"Lioness",   63,46,42,40,55,73,84,80,48,54,3,20),
            P(25,"Crocodile", 74,33,47,85,21,26,29,70,38,88,3,25),
            P(26,"Baldwin",   56,53,51,36,65,92,90,64,61,26,4,18),
            P(27,"Bullseye",  71,40,36,67,31,35,41,86,32,80,3,22),
            P(28,"DLC4A",     50,50,50,50,50,50,50,50,50,50,4,16),
            P(29,"DLC4B",     50,50,50,50,50,50,50,50,50,50,4,16)
        };

        public static bool TryGet(int originalLordId, out AiLordEconomicProfile profile)
        {
            profile = null;
            if (originalLordId >= 1 && originalLordId <= 29)
                profile = Originals[originalLordId - 1];
            else if (originalLordId >= 30 && originalLordId <= 37)
                profile = P(originalLordId, "Custom X" + (originalLordId - 29).ToString(CultureInfo.InvariantCulture),
                    50,50,50,50,50,50,50,50,50,50,4,16);
            return profile != null; // 0, SK_TEMP=38 and unknown IDs are never remapped.
        }

        private static bool Hex64(string value)
        {
            return value != null && value.Length == 64 && value.All(
                c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }
        private static bool SafeOverlayId(string v)
        {
            return !String.IsNullOrEmpty(v) && v.Length <= 64 && v.All(c =>
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '-' || c == '_');
        }

        // An all-or-nothing overlay; an invalid patch never produces a partially
        // modified profile. Calling this does not install, activate or save patches.
        public static bool TryApply(AiLordProfileOverlay patch, string actualOriginalAicFingerprint,
            out AiLordEconomicProfile result, out string reason)
        {
            result = null; reason = "Invalid or unauthorised custom-lord profile";
            if (patch == null || patch.SchemaVersion != 1 || patch.BaseProfileSchema != 1 ||
                patch.TargetLordId < 30 || patch.TargetLordId > 37 ||
                !Hex64(actualOriginalAicFingerprint) ||
                !String.Equals(actualOriginalAicFingerprint, patch.OriginalCustomAicFingerprint, StringComparison.Ordinal) ||
                !SafeOverlayId(patch.OverlayId) || patch.Values == null || patch.Values.Count == 0 || patch.Values.Count > 12)
                return false;
            var values = new Dictionary<string,int>(StringComparer.Ordinal);
            try
            {
                foreach (var item in patch.Values)
                {
                    int min = 0, max = 100;
                    if (item.Key == "minimumRequestIntervalDays") { min = 1; max = 30; }
                    else if (item.Key == "maximumTradeVolume") { min = 1; max = 100; }
                    else if (!(new [] { "initiative","marketPreference","caution","greed","generosity",
                        "loyalty","honor","warFocus","patience","opportunism" }).Contains(item.Key, StringComparer.Ordinal))
                        return false;
                    if (item.Value < min || item.Value > max || values.ContainsKey(item.Key))
                        return false;
                    values.Add(item.Key,item.Value);
                }
                AiLordEconomicProfile baseProfile;
                if (!TryGet(patch.TargetLordId, out baseProfile))
                    return false;
                result = baseProfile.With(values,patch.OverlayId);
                reason = "Validated detached custom profile (not activated)";
                return true;
            }
            catch (ArgumentException) { result = null; return false; }
            catch (InvalidOperationException) { result = null; return false; }
        }
    }
}
