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
            foreach (string name in new[] { "get_deltaTime", "get_unscaledTime" })
                foreach (var method in time.Methods)
                    if (method.Name == name)
                    {
                        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
                        method.Body = new MethodBody(method);
                        var il = method.Body.GetILProcessor();
                        il.Append(il.Create(OpCodes.Ldc_R4, name == "get_deltaTime" ? 0.02f : 10f));
                        il.Append(il.Create(OpCodes.Ret));
                    }
            assembly.Write(args[1]);
        }
        Console.WriteLine("Created an isolated test-only Unity time shim.");
        return 0;
    }
}
