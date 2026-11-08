// Disposable-test build ONLY: redirect the audited game's managed profile getters.
// This does not change Windows settings, the registry, the original assembly, or
// Application.persistentDataPath globally. Routes remain until process exit so
// the game's shutdown settings writer cannot escape to the real user profile.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using CrusaderDE;
using UnityEngine;

namespace StrongholdEuropeDE.Runtime
{
    // APIShared prepares personal settings during plugin initialization.
    // Register this dependency before APIShared in the pinned chainloader.
    [BepInPlugin(PluginId, "Diplomacy Profile", "2.3.11")]
    [BepInDependency("000shcdese", "2.14.3")]
    public sealed class PrivateProfileBootstrap : BaseUnityPlugin
    {
        internal const string PluginId = "000diplomacy.profile";
        private const string ProviderSha =
            "6eac4d696d91a388198dd5cc55ad3e35729ba21d881018fb9085004a3098e476";
        internal static bool Ready { get; private set; }

        private void Awake()
        {
            string game = DisposableGameScope.GameRoot;
            if (!Path.IsPathRooted(game) || !string.Equals(
                Path.Combine(game, "Stronghold Crusader Definitive Edition.exe"),
                Paths.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string profile = Path.GetFullPath(DisposableGameScope.ProfileRoot);
                string parent = Path.GetDirectoryName(game) + Path.DirectorySeparatorChar;
                if (!Directory.Exists(profile) ||
                    !profile.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    profile.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(profile, game, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The private profile must be beside the game.");
                for (string part = profile; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                    if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("The private profile contains a reparse point.");
                if (Chainloader.PluginInfos.ContainsKey("APIShared_Serp"))
                    throw new InvalidOperationException("APIShared initialized before profile routing.");
                string provider = Path.Combine(Paths.PluginPath, "APIShared_Serp", "APIShared.dll");
                if (!string.Equals(DisposableGameScope.HashFile(provider), ProviderSha, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected private APIShared assembly.");
                Assembly assembly = Assembly.LoadFrom(provider);
                if (!string.Equals(assembly.Location, provider, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("APIShared resolved outside the game copy.");
                Type type = assembly.GetType("APIShared.ModSettings.PersonalSettingsPaths", true);
                FieldInfo redirect = type.GetField("TestDocumentsDirectory", BindingFlags.Static | BindingFlags.NonPublic);
                if (redirect == null || redirect.FieldType != typeof(Func<string, string>))
                    throw new MissingFieldException(type.FullName, "TestDocumentsDirectory");
                string documents = Path.Combine(profile, "Documents");
                Directory.CreateDirectory(documents);
                redirect.SetValue(null, new Func<string, string>(_ => documents));
                Ready = true;
                Logger.LogInfo("DIPLOMACY_PRIVATE_PROVIDER_PROFILE_READY");
            }
            catch (Exception error)
            {
                Logger.LogError("Private provider profile refused: " + error.GetType().Name);
                Environment.Exit(2);
            }
        }
    }

    internal static class PrivateProfileRouting
    {
        private static bool installed;
        private static readonly string[] Getters =
        {
            "GetSettingsFileName",
            "GetSavesPath",
            "GetUserMapsPath",
            "GetMpAutoSavePath",
            "GetUserCustomLordsPath",
            "GetUserExtendedLordsPath",
            "GetUserCustomTrailsPath",
            "GetUserTrailMakerPath",
            "GetUserCustomMediaPath",
            "GetUserTrailMakerBackupPath",
            "GetScoresFileName",
            "GetSkirmishMastersFileName",
            "GetCoopFileName",
            "GetCustomTrailsCompletedFileName",
            "GetUserWorkshopPath",
            "GetWorkshopUploadRootPath"
        };
        internal static bool Install(Action<string> log)
        {
            if (installed)
                return true;
            try
            {
                using (var p = Process.GetCurrentProcess())
                    if (p.MainModule == null || !string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.Combine(DisposableGameScope.GameRoot, "Stronghold Crusader Definitive Edition.exe"), StringComparison.OrdinalIgnoreCase))
                        return false;
                string managedReason;
                if (!VerifiedManagedSurface.VerifyGameAssembly(out managedReason))
                {
                    if (log != null)
                        log("_GAME_RUNTIME_SURFACE_REFUSED " + managedReason);
                    return false;
                }

                if (log != null)
                    log("_GAME_RUNTIME_SURFACE_VERIFIED " + managedReason);
                string root = DisposableGameScope.ProfileRoot;
                if (!Directory.Exists(root))
                    return false;
                for (string path = root; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        return false;
                var assembly = typeof(MainViewModel).Assembly;
                var config = assembly.GetType("ConfigSettings", true);
                var targets = new List<MethodInfo>();
                foreach (string name in Getters)
                {
                    var found = config.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Where(m => m.Name == name).ToArray();
                    if (found.Length != 1 || found[0].ReturnType != typeof(string))
                        return false;
                    targets.Add(found[0]);
                }

                var avatars = assembly.GetType("Avatars", true);
                foreach (string name in new[]
                {
                    "GetAvatarTexture",
                    "OutputCoAPNG"
                }

                )
                {
                    var found = avatars.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Where(m => m.Name == name).ToArray();
                    if (found.Length != 1)
                        return false;
                    targets.Add(found[0]);
                }

                var options = assembly.GetType("CrusaderDE.HUD_Options", true);
                var click = options.GetMethod("ButtonClicked", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (click == null)
                    return false;
                targets.Add(click);
                var harmony = new Harmony("strongholdeuropede..disposable.profile");
                var transpiler = new HarmonyMethod(typeof(PrivateProfileRouting).GetMethod("Route", BindingFlags.NonPublic | BindingFlags.Static));
                foreach (var method in targets)
                {
                    if (log != null)
                        log("_PRIVATE_PROFILE_PATCH_BEGIN " + method.DeclaringType.FullName + "::" + method.Name);
                    harmony.Patch(method, transpiler: transpiler);
                }

                // Independent managed getter readback, not merely successful Patch().
                var savePath = config.GetMethod("GetSavesPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                string actual = (string)savePath.Invoke(null, null);
                if (!Path.GetFullPath(actual).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                Directory.CreateDirectory(actual);
                installed = true;
                if (log != null)
                    log("_PRIVATE_PROFILE_ROUTING_VERIFIED methods=" + targets.Count + " saves=" + actual);
                return true;
            }
            catch (Exception e)
            {
                if (log != null)
                    log("_PRIVATE_PROFILE_ROUTING_FAILED " + e.ToString());
                return false;
            }
        }

        private static string ProfileRoot()
        {
            return DisposableGameScope.ProfileRoot;
        }

        private static IEnumerable<CodeInstruction> Route(IEnumerable<CodeInstruction> source)
        {
            var original = typeof(Application).GetProperty("persistentDataPath", BindingFlags.Public | BindingFlags.Static).GetGetMethod();
            var replacement = typeof(PrivateProfileRouting).GetMethod("ProfileRoot", BindingFlags.NonPublic | BindingFlags.Static);
            int count = 0;
            foreach (var instruction in source)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo && ((MethodInfo)instruction.operand) == original)
                {
                    instruction.operand = replacement;
                    count++;
                }

                yield return instruction;
            }

            if (count < 1)
                throw new InvalidOperationException("Audited persistent-data call absent");
        }
    }
}
#endif
