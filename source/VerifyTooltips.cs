using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

public static class TooltipDefinitions
{
    public enum Scope { All = 0, Simplified = 10, Detailed = 20 }
}
public sealed class GuiTooltipClassDefinition { }
public sealed class TooltipFeature { }
public sealed class GuiManager
{
    public bool HeldAlt;
    public bool IsShowingDetailedInformation { [MethodImpl(MethodImplOptions.NoInlining)] get { return HeldAlt; } }
}
public sealed class TooltipPanel
{
    public TooltipDefinitions.Scope LastScope;
    public bool ShowsDetails, ShowsHoldAlt, ShowsCommon;
    public object LastDefinition, LastFeatures;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetupFeatures(TooltipDefinitions.Scope scope, GuiTooltipClassDefinition definition,
        Dictionary<string, TooltipFeature> features)
    {
        LastScope = scope;
        LastDefinition = definition;
        LastFeatures = features;
        ShowsDetails = scope == TooltipDefinitions.Scope.Detailed;
        ShowsHoldAlt = scope == TooltipDefinitions.Scope.Simplified;
        ShowsCommon = true;
    }
}
public static class VerifyTooltips
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new Exception(message);
    }
    public static int Main(string[] args)
    {
        Assembly plugin = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type expanded = plugin.GetType("SolastaCoop.DetailedTooltips", true);
        MethodInfo toggle = expanded.GetMethod("SetEnabled", Flags);
        var gui = new GuiManager();
        var definition = new GuiTooltipClassDefinition();
        var features = new Dictionary<string, TooltipFeature>();
        var spell = new TooltipPanel();
        spell.SetupFeatures(TooltipDefinitions.Scope.Simplified, definition, features);
        Check(!spell.ShowsDetails && spell.ShowsHoldAlt, "Fixture reproduces compact spell tooltip before installation");
        toggle.Invoke(null, new object[] { true });
        Check((bool)expanded.GetProperty("Enabled", Flags).GetValue(null, null),
            "Real embedded Harmony installs a closed-generic enum-reference prefix");
        foreach (string context in new[] { "spell bar", "power bar", "inventory weapon", "inventory armor", "secondary comparison" })
        {
            var panel = new TooltipPanel();
            panel.SetupFeatures(TooltipDefinitions.Scope.Simplified, definition, features);
            Check(panel.ShowsDetails && !panel.ShowsHoldAlt && panel.ShowsCommon &&
                ReferenceEquals(panel.LastDefinition, definition) && ReferenceEquals(panel.LastFeatures, features),
                context + ": detailed feature scope replaces compact scope and preserves native content arguments");
        }
        Check(!gui.IsShowingDetailedInformation, "Enabled expansion does not force map Alt state or highlighting");
        gui.HeldAlt = true;
        spell.SetupFeatures(TooltipDefinitions.Scope.Detailed, definition, features);
        Check(spell.ShowsDetails && gui.IsShowingDetailedInformation,
            "Holding real Alt keeps detailed tooltips and native map state");
        spell.SetupFeatures(TooltipDefinitions.Scope.All, definition, features);
        Check(spell.LastScope == TooltipDefinitions.Scope.All, "Universal scope is preserved");
        spell.SetupFeatures((TooltipDefinitions.Scope)99, definition, features);
        Check((int)spell.LastScope == 99, "Unknown scope is preserved");
        toggle.Invoke(null, new object[] { false });
        gui.HeldAlt = false;
        spell.SetupFeatures(TooltipDefinitions.Scope.Simplified, definition, features);
        Check(!spell.ShowsDetails && spell.ShowsHoldAlt && !gui.IsShowingDetailedInformation,
            "Disabling returns compact tooltip and ordinary map state without Alt");
        gui.HeldAlt = true;
        spell.SetupFeatures(TooltipDefinitions.Scope.Detailed, definition, features);
        Check(spell.ShowsDetails && gui.IsShowingDetailedInformation,
            "Disabling preserves a detailed tooltip requested by real Alt");
        toggle.Invoke(null, new object[] { true });
        toggle.Invoke(null, new object[] { true });
        spell.SetupFeatures(TooltipDefinitions.Scope.Simplified, definition, features);
        string summary = (string)expanded.GetProperty("DiagnosticSummary", Flags).GetValue(null, null);
        Check(spell.ShowsDetails && summary.Contains("calls=11;") && summary.Contains("expanded=6;") &&
            summary.Contains("lastInputScope=Simplified"),
            "Repeated enabling does not duplicate hooks; diagnostics count actual feature setup and expansion");
        Console.WriteLine("Tooltip tests: PASS (real Harmony, fake native tooltip pipeline; nonstandard enum values)");
        return 0;
    }
}
