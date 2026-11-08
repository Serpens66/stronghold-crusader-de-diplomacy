// Generated from the checked-in runtime prerequisite catalog.
// The data and the C# surface must match data/runtime-prerequisites.json.
// This catalogue proves physical artifacts and CLR identities only;
// it cannot establish an unobservable pathless module's provenance.
namespace StrongholdEuropeDE.Runtime
{
    internal static class RuntimePrerequisiteCatalog
    {
        internal const int PrerequisiteCount = 18;
        internal const string DependencyLockSha256 = "1eb88dc99dd216d0c39f3671ab5acf268442e620a13631209de097a8f53a5d89";
        internal const string OriginalEngineRelativePath = "Stronghold Crusader Definitive Edition_Data/Plugins/x86_64/CrusaderDE.dll";
        internal const long OriginalEngineSize = 3451392L;
        internal const string OriginalEngineSha256 = "fbcb93195fc7efca9bdac5204852efdd76f9818f59a6711750d77c9cef2831e2";
        internal const string SystemMemoryPreloaderRelativePath = "BepInEx/patchers/SHCDESE.KillSwitch/System.Memory.dll";

        internal static readonly string[] PrerequisiteIds = new string[]
        {
            "shcdese",
            "nlua",
            "keralua",
            "redbird_core",
            "system_memory",
            "bepinex_core",
            "harmony",
            "r3",
            "assembly_csharp",
            "noesis_gui",
            "unity_engine",
            "unity_engine_core_module",
            "unity_engine_asset_bundle_module",
            "unity_engine_audio_module",
            "unity_engine_image_conversion_module",
            "redbird_abstractions",
            "redbird_backend",
            "redbird_x64",
        };

        internal static readonly string[] PrerequisiteRelativePaths = new string[]
        {
            "BepInEx/plugins/000shcdese/SHCDESE.dll",
            "BepInEx/plugins/000shcdese/NLua.dll",
            "BepInEx/plugins/000shcdese/KeraLua.dll",
            "BepInEx/plugins/000shcdese/RedBird.Core.dll",
            "BepInEx/plugins/000shcdese/System.Memory.dll",
            "BepInEx/core/BepInEx.dll",
            "BepInEx/core/0Harmony.dll",
            "BepInEx/plugins/000shcdese/R3.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/Assembly-CSharp.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/Noesis.NoesisGUI.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/UnityEngine.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/UnityEngine.CoreModule.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/UnityEngine.AssetBundleModule.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/UnityEngine.AudioModule.dll",
            "Stronghold Crusader Definitive Edition_Data/Managed/UnityEngine.ImageConversionModule.dll",
            "BepInEx/plugins/000shcdese/RedBird.Abstractions.dll",
            "BepInEx/plugins/000shcdese/RedBird.Backends.NativeX64.dll",
            "BepInEx/plugins/000shcdese/RedBird.X64.dll",
        };

        internal static readonly long[] PrerequisiteSizes = new long[]
        {
            1585152L,
            92160L,
            30720L,
            119296L,
            145200L,
            128512L,
            204800L,
            604160L,
            3693056L,
            1356800L,
            111352L,
            1275640L,
            31992L,
            69368L,
            22776L,
            28160L,
            44544L,
            173568L,
        };

        internal static readonly string[] PrerequisiteSha256 = new string[]
        {
            "5cb1c33be5463086505a17f5c48aaa357d733cfff7240e101c821f0e3c668498",
            "476fff1274aa8a85d18df8ca1cfc1f420ed4654dcf9aba0957b93b978a3592f6",
            "02095f7fe9d24c0999e2bd073038a1161b569c1a64be894804116d5b195a858e",
            "ee4b036c486077f7d8898ae054f124edde73e6ae1a059e8991c361e2b720d29f",
            "d5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d",
            "8255b28902886085c578b9e427d3073c97002db85176d2090cdeda90ef14ce70",
            "1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031",
            "8d77d5e9b30f6bdc2aa60055a5950ea75d259c9d1c9667a04a7d598e1da9d609",
            "bc8b6a395f01d48557db413600c8dd8d1fdfd3abdf97bfbbb68a3c56b04fd789",
            "98476d3ca84ae0f2dcfbaddcc64b01a1f65474bd44402673fd6856d1b5347648",
            "6abba25864b843149b594c7831dbf5eb7afe87c55d5112a4739ca4da09b91280",
            "e468e19789d608b41e2c7fe4f367c7082fece7d1883f568e75870f6c5384cc86",
            "0ba5cb05eb305368c96124efc9104e65a17cc8872822147c0252644d4efecb83",
            "9cf8c391390e5178c75cdfb186da3dc9f8679a31491f69739aaa6f65bbd0724d",
            "1fe9119a493265849938f734e384a7a63cf6b8c75ed20c43c742feb601eba8ed",
            "339cece116f66f59ecc7da5e9a5c0b2dfe24f4e7a5e7efc005864fa77c027a1e",
            "0843dd4c381a3e77dd6d8b51d5ccf95465b3fb49bfdf2980f39a212d774aadb0",
            "e085b83b2b87b5d2142c980090d8d9ec616a90a443bd125c17b41e0bb47a44eb",
        };

        internal static readonly string[] PrerequisiteAssemblyNames = new string[]
        {
            "SHCDESE",
            "NLua",
            "KeraLua",
            "RedBird.Core",
            "System.Memory",
            "BepInEx",
            "0Harmony",
            "R3",
            "Assembly-CSharp",
            "Noesis.NoesisGUI",
            "UnityEngine",
            "UnityEngine.CoreModule",
            "UnityEngine.AssetBundleModule",
            "UnityEngine.AudioModule",
            "UnityEngine.ImageConversionModule",
            "RedBird.Abstractions",
            "RedBird.Backends.NativeX64",
            "RedBird.X64",
        };

        internal static readonly string[] PrerequisiteAssemblyVersions = new string[]
        {
            "2.14.3.0",
            "1.7.9.0",
            "1.4.9.0",
            "1.5.0.0",
            "4.0.5.0",
            "5.4.23.5",
            "2.9.0.0",
            "1.3.1.0",
            "0.0.0.0",
            "0.0.0.0",
            "0.0.0.0",
            "0.0.0.0",
            "0.0.0.0",
            "0.0.0.0",
            "0.0.0.0",
            "1.5.0.0",
            "1.5.0.0",
            "1.5.0.0",
        };

        internal static readonly string[] PrerequisiteMvids = new string[]
        {
            "aab7af1d-dabf-4f55-a7a6-dd67c8f06853",
            "401e1007-417b-4366-a608-f6b8ffe5ca2e",
            "4ab3a0be-7da2-4255-8556-dcab69a7adc4",
            "0ca8fc0a-fb1d-4661-b68d-51f2000bad45",
            "27c7dfb4-b639-4500-8914-8d6e6caca8cb",
            "d1b92069-86c2-41dc-ad96-bb21eee55a97",
            "474744d6-5d8e-460f-a08c-d5fd82b5d65f",
            "b58e2dc8-2e7f-419e-bf7d-bc9786b1bf15",
            "29ade4ef-f587-422f-a1eb-f81dff066e0a",
            "7383317c-d471-469f-ac85-2aafde4cb013",
            "2098d95a-268b-46a0-9676-e36d11d685a5",
            "806021f9-55ec-43bb-97cd-1adca41e00da",
            "90538353-7aa0-4a16-ad89-f45382d0c777",
            "54f216ae-0646-4f4f-aa73-c4817fcfcede",
            "ee44ec0b-022d-49ac-b713-628f3e55be93",
            "873c04a2-0fff-42d5-b3dc-ba2cc2bb14ec",
            "1ffce45a-1f84-4f06-bbb9-b7d67c7e4f75",
            "88e48873-c306-479a-9b1e-2d2b8b92f514",
        };

    }
}
