using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SolastaCoop
{
    internal static class HarmonySupport
    {
        private static object patcher;
        private static Type methodType;
        private static MethodInfo patch;
        internal static string Version { get; private set; }

        private static void Initialize()
        {
            if (patcher != null) return;
            Assembly harmony = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("HarmonyLib.Harmony", false) != null);
            if (harmony == null)
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SolastaCoop.Harmony"))
                {
                    if (stream == null) throw new FileNotFoundException("Embedded Harmony resource missing.");
                    using (var bytes = new MemoryStream()) { stream.CopyTo(bytes); harmony = Assembly.Load(bytes.ToArray()); }
                }
            }
            Type harmonyType = harmony.GetType("HarmonyLib.Harmony", true);
            methodType = harmony.GetType("HarmonyLib.HarmonyMethod", true);
            patch = harmonyType.GetMethods().FirstOrDefault(m => m.Name == "Patch" && m.GetParameters().Length == 5);
            if (patch == null) throw new MissingMethodException("Harmony.Patch");
            patcher = Activator.CreateInstance(harmonyType, new object[] { "SolastaCoopView." + BuildInfo.Version });
            Version = harmony.GetName().Version.ToString();
        }

        internal static void Patch(MethodInfo target, MethodInfo prefix = null, MethodInfo postfix = null, MethodInfo transpiler = null)
        {
            if (target == null) throw new MissingMethodException("Patch target missing.");
            Initialize();
            object before = prefix == null ? null : Activator.CreateInstance(methodType, new object[] { prefix });
            object after = postfix == null ? null : Activator.CreateInstance(methodType, new object[] { postfix });
            object transformation = transpiler == null ? null : Activator.CreateInstance(methodType, new object[] { transpiler });
            patch.Invoke(patcher, new[] { (object)target, before, after, transformation, null });
        }
    }

    internal static class DetailedTooltips
    {
        internal static bool Enabled { get; private set; }
        internal static string Status { get; private set; }
        private static bool installed;
        private static int setupCalls, expandedCalls;
        private static string lastScope = "none";
        internal static string DiagnosticSummary
        {
            get { return Status + "\nTooltipPanel.SetupFeatures patched=" + installed +
                "; calls=" + setupCalls + "; expanded=" + expandedCalls + "; lastInputScope=" + lastScope; }
        }

        private static class ScopeValues<T> where T : struct
        {
            internal static readonly T Simplified = (T)Enum.Parse(typeof(T), "Simplified");
            internal static readonly T Detailed = (T)Enum.Parse(typeof(T), "Detailed");
        }

        internal static void SetEnabled(bool enabled)
        {
            try
            {
                if (enabled && !installed)
                {
                    Type panel = Read.Type("TooltipPanel"), scope = Read.Type("TooltipDefinitions+Scope");
                    if (panel == null || scope == null || !scope.IsEnum ||
                        !Enum.IsDefined(scope, "Simplified") || !Enum.IsDefined(scope, "Detailed"))
                        throw new MissingMemberException("TooltipPanel / TooltipDefinitions.Scope API missing.");
                    MethodInfo target = panel.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .SingleOrDefault(m => m.Name == "SetupFeatures" && m.ReturnType == typeof(void) &&
                            m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == scope);
                    MethodInfo prefix = typeof(DetailedTooltips).GetMethod("ExpandScope", BindingFlags.NonPublic | BindingFlags.Static)
                        .MakeGenericMethod(scope);
                    HarmonySupport.Patch(target, prefix);
                    installed = true;
                    Log.Write("Detailed tooltip patch installed: TooltipPanel.SetupFeatures; scope=" + scope.FullName +
                        "; Harmony=" + HarmonySupport.Version);
                }
                Enabled = enabled;
                Status = enabled ? "Подробные тултипы способностей и предметов всегда включены." : "Подробные тултипы по Alt.";
                Log.Write(Status);
            }
            catch (Exception e)
            {
                Enabled = false;
                Status = "Не удалось включить подробные тултипы: " + e.GetBaseException().Message;
                Log.Error("core", "Detailed tooltips", e);
            }
        }
        private static void ExpandScope<T>(ref T __0) where T : struct
        {
            setupCalls++;
            lastScope = __0.ToString();
            if (Enabled && EqualityComparer<T>.Default.Equals(__0, ScopeValues<T>.Simplified))
            {
                __0 = ScopeValues<T>.Detailed;
                expandedCalls++;
            }
            if (setupCalls == 1) Log.Write("Native tooltip feature hook reached; " + DiagnosticSummary);
            if (Log.Verbose && (setupCalls <= 5 || setupCalls % 100 == 0))
                Log.Event("core", "DEBUG", "Native tooltip feature setup; " + DiagnosticSummary);
        }
    }
}
