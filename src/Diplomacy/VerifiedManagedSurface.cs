// Generated from the SHA-pinned original PE method bodies, not guessed live bytes.
// BepInEx loads some game assemblies from byte arrays: empty Assembly.Location is
// NOT ignored, but replaced here by verification of the actual runtime IL bodies
// of every critical managed boundary plus the untouched original file digest.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CrusaderDE;
using SHCDESE.API;

namespace StrongholdEuropeDE.Runtime
{
    internal static class VerifiedManagedSurface
    {
        private sealed class Pin
        {
            internal readonly string Type, Method, Hash;
            internal readonly int Count;
            internal Pin(string t, string m, int c, string h)
            {
                Type = t;
                Method = m;
                Count = c;
                Hash = h;
            }
        }

        internal const string OriginalRunDigest =
            "3ed0f74b6940ef7209b7e09e2f716e4c019d4058d02895c8325293fc43b75db7";

        private static readonly Pin[] GamePins =
        {
            new Pin("ConfigSettings", "GetSettingsFileName", 1, "cfc79831bb4eccd9f0661d1082093cc231e40c61967ed1b242c72fcfefbb42cd"),
            new Pin("ConfigSettings", "GetSavesPath", 0, "aa1b232ba80632ba44e6ada3bc5e169cab80a09a61ad44b146b724275161c626"),
            new Pin("ConfigSettings", "GetUserMapsPath", 0, "7866c33dbb5a62c2d970d74b3501f785827bdccc5aad436d36a50e9249e4ce44"),
            new Pin("ConfigSettings", "GetMpAutoSavePath", 0, "e8853df3ed02735269629beb54d0235f54f75fba0da39b40b073a06c4617485c"),
            new Pin("ConfigSettings", "GetUserCustomLordsPath", 0, "00313ae4b8c30be79367b13fb60161d4d14648af80085894f770301efa846cbc"),
            new Pin("ConfigSettings", "GetUserExtendedLordsPath", 0, "d357033e561205dc124d49e853c89acb7edb108b382ed711bde5e6f6106f3624"),
            new Pin("ConfigSettings", "GetUserCustomTrailsPath", 0, "0a1e2420d48652f13ca9a87699d52da979aeffd1e21ccdc2042d4a5805fb06c2"),
            new Pin("ConfigSettings", "GetUserTrailMakerPath", 0, "ac9980c0ddaff11d84f95f5b8a8b04876b6683867a2d8865bfe6f0db6fa91bad"),
            new Pin("ConfigSettings", "GetUserCustomMediaPath", 0, "f25cc356ec0d4505ae47c785912c0668d0a611dc18c1b41a1d14a9d4aaa95ee4"),
            new Pin("ConfigSettings", "GetUserTrailMakerBackupPath", 0, "bef5b103d14f6e9387e570515eb7e281b9aef09ccff2c8ca46c5537f488b04d1"),
            new Pin("ConfigSettings", "GetScoresFileName", 0, "daa282801e28e3dcfe948b7e11eba0ca8e927e5bf73c065d351b27e7b2f2e9a9"),
            new Pin("ConfigSettings", "GetSkirmishMastersFileName", 0, "7902b346e2f9f69960b437f09634512a2bc8a89dde99853afd8912246a899c0c"),
            new Pin("ConfigSettings", "GetCoopFileName", 0, "6c28bec99dc504895258e0afc77aaf3835cf57a984a7bf2a1c7ac6bb244bc860"),
            new Pin("ConfigSettings", "GetCustomTrailsCompletedFileName", 0, "1e70b7c56020ebaebaf8570d7645db2d44926c18748ff192b379a82b941007c8"),
            new Pin("ConfigSettings", "GetUserWorkshopPath", 0, "98116fc18ffe20614f8fa6b79cb9d4e1a9e0eff1397ed66ca99241af39398a23"),
            new Pin("ConfigSettings", "GetWorkshopUploadRootPath", 0, "bbc3fa7d1f878e83a9263ab4e8717be7c246738824b483fee6de9ea51ca93d28"),
            new Pin("EngineInterface", "SaveSaveGame", 8, "f7fef582d78d162632af5d276fea53be00f6ca945d399b25a0881ffef5b2f1c0"),
            new Pin("EngineInterface", "run", 1, OriginalRunDigest),
            new Pin("EngineInterface", "LoadSaveFile", 1, "e6e52a8530dd418fc435f83358c42b027fc5395b03e28c4063a4243dfe7f50fd"),
            new Pin("EngineInterface", "ReceiveChore", 3, "f7c6666705ac7c5a4f7fbefadaa0c5f78f6d7d90c3b0497170b058c54b55ee03"),
        };
        private static readonly Pin[] RawraPins =
        {
            new Pin("SHCDESE.API.GameTimeManagerAPI", "GetElapsedMapTicks", 0, "e836333c3707d320c4698c4cc29c08ab90ec34c02c4405e25126d61d4c4195ce"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "GetGoodAmount", 2, "7901209404e76654807fbc23e42a3576bfc9fcbdd8bfb167c91714291e74320a"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "TryAddGood", 3, "c38483d8a3903a6a9041186e1174468b26e67b2b2b3f8eb2c95974443c80e066"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "RemoveGood", 4, "42f626af9e20005aa785352e313810f960dff3d8cfd2e8a68bbb7ee5127ccd08"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "GetTradeBasePrice", 1, "38484ab94b2020759502872983b17b837da6f44b82b711cb2d7931f5e8a6af6c"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "IsTradeGoodAllowed", 1, "2124fb52db8de3e5486f7ed8619f3c6a1cdc07fdf2db61ff7232a605eb4fe2c4"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "IsPlayerAlliedTo", 2, "ea560f42d2f997b4970e1eeb1a30a60e1590f86eceee95db0c10f36b38cb1fad"),
            new Pin("SHCDESE.API.GamePlayerManagerAPI", "GetPlayerTeam", 1, "123ee12b06b73a514f28e9771b883c1a684ebe12e1b3217fd83add02c4e32911"),
            new Pin("SHCDESE.API.GameNetworkAPI", "SendScriptExtenderChorePayload", 1, "36d269af7baf420dc17e8b48714248abb3b6c532dfbdb237bc7aca62aafa2d8f"),
            new Pin("SHCDESE.API.GameNetworkAPI", "DispatchReceivedScriptExtenderPayload", 1, "53d95c47027186b3d9c457f69aa66b2968993d41701ea26ecabab544b5b5b82a"),
            new Pin("SHCDESE.API.GameNetworkAPI", "HandleRawPacket", 3, "4e93994820cd2374607dc5cb5aaecafa551127b884a2a08e66d73bfccab079e0"),
            new Pin("SHCDESE.API.GameNetworkAPI", "GetHostSteamId", 0, "e7060b7ddec6343a086729b7e8f2d37ea4b4e2cf6d3c90e3267736b57a2269af"),
            new Pin("SHCDESE.API.GameNetworkAPI", "GetPlayerIdForSteamId", 1, "b9b8a278da69cfb711d66cc37c0db5bfd1da04404d1ea132e2243f5458530957"),
            new Pin("SHCDESE.API.GameNetworkAPI", "IsLocalHost", 0, "770afed0477dddf314387edca602980274cd41c47f8b279ea30761f1ce21e194"),
            new Pin("SHCDESE.API.ModSaveDataAPI", "WriteAllModDataToArchive", 1, "b620d2098115bee49b05031a3742579180cc7a4b6b015b4abb8102f584cabc92"),
            new Pin("SHCDESE.API.ModSaveDataAPI", "ReadAllModDataFromArchive", 1, "dade18ea7398cde75accfec093a67a7710c48ab53ee4a40715012eb0d9e702c8"),
            new Pin("SHCDESE.API.ModSaveDataAPI", "GetModDataFileName", 1, "b5ecb02c4a56ec4ee8c45e34b22e4906c845dea34e626a70b5df62acb47aa1da"),
            new Pin("SHCDESE.API.GameMapArchiveManagerAPI", "PrepareMapArchiveAndGetBytes", 1, "caf73a06b850026857b7e668e2d1e94b6a3db0e5e4efb2c7ca68f3245146a3a8"),
            new Pin("SHCDESE.API.GameMapArchiveManagerAPI", "SetCurrentFileContext", 2, "bc71575860cfe91de990ed3a2411316a9bdc7916511e12f54c7567f346391c52"),
            new Pin("SHCDESE.API.Components.Archive.MapArchive", "TryLoad", 2, "7ab151b0d46a77721e579f49fdd877f8c88653e1288c6be174b21b807d81bcb3"),
            new Pin("SHCDESE.API.Components.Archive.MapArchive", "TryReadBinaryFile", 2, "f9a1f38dbd6533a7ca92f20af6457576efb383f4044809488c1be28b4d01971f"),
            new Pin("SHCDESE.API.GameTimeManagerAPI", "GetElapsedMapTicks", 0, "e836333c3707d320c4698c4cc29c08ab90ec34c02c4405e25126d61d4c4195ce"),
        };
        private static string Digest(byte[] data)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static bool Verify(Assembly actual, string original, string sha, Pin[] pins, out string reason)
        {
            reason = "managed_surface_unverified";
            try
            {
                if (!File.Exists(original) || DisposableGameScope.HashFile(original) != sha)
                {
                    reason = "original_PE_changed";
                    return false;
                }

                foreach (var p in pins)
                {
                    Type type = actual.GetType(p.Type, false);
                    if (type == null)
                    {
                        reason = "missing_type:" + p.Type;
                        return false;
                    }

                    var methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(x => x.Name == p.Method && x.GetParameters().Length == p.Count).ToArray();
                    if (methods.Length != 1)
                    {
                        reason = "ambiguous_runtime_method:" + p.Type + "::" + p.Method;
                        return false;
                    }

                    var body = methods[0].GetMethodBody();
                    byte[] il = body == null ? null : body.GetILAsByteArray();
                    string current = il == null ? "missing" : CanonicalManagedIL.Digest(methods[0]);
                    if (current != p.Hash)
                    {
                        reason = "runtime_semantic_IL_differs:" + p.Type + "::" + p.Method + "|actual=" + current + "|expected=" + p.Hash;
#if DIPLOMACY_PRIVATE_TESTS
                        try
                        {
                            string q = Path.Combine(Path.GetDirectoryName(DisposableGameScope.ProfileRoot), "qa");
                            File.WriteAllText(Path.Combine(q, "RUNTIME_IL_" + p.Type.Replace('.', '_') + "_" + p.Method + ".txt"), CanonicalManagedIL.Normalize(methods[0]));
                        }
                        catch
                        {
                        }

#endif
                        return false;
                    }
                }

                reason = "actual_runtime_IL_matched:" + pins.Length + "|mvid=" + actual.ManifestModule.ModuleVersionId;
                return true;
            }
            catch (Exception e)
            {
                reason = "runtime_IL_verification_failed:" + e.GetType().Name;
                return false;
            }
        }

        internal static bool VerifyGameAssembly(out string reason)
        {
            return Verify(typeof(MainViewModel).Assembly, Path.Combine(DisposableGameScope.GameRoot, @"Stronghold Crusader Definitive Edition_Data\Managed\Assembly-CSharp.dll"), DisposableGameScope.GameManagedSha, GamePins, out reason);
        }

        internal static bool VerifyRawraAssembly(out string reason)
        {
            return Verify(typeof(GameNetworkAPI).Assembly, Path.Combine(DisposableGameScope.GameRoot, @"BepInEx\plugins\000shcdese\SHCDESE.dll"), DisposableGameScope.RawraSha, RawraPins, out reason);
        }
    }
}
