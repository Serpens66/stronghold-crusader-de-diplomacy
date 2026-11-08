using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class CompatibilityReport
    {
        internal readonly List<string> MissingAssemblies = new List<string>();
        internal readonly List<string> MissingTypes = new List<string>();
        internal readonly List<string> MissingMembers = new List<string>();
        internal readonly List<string> Unattested = new List<string>();
        // Diagnostics are non-authoritative. Any unverified original-game,
        // extender, managed runtime or loaded module identity is a blocker
        // for the strict private native compatibility decision.
        internal readonly List<string> GameSideDrift = new List<string>();
        internal int BindingsChecked;
        internal int BindingsPresent;
        internal int PrerequisitesTotal;
        internal int PrerequisitesAttested;
        // Presence observation only. Valid native identity also requires
        // all exact prerequisite, loaded-origin and engine checks below.
        internal bool ExtenderAttested;
        internal bool EngineAttested;
        internal string EngineDigestPrefix = "?";
        internal string EngineFailure;
        internal bool Compatible
        {
            get
            {
                // Both the member surface and original runtime identities are required.
                return MissingAssemblies.Count == 0 && MissingTypes.Count == 0 && MissingMembers.Count == 0 && IdentityAttested;
            }
        }

        internal bool IdentityAttested
        {
            get
            {
                // Reporting drift is never evidence for native authority.
                // Every checked-in original/managed prerequisite and the
                // separately original-pinned CrusaderDE native image must pass.
                return ExtenderAttested && EngineAttested && PrerequisitesTotal == RuntimePrerequisiteCatalog.PrerequisiteCount && PrerequisitesAttested == PrerequisitesTotal && Unattested.Count == 0 && GameSideDrift.Count == 0;
            }
        }

        internal string FirstProblem
        {
            get
            {
                if (MissingAssemblies.Count > 0)
                {
                    return "assembly:" + MissingAssemblies[0];
                }

                if (MissingTypes.Count > 0)
                {
                    return "type:" + MissingTypes[0];
                }

                if (MissingMembers.Count > 0)
                {
                    return "member:" + MissingMembers[0];
                }

                if (Unattested.Count > 0)
                    return "prerequisite_unattested:" + Unattested[0];
                if (!ExtenderAttested)
                    return "extender_missing:no_loaded_SHCDESE";
                if (!IdentityAttested)
                    return "prerequisite_contract_incomplete";
                return null;
            }
        }
    }

    /// <summary>
    /// Attestiert zuerst Engine und verwaltete Voraussetzungen exakt gegen
    /// den erzeugten Bauvertrag. Erst danach werden Mitglieder aufgeloest.
    /// </summary>
    /// <remarks>
    /// Entscheidung unbekannte Versionen bleiben passiv.
    /// Name und Parameterzahl belegen weder eine CLR-Signatur noch native
    /// Layouts. Deshalb ersetzt die Mitgliederliste niemals die Attestation.
    /// Eine spaetere SE-Version braucht einen neu belegten Bauvertrag; ein
    /// erfolgreiches externes Signatur-Preflight allein aktiviert sie nicht.
    ///
    /// Wirft nie: eine Pruefung, die selbst scheitert, zaehlt als fehlend
    /// bzw. unbeglaubigt und wird benannt.
    /// </remarks>
    internal static class DiplomatieCompatibilityGuard
    {
        private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        internal static CompatibilityReport Probe()
        {
            var report = new CompatibilityReport();
            try
            {
                ProbeEngine(report);
            }
            catch (Exception exception)
            {
                report.EngineAttested = false;
                report.EngineFailure = exception.GetType().Name;
            }

            try
            {
                ProbePrerequisites(report);
            }
            catch (Exception exception)
            {
                report.Unattested.Add("probe_failed:" + exception.GetType().Name);
            }

            // Do not resolve native managers/bridges or load arbitrary member
            // assemblies until every expected binary has been attested.
            if (!report.IdentityAttested)
                return report;
            try
            {
                ProbeBindings(report);
            }
            catch (Exception exception)
            {
                report.MissingMembers.Add("probe_failed:" + exception.GetType().Name);
            }

            try
            {
                // The native bridge has separate live-bound method contracts;
                // this member presence check is not a native authority grant.
                string bridgeFailure;
                if (!DiplomacyFeature.ScriptExtenderNativeBridge.TryResolve(out bridgeFailure))
                {
                    report.MissingMembers.Add("bridge:" + bridgeFailure);
                }
                else
                {
                    report.BindingsChecked += 5;
                    report.BindingsPresent += 5;
                }
            }
            catch (Exception exception)
            {
                report.MissingMembers.Add("bridge_probe_failed:" + exception.GetType().Name);
            }

            try
            {
                report.BindingsChecked++;
                string inventoryFailure;
                if (DiplomacyFeature.TryResolveNativeInventoryMonitor(out inventoryFailure))
                    report.BindingsPresent++;
                else
                    report.MissingMembers.Add("inventory_monitor:" + inventoryFailure);
            }
            catch (Exception exception)
            {
                report.MissingMembers.Add("inventory_monitor_probe_failed:" + exception.GetType().Name);
            }

            return report;
        }

        private static void ProbeBindings(CompatibilityReport report)
        {
            var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(name) && !assemblies.ContainsKey(name))
                {
                    assemblies[name] = assembly;
                }
            }

            var typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
            var missingAssemblies = new HashSet<string>(StringComparer.Ordinal);
            var missingTypes = new HashSet<string>(StringComparer.Ordinal);
            int count = GeneratedCompatibilityContract.Count;
            for (int index = 0; index < count; index++)
            {
                report.BindingsChecked++;
                string assemblyName = GeneratedCompatibilityContract.Assemblies[index];
                string typeName = GeneratedCompatibilityContract.Types[index];
                string member = GeneratedCompatibilityContract.Members[index];
                int kind = GeneratedCompatibilityContract.Kinds[index];
                int parameters = GeneratedCompatibilityContract.Parameters[index];
                Assembly assembly;
                if (!assemblies.TryGetValue(assemblyName, out assembly))
                {
                    assembly = TryLoad(assemblyName);
                    if (assembly != null)
                    {
                        assemblies[assemblyName] = assembly;
                    }
                }

                if (assembly == null)
                {
                    if (missingAssemblies.Add(assemblyName))
                    {
                        report.MissingAssemblies.Add(assemblyName);
                    }

                    continue;
                }

                string typeKey = assemblyName + "|" + typeName;
                Type type;
                if (!typeCache.TryGetValue(typeKey, out type))
                {
                    try
                    {
                        type = assembly.GetType(typeName, false);
                    }
                    catch
                    {
                        type = null;
                    }

                    typeCache[typeKey] = type;
                }

                if (type == null)
                {
                    if (missingTypes.Add(typeKey))
                    {
                        report.MissingTypes.Add(assemblyName + "/" + typeName);
                    }

                    continue;
                }

                if (kind == 0)
                {
                    report.BindingsPresent++;
                    continue;
                }

                bool present;
                try
                {
                    present = kind == 2 ? type.GetField(member, AllMembers) != null : HasMethod(type, member, parameters);
                }
                catch
                {
                    present = false;
                }

                if (present)
                {
                    report.BindingsPresent++;
                }
                else
                {
                    report.MissingMembers.Add(assemblyName + "/" + typeName + "::" + member + (parameters >= 0 ? "(" + parameters.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty));
                }
            }
        }

        private static bool HasMethod(Type type, string member, int parameters)
        {
            if (member == ".ctor" || member == ".cctor")
            {
                foreach (ConstructorInfo constructor in type.GetConstructors(AllMembers))
                {
                    if (parameters < 0 || constructor.GetParameters().Length == parameters)
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (MemberInfo candidate in type.GetMember(member, MemberTypes.Method, AllMembers))
            {
                var method = candidate as MethodInfo;
                if (method == null)
                {
                    continue;
                }

                if (parameters < 0 || method.GetParameters().Length == parameters)
                {
                    return true;
                }
            }

            return false;
        }

        private static Assembly TryLoad(string name)
        {
            try
            {
                return Assembly.Load(new AssemblyName(name));
            }
            catch
            {
                return null;
            }
        }

        private static void ProbePrerequisites(CompatibilityReport report)
        {
            int count = RuntimePrerequisiteCatalog.PrerequisiteIds.Length;
            report.PrerequisitesTotal = count;
            if (count <= 0 || count != RuntimePrerequisiteCatalog.PrerequisiteCount || RuntimePrerequisiteCatalog.PrerequisiteRelativePaths.Length != count || RuntimePrerequisiteCatalog.PrerequisiteSizes.Length != count || RuntimePrerequisiteCatalog.PrerequisiteSha256.Length != count || RuntimePrerequisiteCatalog.PrerequisiteAssemblyNames.Length != count || RuntimePrerequisiteCatalog.PrerequisiteAssemblyVersions.Length != count || RuntimePrerequisiteCatalog.PrerequisiteMvids.Length != count)
                throw new InvalidDataException("malformed compiled prerequisite contract");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                string id = RuntimePrerequisiteCatalog.PrerequisiteIds[index];
                try
                {
                    if (string.IsNullOrEmpty(id) || !ids.Add(id))
                        throw new InvalidDataException("duplicate or empty prerequisite identifier");
                    string path = Path.Combine(Paths.GameRootPath, RuntimePrerequisiteCatalog.PrerequisiteRelativePaths[index].Replace('/', Path.DirectorySeparatorChar));
                    // ANWESENHEIT des Extenders bleibt Pflicht - ohne
                    // geladene SHCDESE gibt es keine API, an der die
                    // Diplomatie haengen koennte. Das ist keine Versions-,
                    // sondern eine Existenzfrage und steht deshalb VOR der
                    // Identitaetspruefung, damit eine fremde Fassung sie
                    // nicht ueberspringt.
                    if (string.Equals(id, "shcdese", StringComparison.Ordinal))
                    {
                        if (!string.Equals(RuntimePrerequisiteCatalog.PrerequisiteAssemblyNames[index], "SHCDESE", StringComparison.Ordinal))
                            throw new InvalidDataException("invalid extender assembly name");
                        bool loaded = false;
                        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                            if (string.Equals(assembly.GetName().Name, "SHCDESE", StringComparison.Ordinal))
                                loaded = true;
                        if (!loaded)
                            throw new InvalidDataException("extender is not loaded");
                    // Presence alone is not provenance or native authority.
                    }

                    if (!File.Exists(path))
                    {
                        NoteUnattested(report, id, "absent");
                        continue;
                    }

                    // Attest file SHA, size, CLR identity and loaded origin.
                    // This is a strict prerequisite check, not a native
                    // module mapping or permission to mutate original goods.
                    if (NoteIdentity(report, id, index) && string.Equals(id, "shcdese", StringComparison.Ordinal))
                        report.ExtenderAttested = true;
                }
                catch (Exception exception)
                {
                    // Every exception, including runtime-version, path and
                    // actual loaded-module identity issues, is a blocker.
                    report.Unattested.Add(id + ":" + PrerequisiteFailureDetail(exception));
                }
            }
        }

        /// <summary>
        /// Distinguishes game-side artifacts from extender-side artifacts
        /// in bounded diagnostics. Both classes remain strictly pinned.
        /// </summary>
        private static readonly HashSet<string> GameSidePrerequisites = new HashSet<string>(StringComparer.Ordinal)
        {
            "assembly_csharp",
            "noesis_gui",
            "unity_engine",
            "unity_engine_core_module",
            "unity_engine_asset_bundle_module",
            "unity_engine_audio_module",
            "unity_engine_image_conversion_module",
        };
        private static void NoteUnattested(CompatibilityReport report, string id, string detail)
        {
            // Preserve the diagnostic distinction but NEVER upgrade drift
            // into proof. Both original game and extender failures count
            // against the exact managed/native compatibility contract.
            string kind = id != null && GameSidePrerequisites.Contains(id) ? "game:" : "extender:";
            report.GameSideDrift.Add(kind + id + ":" + detail);
            report.Unattested.Add(id + ":" + detail);
        }

        private static bool NoteIdentity(CompatibilityReport report, string id, int index)
        {
            try
            {
                // Only strict verification is admissible here. Never call a
                // ReportingDrift overload to declare a loaded module trusted.
                RuntimeBuildGuard.VerifyOptional(id);
                report.PrerequisitesAttested++;
                return true;
            }
            catch (Exception exception)
            {
                NoteUnattested(report, id, PrerequisiteFailureDetail(exception));
                return false;
            }
        }

        private static string PrerequisiteFailureDetail(Exception exception)
        {
            // Keep the verifier's reason (file SHA, identity, loaded MVID/path)
            // without allowing exception text to add log lines or fields.
            string message = exception.Message ?? string.Empty;
            int length = Math.Min(message.Length, 240);
            var detail = new char[length];
            for (int index = 0; index < length; index++)
            {
                char character = message[index];
                detail[index] = char.IsControl(character) || character == '|' ? ' ' : character;
            }

            return exception.GetType().Name + ":" + new string (detail) + (message.Length > length ? "..." : string.Empty);
        }

        private static void ProbeEngine(CompatibilityReport report)
        {
            string path = Path.Combine(Paths.GameRootPath, RuntimePrerequisiteCatalog.OriginalEngineRelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                string digest = Hashing.Sha256FileStable(path, RuntimePrerequisiteCatalog.OriginalEngineSize);
                report.EngineDigestPrefix = digest.Length >= 12 ? digest.Substring(0, 12) : digest;
                report.EngineAttested = string.Equals(digest, RuntimePrerequisiteCatalog.OriginalEngineSha256, StringComparison.Ordinal);
                if (!report.EngineAttested)
                {
                    report.EngineFailure = "digest";
                }
            }
            catch (Exception exception)
            {
                report.EngineAttested = false;
                report.EngineFailure = exception.GetType().Name;
                try
                {
                    var info = new FileInfo(path);
                    report.EngineDigestPrefix = info.Exists ? "size:" + info.Length.ToString(CultureInfo.InvariantCulture) : "missing";
                }
                catch
                {
                    report.EngineDigestPrefix = "?";
                }
            }
        }
    }
}
