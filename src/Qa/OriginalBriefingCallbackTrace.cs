// original briefing callback - PRIVATE QA ONLY. Observational Harmony Postfixes on original
// UI/Director methods. No GameAction hook, no changes to native/UI arguments.
// The trace is explicitly armed only for one isolated original game restart.
#if DIPLOMACY_PRIVATE_TESTS
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using CrusaderDE;

namespace StrongholdEuropeDE.Runtime
{
    internal static class OriginalBriefingCallbackTrace
    {
        private const string HarmonyId =
            "strongholdeuropede.diplomatie.private.qa-original-briefing-readonly";
        private static readonly object Gate = new object();
        private static bool _installed;
        private static int _armed;
        private static int _callbackCount, _openCount, _showCount, _faultCount;
        private static MethodInfo Target(Type owner, string name, Type argument)
        {
            return owner.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { argument }, null);
        }
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "none";
            return text.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
        }
        private static string CallerTrace()
        {
            try
            {
                var trace = new StackTrace(2, false);
                var frames = trace.GetFrames();
                if (frames == null) return "none";
                string joined = string.Join(">", frames.Take(12).Select(f =>
                {
                    MethodBase m = f.GetMethod();
                    return m == null ? "unknown" :
                        (m.DeclaringType == null ? "global" :
                        m.DeclaringType.FullName) + "." + m.Name;
                }));
                if (joined.Length > 850) joined = joined.Substring(0, 850);
                return Sanitize(joined);
            }
            catch { return "stack_unavailable"; }
        }
        private static void SafeNote(string status, string payload)
        {
            if (Volatile.Read(ref _armed) != 1) return;
            try
            {
                HeadlessQaDriver.Note("status=" + status +
                    "|thread=" + Thread.CurrentThread.ManagedThreadId +
                    "|origin_stack=" + CallerTrace() +
                    "|data=" + Sanitize(payload));
            }
            catch
            {
                Interlocked.Increment(ref _faultCount);
            }
        }
        private static void AfterSetOriginalCallback(Action __0)
        {
            if (Volatile.Read(ref _armed) != 1) return;
            if (Interlocked.Increment(ref _callbackCount) > 24) return;
            SafeNote("original_director_callback_registered",
                "method=" + (__0 == null ? "null" :
                    (__0.Method.DeclaringType == null ? "unknown" :
                        __0.Method.DeclaringType.FullName) + "." + __0.Method.Name));
        }
        private static void AfterOriginalOpenBriefing(object __0)
        {
            if (Volatile.Read(ref _armed) != 1) return;
            if (Interlocked.Increment(ref _openCount) > 24) return;
            SafeNote("original_briefing_open_called",
                "argument=" + (__0 == null ? "null" : __0.ToString()));
        }
        private static void AfterOriginalVisibility(bool __0)
        {
            if (!__0 || Volatile.Read(ref _armed) != 1) return;
            if (Interlocked.Increment(ref _showCount) > 24) return;
            SafeNote("original_briefing_visible_true", "visible=true");
        }
        internal static bool TryInstall()
        {
            lock (Gate)
            {
                if (_installed) return true;
                try
                {
                    MethodInfo callback = Target(typeof(Director),
                        "SetPostUpdateCallback", typeof(Action));
                    MethodInfo open = Target(typeof(MainViewModel),
                        "ButtonGotoBriefing", typeof(object));
                    MethodInfo show = Target(typeof(MainViewModel),
                        "set_Show_HUD_Briefing", typeof(bool));
                    if (callback == null || open == null || show == null)
                    {
                        HeadlessQaDriver.Note(
                            "status=readonly_original_callback_surface_missing");
                        return false;
                    }
                    var flags = BindingFlags.Static | BindingFlags.NonPublic;
                    var owner = new Harmony(HarmonyId);
                    owner.Patch(callback, postfix: new HarmonyMethod(
                        typeof(OriginalBriefingCallbackTrace).GetMethod(
                            nameof(AfterSetOriginalCallback), flags)));
                    owner.Patch(open, postfix: new HarmonyMethod(
                        typeof(OriginalBriefingCallbackTrace).GetMethod(
                            nameof(AfterOriginalOpenBriefing), flags)));
                    owner.Patch(show, postfix: new HarmonyMethod(
                        typeof(OriginalBriefingCallbackTrace).GetMethod(
                            nameof(AfterOriginalVisibility), flags)));
                    _installed = true;
                    HeadlessQaDriver.Note(
                        "status=three_readonly_original_methods_observed" +
                        "|gameaction_hook_added=false|ui_mutations=false");
                    return true;
                }
                catch (Exception error)
                {
                    HeadlessQaDriver.Note(
                        "status=readonly_instrumentation_unavailable" +
                        "|reason=" + error.GetType().Name);
                    return false;
                }
            }
        }
        internal static void Arm()
        {
            Volatile.Write(ref _armed, 1);
            HeadlessQaDriver.Note(
                "status=original_callback_trace_armed|bounded=true");
        }
        internal static void Stop()
        {
            Volatile.Write(ref _armed, 0);
            HeadlessQaDriver.Note(
                "status=original_callback_trace_disarmed" +
                "|callback_regs=" + Volatile.Read(ref _callbackCount) +
                "|briefing_opens=" + Volatile.Read(ref _openCount) +
                "|visibility_true=" + Volatile.Read(ref _showCount) +
                "|diagnostic_faults=" + Volatile.Read(ref _faultCount) +
                "|no_native_effects=true");
        }
    }
}
#endif
