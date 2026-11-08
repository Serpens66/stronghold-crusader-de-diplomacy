using System;
using System.IO;
using System.Reflection;

namespace StrongholdEuropeDE.Runtime
{
    // Provenance check, not an authorization for original/native goods mutation.
    // The original monitor/native-image/loaded-IL gates are independently required.
    internal static class RuntimePrerequisiteOriginPolicy
    {
        private static bool Same(string first, string second)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Under(string root, string path)
        {
            string start = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(start, StringComparison.OrdinalIgnoreCase);
        }

        // Diagnostic returns only bounded origin KINDS, never full profile,
        // account, game or executable paths.
        private static string Kind(string actual, string root, string primary, string secondary)
        {
            if (string.IsNullOrWhiteSpace(actual))
                return "absent";
            if (!Path.IsPathRooted(actual))
                return "pathless_or_virtual";
            try
            {
                string full = Path.GetFullPath(actual);
                if (Same(full, primary))
                    return "pinned_plugin_file";
                if (!string.IsNullOrEmpty(secondary) && Same(full, secondary))
                    return "pinned_preloader_file";
                if (Under(root, full))
                    return "other_game_file";
                return "external_file";
            }
            catch (Exception e)
            {
                if (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                    return "unresolvable";
                return "unresolvable";
            }
        }

        private static bool HasReparseAncestor(string file, string gameRoot)
        {
            for (string path = file; !string.IsNullOrEmpty(path) && Under(gameRoot, path); path = Path.GetDirectoryName(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }

            return false;
        }

        private static bool VerifiedSecondSystemMemoryFile(string path, string root)
        {
            // The primary copy is checked by RuntimeBuildGuard's existing
            // strict SHA/size/managed-identity verification.
            if (!Under(root, path) || !File.Exists(path) || HasReparseAncestor(path, root))
                return false;
            int index = Array.IndexOf(RuntimePrerequisiteCatalog.PrerequisiteIds, "system_memory");
            if (index < 0)
                return false;
            string checksum = Hashing.Sha256FileStable(path, RuntimePrerequisiteCatalog.PrerequisiteSizes[index]);
            if (!Same(checksum, RuntimePrerequisiteCatalog.PrerequisiteSha256[index]))
                return false;
            AssemblyName identity = AssemblyName.GetAssemblyName(path);
            return string.Equals(identity.Name, RuntimePrerequisiteCatalog.PrerequisiteAssemblyNames[index], StringComparison.Ordinal) && string.Equals(identity.Version.ToString(), RuntimePrerequisiteCatalog.PrerequisiteAssemblyVersions[index], StringComparison.Ordinal);
        }

        internal static bool Matches(string dependencyId, string gameRootPath, string expectedPath, string location, string modulePath, bool allowPathlessUnityLoad, out string diagnostic)
        {
            diagnostic = "runtime_origin_unverified";
            try
            {
                string root = Path.GetFullPath(gameRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string expected = Path.GetFullPath(expectedPath);
                if (!Under(root, expected))
                {
                    diagnostic = "runtime_expected_file_outside_private_game";
                    return false;
                }

                string preloader = null;
                if (string.Equals(dependencyId, "system_memory", StringComparison.Ordinal))
                    preloader = Path.GetFullPath(Path.Combine(root, RuntimePrerequisiteCatalog.SystemMemoryPreloaderRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                string locationKind = Kind(location, root, expected, preloader);
                string moduleKind = Kind(modulePath, root, expected, preloader);
                diagnostic = "loaded_origin_refused:" + dependencyId + "|assembly_path_kind=" + locationKind + "|module_path_kind=" + moduleKind;
                // Reject any foreign *rooted* path or malformed pseudo-path.
                if (locationKind == "external_file" || moduleKind == "external_file" || locationKind == "other_game_file" || moduleKind == "other_game_file" || locationKind == "unresolvable" || moduleKind == "unresolvable")
                    return false;
                if (preloader != null)
                {
                    // System.Memory was found in the original SHCDESE preloader
                    // and plugin with equal SHA and CLR metadata. Require an
                    // actual PHYSICAL loaded origin; both pathless is unknown,
                    // never a filename/MVID- or QA-flag-based permission.
                    bool locationExpected = locationKind == "pinned_plugin_file";
                    bool moduleExpected = moduleKind == "pinned_plugin_file";
                    bool locationPreloader = locationKind == "pinned_preloader_file";
                    bool modulePreloader = moduleKind == "pinned_preloader_file";
                    if (!(locationExpected || moduleExpected || locationPreloader || modulePreloader))
                        return false;
                    if ((locationExpected || moduleExpected) && (locationPreloader || modulePreloader))
                        return false;
                    if (locationPreloader || modulePreloader)
                    {
                        if (!VerifiedSecondSystemMemoryFile(preloader, root))
                        {
                            diagnostic = "system_memory_preloader_artifact_not_attested";
                            return false;
                        }

                        diagnostic = "loaded_origin_exact_verified_preloader_file";
                    }
                    else
                    {
                        diagnostic = "loaded_origin_exact_pinned_plugin_file";
                    }

                    return true;
                }

                bool match = RuntimeDecision.LoadedAssemblyPathsMatch(expected, allowPathlessUnityLoad, location, modulePath);
                if (!match)
                    return false;
                if (locationKind == "pinned_plugin_file" || moduleKind == "pinned_plugin_file")
                {
                    diagnostic = "loaded_origin_exact_file";
                    return true;
                }

                // This does NOT attest a native module. Unity can load
                // managed PE bytes without a physical CLR Location;
                // the separate VerifiedManagedSurface and native guards
                // must still verify runtime IL, original bytes, module image,
                // original monitor and game-instance identity.
                if (!allowPathlessUnityLoad)
                    return false;
                if (string.Equals(dependencyId, "assembly_csharp", StringComparison.Ordinal))
                {
                    string proof;
                    if (!VerifiedManagedSurface.VerifyGameAssembly(out proof))
                    {
                        diagnostic = "pathless_original_game_runtime_IL_unattested";
                        return false;
                    }
                }

                diagnostic = "unity_managed_pathless_metadata_only_not_native_authority";
                return true;
            }
            catch (Exception e)
            {
                diagnostic = "origin_check_failed:" + e.GetType().Name;
                return false;
            }
        }
    }
}
