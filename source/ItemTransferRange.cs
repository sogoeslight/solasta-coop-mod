using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SolastaCoop
{
    // Replace only the distance argument immediately passed to IsWithinXCells
    // in the character inventory's receiver-selection refresh.
    internal static class ItemTransferRange
    {
        private const int VanillaCells = 6;
        private const int ExtendedCells = 9999;
        private static bool installed;
        private static int matched;
        internal static bool Enabled { get; private set; } = true;
        internal static string Status
        {
            get
            {
                if (!installed) return "Range patch was not installed; the standard 6-cell limit applies.";
                return (Enabled ? "Recipient selection: effectively unlimited range." : "Standard 6-cell limit.") +
                    " Patched checks: " + matched + ".";
            }
        }
        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled && installed;
            Log.Write("Extended item-transfer recipient range=" + Enabled);
        }
        // Called in patched IL instead of the game constant. No inventory state is written here.
        public static int AllowedCells()
        {
            return Enabled && installed ? ExtendedCells : VanillaCells;
        }
        internal static void Install()
        {
            if (installed) return;
            try
            {
                Type screen = Read.Type("CharacterInspectionScreen");
                MethodInfo original = screen == null ? null : screen.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SingleOrDefault(m => m.Name == "RefreshCharactersTable" && m.GetParameters().Length == 0);
                if (original == null) throw new MissingMethodException("CharacterInspectionScreen.RefreshCharactersTable");
                MethodInfo rewriter = typeof(ItemTransferRange).GetMethod("Transpile",
                    BindingFlags.NonPublic | BindingFlags.Static);
                HarmonySupport.Patch(original, transpiler: rewriter);
                installed = matched == 2;
                if (!installed)
                {
                    Enabled = false;
                    Log.Event("core", "WARN", "Unexpected item-transfer IL pattern; disabling extended range. matches=" + matched);
                }
                else Log.Write("Item transfer UI range patch installed; recipients can be selected at long distance.");
            }
            catch (Exception e)
            {
                installed = false;
                Enabled = false;
                Log.Error("core", "Install item-transfer range", e);
            }
        }
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> body = instructions.ToList();
            int replacements = 0;
            MethodInfo distance = typeof(ItemTransferRange).GetMethod("AllowedCells",
                BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i + 1 < body.Count; i++)
            {
                if (body[i].opcode != OpCodes.Ldc_I4_6) continue;
                if (body[i + 1].opcode != OpCodes.Callvirt && body[i + 1].opcode != OpCodes.Call) continue;
                MethodInfo target = body[i + 1].operand as MethodInfo;
                if (target == null || target.Name != "IsWithinXCells") continue;
                body[i].opcode = OpCodes.Call;
                body[i].operand = distance;
                replacements++;
            }
            matched = replacements;
            return body;
        }
    }
}
