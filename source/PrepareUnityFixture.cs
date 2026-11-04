using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class PrepareUnityFixture
{
    public static int Main(string[] args)
    {
        using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
        {
            var time = assembly.MainModule.GetType("UnityEngine.Time");
            var elapsed = new FieldDefinition("CoopTestElapsed", FieldAttributes.Public | FieldAttributes.Static,
                assembly.MainModule.TypeSystem.Single);
            time.Fields.Add(elapsed);
            foreach (string name in new[] { "get_deltaTime", "get_unscaledTime" })
                foreach (var method in time.Methods)
                    if (method.Name == name)
                    {
                        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
                        method.Body = new MethodBody(method);
                        var il = method.Body.GetILProcessor();
                        il.Append(il.Create(OpCodes.Ldc_R4, name == "get_deltaTime" ? 0.02f : 10f));
                        if (name == "get_unscaledTime")
                        {
                            il.Append(il.Create(OpCodes.Ldsfld, elapsed));
                            il.Append(il.Create(OpCodes.Add));
                        }
                        il.Append(il.Create(OpCodes.Ret));
                    }
            var preferences = assembly.MainModule.GetType("UnityEngine.PlayerPrefs");
            foreach (var method in preferences.Methods)
                if (((method.Name == "GetInt" || method.Name == "GetFloat") && method.Parameters.Count == 2) ||
                    ((method.Name == "SetInt" || method.Name == "SetFloat") && method.Parameters.Count == 2) ||
                    (method.Name == "Save" && method.Parameters.Count == 0))
                {
                    method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
                    method.Body = new MethodBody(method);
                    var il = method.Body.GetILProcessor();
                    if (method.Name == "GetInt" || method.Name == "GetFloat") il.Append(il.Create(OpCodes.Ldarg_1));
                    il.Append(il.Create(OpCodes.Ret));
                }
            assembly.Write(args[1]);
        }
        Console.WriteLine("Created isolated test-only Unity time and preference shims.");
        return 0;
    }
}
