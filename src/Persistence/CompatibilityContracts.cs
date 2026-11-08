// Stable storage, loader and digest identifiers preserve existing data and peers.
// These values are external compatibility contracts; implementation names are independent.
namespace StrongholdEuropeDE.Runtime
{
    internal static class CompatibilityContracts
    {
        internal const string LegacySaveDataIdentifier = "StrongholdEuropeDE.Diplomacy.v1";
        internal const string SaveDataIdentifier = "StrongholdEuropeDE.Diplomacy.v2";
        internal const string FeatureId = "gameplay.diplomacy.v2";
        internal const string PluginId = "strongholdeuropede.diplomatie.v2.private";
        internal const string HarmonyId = "strongholdeuropede.diplomatie.v2.harmony";
        internal const string LegacySaveMagic = "SEDE-DIP-V1\0";
        internal const string LegacyChecksummedSaveMagic = "SEDE-DIP-V1C\0";
        internal const string SaveMagic = "SEDE-DIP-V2\0";
        internal const string LegacyMapFingerprintDomain = "map-v2";
        internal const string TradeMapFingerprintSuffix = "|trade-v63";
        internal const string EngineRosterFingerprintDomain = "V114_ENGINE_STEAM_ROSTER_1";
        internal const string NativeStartLayoutDomain = "V114_NATIVE_START_LAYOUT_1";
        internal const string OriginalMapFingerprintDomain = "V114_ORIGINAL_MAP_1";
        internal const string TradeRulesDomain = "V114_TRADE_RULES_1|codec=19|canonical=3|signed=1";
        internal const string MultiplayerWorldDigestDomain = "V121_ORIGINAL_MP_WORLD_DIGEST_SHARED_EPOCH_V2";
        internal const string ConsentWorldDigestDomain = "V124_ORIGINAL_MP_CONSENT_ONLY_CAPACITY_UNKNOWN_V1";
        internal const string SavedOwnerProjectionDomain = "V126_ACTUAL_SCHEMA19_SHARED_OWNER_PROJECTION_V1";
#if DIPLOMACY_PRIVATE_TESTS
        internal const string PrivateAidReceiptDomain = "SE_PRIVATE_ORIGINAL_AID_SAVE_V1";
        internal const string PrivateTradeColdExpectationDomain = "V114_NATIVE_COLD_EXPECTATION_V1";
        internal const string PrivateAidColdExpectationDomain = "V9_ORIGINAL_AI_AID_COLD_V1";
#endif
    }
}
