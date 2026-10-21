using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static void Main(string[] args)
    {
        string originalDll = args[0];
        string patchedDll = args[1];

        using var origModule = ModuleDefinition.ReadModule(originalDll);
        using var patchModule = ModuleDefinition.ReadModule(patchedDll);

        foreach (var origType in origModule.Types)
        {
            var patchType = patchModule.GetType(origType.FullName);
            if (patchType == null) continue;

            foreach (var origMethod in origType.Methods)
            {
                var patchMethod = patchType.Methods.FirstOrDefault(m => m.FullName == origMethod.FullName);
                if (patchMethod == null || !origMethod.HasBody) continue;

                var origInsts = origMethod.Body.Instructions;
                var patchInsts = patchMethod.Body.Instructions;

                bool methodPrinted = false;

                for (int i = 0; i < origInsts.Count; i++)
                {
                    if (origInsts[i].OpCode != patchInsts[i].OpCode)
                    {
                        if (!methodPrinted) { Console.WriteLine($"\nMethod: {origMethod.FullName}"); methodPrinted = true; }
                        Console.WriteLine($"  [{i}] OpCode: {origInsts[i].OpCode} -> {patchInsts[i].OpCode}");
                    }
                    else if (origInsts[i].Operand is int or float or sbyte)
                    {
                        if (!Equals(origInsts[i].Operand, patchInsts[i].Operand))
                        {
                            if (!methodPrinted) { Console.WriteLine($"\nMethod: {origMethod.FullName}"); methodPrinted = true; }
                            Console.WriteLine($"  [{i}] Operand: {origInsts[i].Operand} -> {patchInsts[i].Operand}");
                        }
                    }
                }
            }
        }
    }
}
