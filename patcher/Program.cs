using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EtgVerify;
using Mono.Cecil;
using Mono.Cecil.Cil;

// exit codes: 0 ok, 1 verify failed / error, 2 usage, 3 already patched, 4 not Enter the
// Gungeon, 5 --target does not match the dll's platform
//
// usage:
//   patcher <ManagedDir>                    <ManagedDir>/Assembly-CSharp.dll -> Assembly-CSharp-43.dll beside it, full verify
//   patcher <Assembly-CSharp.dll>           reference-free verify, Assembly-CSharp-43.dll beside the input
//   patcher <input.dll> [--target win|mac|linux|auto] [--refs <ManagedDir>] [--out <output.dll>]
// the platform is read from the dll; --target only refuses a mismatch (auto, the default, accepts any).
//
// a windows user can drag Assembly-CSharp.dll (or the Managed folder) onto patcher.exe; that is the
// one-argument form above. explorer gives such a launch its own console window that closes when the
// process exits, so Main waits for Enter first (see WaitIfOwnConsole).
class Program
{
    static int Main(string[] args)
    {
        int code = Run(args);
        WaitIfOwnConsole();
        return code;
    }

    // kernel32 is only touched from WaitIfOwnConsole (windows, from Main). the browser build links
    // this file too but enters through WasmEntry, so it compiles the declaration and never calls it.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    // true when this process is the only one attached to its console: launched from explorer by drag
    // or double-click rather than from cmd/powershell (which also attach), patch.py or ci.
    static void WaitIfOwnConsole()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || Console.IsInputRedirected) return;
            if (GetConsoleProcessList(new uint[2], 2) != 1) return;
            Console.WriteLine();
            Console.WriteLine("Press Enter to close this window.");
            Console.ReadLine();
        }
        catch (Exception)
        {
            // no console, or kernel32 unavailable: just exit
        }
    }

    static int Run(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine($"   Enter the Gungeon 4:3 Aspect Ratio Patcher {PatchApi.Version}");
        Console.WriteLine("==================================================");

        string? input = null, target = null, refs = null, output = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--target" && i + 1 < args.Length) target = args[++i];
            else if (args[i] == "--refs" && i + 1 < args.Length) refs = args[++i];
            else if (args[i] == "--out" && i + 1 < args.Length) output = args[++i];
            else if (!args[i].StartsWith("--") && input == null) input = args[i];
            else return Usage($"unexpected argument '{args[i]}'");
        }
        if (input == null) return Usage("missing <Assembly-CSharp.dll or Managed folder>");
        if (target is not (null or "win" or "mac" or "linux" or "auto")) return Usage("--target must be win, mac, linux or auto");

        // a flag-free launch is the drag-and-drop form: it gets the next-steps text
        bool bare = args.Length == 1;
        if (Directory.Exists(input))
        {
            // a Managed folder: its own unity dlls are the refs, so the verify is Full
            refs ??= input;
            output ??= Path.Combine(input, "Assembly-CSharp-43.dll");
            input = Path.Combine(input, "Assembly-CSharp.dll");
        }
        target ??= "auto";
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"[!] cannot find {input}");
            if (bare) Console.WriteLine("Use the Assembly-CSharp.dll from the game's Managed folder, or drop that folder itself.");
            return 1;
        }
        if (refs != null && !Directory.Exists(refs)) { Console.Error.WriteLine($"[!] refs dir {refs} does not exist"); return 1; }
        output ??= target == "auto" || bare
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, "Assembly-CSharp-43.dll")
            : $"Assembly-CSharp-43-{target}.dll";

        Console.WriteLine($"[+] input {input}");
        var result = PatchApi.Patch(File.ReadAllBytes(input), target, refs);
        Console.Write(result.Log);
        if (result.Passed + result.Failed + result.Skipped > 0)
        {
            Console.Write(result.VerifyLog);
            foreach (var f in result.Failures) Console.WriteLine($"    - {f}");
        }

        if (result.Output == null)
        {
            Console.Error.WriteLine($"[!] nothing written: {result.Reason}");
            if (bare) PrintFailure(result.Reason);
            return result.Reason switch
            {
                "already_patched" => 3,
                "not_etg" => 4,
                "platform_mismatch" => 5,
                _ => 1,
            };
        }

        File.WriteAllBytes(output, result.Output);
        Console.WriteLine($"[+] saved {output}");
        Console.WriteLine($"    platform {result.Platform}, {result.Summary}");
        Console.WriteLine($"    sha256 {result.Sha256}");
        if (bare) PrintNextSteps(output, result.Platform);
        return 0;
    }

    static int Usage(string why)
    {
        Console.Error.WriteLine($"[!] {why}");
        Console.WriteLine();
        Console.WriteLine("Drag Assembly-CSharp.dll (or the whole Managed folder) onto patcher.exe.");
        Console.WriteLine("For Enter the Gungeon on Windows it is in:  Enter the Gungeon\\EtG_Data\\Managed");
        Console.WriteLine();
        Console.WriteLine("From a terminal:");
        Console.WriteLine("  patcher <ManagedDir>                    full verify, writes Assembly-CSharp-43.dll in that folder");
        Console.WriteLine("  patcher <Assembly-CSharp.dll>           writes Assembly-CSharp-43.dll beside it");
        Console.WriteLine("  patcher <input.dll> [--target win|mac|linux|auto] [--refs <ManagedDir>] [--out <output.dll>]");
        return 2;
    }

    static void PrintNextSteps(string output, string? platform)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        Console.WriteLine();
        Console.WriteLine($"Patched and verified for the {platform} build of the game. Your original was not changed. Next steps:");
        Console.WriteLine($"  1. Open the folder {dir}");
        Console.WriteLine("  2. Back up the original: rename Assembly-CSharp.dll to Assembly-CSharp.dll.bak");
        Console.WriteLine("  3. Rename Assembly-CSharp-43.dll to Assembly-CSharp.dll");
        Console.WriteLine("  4. In the game's Options > Video, set the resolution to your screen's and fullscreen.");
        Console.WriteLine("  To undo, put the .bak back, or use Steam: Verify integrity of game files.");
        Console.WriteLine("  Patch the file from the install you will play: a Mac dll on a handheld (DroidDeck, GameNative) hangs the boot.");
    }

    static void PrintFailure(string reason)
    {
        Console.WriteLine();
        switch (reason)
        {
            case "already_patched":
                Console.WriteLine("This Assembly-CSharp.dll is already patched, so nothing was written.");
                Console.WriteLine("Restore Assembly-CSharp.dll.bak, or use Steam (Enter the Gungeon > Properties > Installed Files >");
                Console.WriteLine("Verify integrity of game files), then patch the original again.");
                break;
            case "not_etg":
                Console.WriteLine("That is not Enter the Gungeon's Assembly-CSharp.dll, so nothing was written.");
                Console.WriteLine("Pick the Assembly-CSharp.dll inside the game's Managed folder (EtG_Data\\Managed).");
                break;
            default:
                Console.WriteLine("Verification failed, so nothing was written and your game is untouched.");
                Console.WriteLine("The game has probably been updated and the patch no longer matches.");
                Console.WriteLine("Please open an issue: https://github.com/jevonlipsey/gungeon-43aspect/issues");
                break;
        }
    }

    /// the literal GetPlatformName returns, or null if the method is not the expected shape
    internal static string? ReadPlatform(ModuleDefinition module)
    {
        var get = module.Types.FirstOrDefault(t => t.Name == "AkBasePathGetter")
            ?.Methods.FirstOrDefault(m => m.Name == "GetPlatformName" && m.HasBody);
        if (get == null) return null;
        var lits = get.Body.Instructions
            .Where(i => i.OpCode.Code == Code.Ldstr && !string.IsNullOrEmpty((string)i.Operand)).ToList();
        return lits.Count == 1 ? (string)lits[0].Operand : null;
    }

    /// <summary>
    /// runs every patch on an open assembly and returns the written bytes. sites are found
    /// by il pattern only: the mac and windows dlls are different compiles, so no offset or
    /// token from one is valid in the other.
    /// </summary>
    internal static byte[] PatchAssembly(AssemblyDefinition asm)
    {
        var module = asm.MainModule;

        // the helper goes in first; every site calls into the clone. its unity calls
        // (Screen.width ...) import against the UnityEngine.CoreModule row the game already has,
        // so no hand-built core refs and no new AssemblyReference rows are needed
        var helper = InjectHelper(module);

        PatchAspectGetters(module, helper);
        PatchForceAspect(module, helper);
        PatchPixelator(module);
        PatchRecommendedResolution(module, helper);

        using var ms = new MemoryStream();
        asm.Write(ms);
        var bytes = ms.ToArray();
        VerifyBranches(bytes);
        Console.WriteLine($"[+] wrote patched assembly ({bytes.Length} bytes)");
        return bytes;
    }

    // ---- site lookup ----

    static MethodDefinition FindMethod(ModuleDefinition module, string typeName, string methodName)
    {
        var types = module.Types.Where(t => t.Name == typeName && t.Namespace == "").ToList();
        if (types.Count != 1) throw new InvalidOperationException($"expected one type {typeName}, found {types.Count}");
        var methods = types[0].Methods.Where(m => m.Name == methodName && m.HasBody).ToList();
        if (methods.Count != 1) throw new InvalidOperationException($"expected one method {typeName}.{methodName}, found {methods.Count}");
        return methods[0];
    }

    static MethodDefinition HelperMethod(TypeDefinition helper, string name, int paramCount = 0)
    {
        var ms = helper.Methods.Where(m => m.Name == name && m.Parameters.Count == paramCount).ToList();
        if (ms.Count != 1) throw new InvalidOperationException($"Gungeon43.{name}/{paramCount}: expected one method, found {ms.Count}");
        return ms[0];
    }

    static bool IsR4(Instruction ins, float v) => ins.OpCode.Code == Code.Ldc_R4 && (float)ins.Operand == v;

    static bool IsI4(Instruction ins, int v) => ins.OpCode.Code switch
    {
        Code.Ldc_I4 => (int)ins.Operand == v,
        Code.Ldc_I4_S => (sbyte)ins.Operand == v,
        _ => false,
    };

    // every patched constant is asserted: a different compile that has two of them (or none)
    // must stop the run instead of patching the wrong one
    static Instruction ExactlyOne(MethodDefinition method, Func<Instruction, bool> match, string what)
    {
        var hits = method.Body.Instructions.Where(match).ToList();
        if (hits.Count != 1)
            throw new InvalidOperationException($"{method.DeclaringType.Name}.{method.Name}: expected exactly 1 {what}, found {hits.Count}");
        return hits[0];
    }

    // ---- sites ----

    /// S1/S2: `if (Gungeon43.Active()) return Gungeon43.X(OverrideAspect.HasValue);` ahead of the
    /// original body. it runs before the OverrideAspect check on purpose, so the menu's 16:9
    /// override is ignored on narrow screens; the flag only tells the helper to use the menu's
    /// 270-row budget instead of the gameplay one. outside narrow screens the original body runs.
    static void PatchAspectGetters(ModuleDefinition module, TypeDefinition helper)
    {
        // the field and get_HasValue references come from the original get_ASPECT body, so
        // the patch adds no member reference rows of its own
        var aspectGetter = FindMethod(module, "BraveCameraUtility", "get_ASPECT");
        // (the getter loads the field twice, for HasValue and for Value; take the HasValue pair)
        var hasValueCall = ExactlyOne(aspectGetter,
            i => i.OpCode.Code == Code.Call && i.Operand is MethodReference mr && mr.Name == "get_HasValue", "call get_HasValue");
        if (hasValueCall.Previous?.OpCode.Code != Code.Ldsflda || ((FieldReference)hasValueCall.Previous.Operand).Name != "OverrideAspect")
            throw new InvalidOperationException("BraveCameraUtility.get_ASPECT: get_HasValue is not called on OverrideAspect");
        var overrideField = hasValueCall.Previous.Operand;
        var hasValue = hasValueCall.Operand;

        var active = HelperMethod(helper, "Active");
        foreach (var (name, callee) in new[]
        {
            ("get_ASPECT", "Aspect"), ("get_H_PIXELS", "HPixels"), ("get_V_PIXELS", "VPixels"),
        })
        {
            var method = FindMethod(module, "BraveCameraUtility", name);
            ExpandShortBranches(method);
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Call, active));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ldsflda, (FieldReference)overrideField));
            il.InsertBefore(first, il.Create(OpCodes.Call, (MethodReference)hasValue));
            il.InsertBefore(first, il.Create(OpCodes.Call, HelperMethod(helper, callee, 1)));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            Console.WriteLine($"    [OK] BraveCameraUtility.{name} -> Gungeon43.{callee}(OverrideAspect.HasValue) on narrow screens");
        }
    }

    /// S3: the ui cameras (ForceToSixteenNine) build a 480x270 box; on a narrow screen that box
    /// is as wide as the game camera and 9/16 of it tall.
    static void PatchForceAspect(ModuleDefinition module, TypeDefinition helper)
    {
        var method = FindMethod(module, "BraveCameraUtility", "MaintainCameraAspectForceAspect");
        ExpandShortBranches(method);
        void Swap(Func<Instruction, bool> match, string what, string callee)
        {
            var ins = ExactlyOne(method, match, what);
            ins.OpCode = OpCodes.Call;
            ins.Operand = HelperMethod(helper, callee);
        }
        Swap(i => IsR4(i, 480f), "ldc.r4 480", "ForceH");
        Swap(i => IsR4(i, 270f), "ldc.r4 270", "ForceV");
        Swap(i => IsI4(i, 480), "ldc.i4 480", "ForceHi");
        Swap(i => IsI4(i, 270), "ldc.i4 270", "ForceVi");
        Console.WriteLine("    [OK] BraveCameraUtility.MaintainCameraAspectForceAspect -> ForceH/ForceV/ForceHi/ForceVi");
    }

    /// S4: tile scale is `Screen.height * rect.height / 270f`; the 270 is the vertical pixel
    /// budget, which is now V_PIXELS.
    static void PatchPixelator(ModuleDefinition module)
    {
        var method = FindMethod(module, "Pixelator", "CheckSize");
        ExpandShortBranches(method);
        var ins = ExactlyOne(method, i => IsR4(i, 270f), "ldc.r4 270");
        var vPixels = FindMethod(module, "BraveCameraUtility", "get_V_PIXELS");
        // rewritten in place so branches that targeted the constant land on the call
        ins.OpCode = OpCodes.Call;
        ins.Operand = vPixels;
        var il = method.Body.GetILProcessor();
        il.InsertAfter(ins, il.Create(OpCodes.Conv_R4));
        Console.WriteLine("    [OK] Pixelator.CheckSize -> 270f replaced by BraveCameraUtility.V_PIXELS");
    }

    /// S5: the resolution picker prefers 16:9; hand it the panel's own aspect instead.
    static void PatchRecommendedResolution(ModuleDefinition module, TypeDefinition helper)
    {
        var method = FindMethod(module, "GameOptions", "GetRecommendedResolution");
        ExpandShortBranches(method);
        var ins = ExactlyOne(method, i => IsR4(i, 1.7777778f), "ldc.r4 1.7777778");
        ins.OpCode = OpCodes.Call;
        ins.Operand = HelperMethod(helper, "NativeAspect");
        Console.WriteLine("    [OK] GameOptions.GetRecommendedResolution -> Gungeon43.NativeAspect()");
    }

    /// <summary>
    /// cecil writes a short branch as a single signed byte and never widens it, so
    /// inserting code in front of a branch target silently overflows the operand and
    /// sends the branch into the middle of an instruction. mono then jits garbage and
    /// the game dies with an access violation. widen every short branch in any method
    /// we edit to its long form before cecil gets a chance to write the body.
    /// </summary>
    static void ExpandShortBranches(MethodDefinition method)
    {
        if (!method.HasBody) return;
        foreach (var ins in method.Body.Instructions)
        {
            var wide = IlSig.LongBranch(ins.OpCode);
            if (wide.HasValue) ins.OpCode = wide.Value;
        }
    }

    /// <summary>
    /// re-reads what we just wrote. a branch whose target does not land on an
    /// instruction boundary comes back with a null operand, so this is the cheap way
    /// to catch a corrupt body before anyone puts it on a device.
    /// </summary>
    static void VerifyBranches(byte[] assemblyBytes)
    {
        var broken = new List<string>();
        using (var ms = new MemoryStream(assemblyBytes))
        using (var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = new NullAssemblyResolver() }))
        {
            var stack = new Stack<TypeDefinition>(asm.MainModule.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                foreach (var n in type.NestedTypes) stack.Push(n);
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.OpCode.OperandType != OperandType.ShortInlineBrTarget &&
                            ins.OpCode.OperandType != OperandType.InlineBrTarget) continue;
                        if (ins.Operand == null)
                            broken.Add($"{type.Name}::{method.Name}@{ins.Offset:x4}");
                    }
                }
            }
        }

        if (broken.Count > 0)
            throw new InvalidOperationException(
                $"patched IL is corrupt: {broken.Count} branch(es) point into the middle of an instruction ({string.Join(", ", broken.Take(8))})");

        Console.WriteLine("    [OK] every branch target in the output still resolves");
    }

    // ---- helper clone ----

    static TypeDefinition InjectHelper(ModuleDefinition targetModule)
    {
        var helperBytes = ReadHelperAssembly()
            ?? throw new FileNotFoundException("embedded GungeonHelper.dll payload missing from the patcher binary");

        // the helper is only read and cloned, never resolved, so it gets no search dirs
        var readerParams = new ReaderParameters { AssemblyResolver = new NullAssemblyResolver() };

        using var ms = new MemoryStream(helperBytes);
        using var helperAsm = AssemblyDefinition.ReadAssembly(ms, readerParams);
        var srcType = helperAsm.MainModule.Types.First(t => t.Name == Sites.HelperType);

        var newType = new TypeDefinition(srcType.Namespace, srcType.Name, srcType.Attributes, targetModule.TypeSystem.Object);
        targetModule.Types.Add(newType);

        var fieldMap = new Dictionary<string, FieldDefinition>();
        foreach (var f in srcType.Fields)
        {
            // consts are inlined by the compiler and nothing references them; cloning
            // the literal without its constant row would leave a malformed field
            if (f.IsLiteral) continue;
            var newField = new FieldDefinition(f.Name, f.Attributes, targetModule.ImportReference(f.FieldType));
            newType.Fields.Add(newField);
            fieldMap[f.Name] = newField;
        }

        // pass 1: declare every method and parameter before any body is copied.
        // a self-call (ForceH -> HPixels) can only resolve once the callee exists on the
        // clone - otherwise the mapping falls back to an import of GungeonHelper.dll
        // (which is not in the game folder) and every call throws FileNotFoundException.
        foreach (var m in srcType.Methods)
        {
            var decl = new MethodDefinition(m.Name, m.Attributes, targetModule.ImportReference(m.ReturnType));
            foreach (var p in m.Parameters)
                decl.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, targetModule.ImportReference(p.ParameterType)));
            newType.Methods.Add(decl);
        }

        // pass 2: copy the bodies, resolving self references against the clones
        for (int mi = 0; mi < srcType.Methods.Count; mi++)
        {
            var m = srcType.Methods[mi];
            var newMethod = newType.Methods[mi];
            if (!m.HasBody) continue;

            newMethod.Body.InitLocals = m.Body.InitLocals;
            newMethod.Body.MaxStackSize = m.Body.MaxStackSize;

            var varMap = new Dictionary<VariableDefinition, VariableDefinition>();
            foreach (var v in m.Body.Variables)
            {
                var newVar = new VariableDefinition(targetModule.ImportReference(v.VariableType));
                newMethod.Body.Variables.Add(newVar);
                varMap[v] = newVar;
            }

            var il = newMethod.Body.GetILProcessor();
            var insMap = new Dictionary<Instruction, Instruction>();

            foreach (var ins in m.Body.Instructions)
            {
                var dummy = Instruction.Create(OpCodes.Nop);
                insMap[ins] = dummy;
                il.Append(dummy);
            }

            for (int idx = 0; idx < m.Body.Instructions.Count; idx++)
            {
                var ins = m.Body.Instructions[idx];
                var newIns = newMethod.Body.Instructions[idx];
                newIns.OpCode = ins.OpCode;

                if (ins.Operand == null)
                    newIns.Operand = null;
                else if (ins.Operand is FieldReference fr)
                {
                    if (fr.DeclaringType != null && fr.DeclaringType.FullName == srcType.FullName
                        && fieldMap.TryGetValue(fr.Name, out var mappedField))
                        newIns.Operand = mappedField;
                    else
                        newIns.Operand = targetModule.ImportReference(fr);
                }
                else if (ins.Operand is MethodReference mr)
                    newIns.Operand = MapHelperMethod(targetModule, srcType, newType, mr);
                else if (ins.Operand is TypeReference tr)
                    newIns.Operand = tr.FullName == srcType.FullName ? (TypeReference)newType : targetModule.ImportReference(tr);
                else if (ins.Operand is VariableDefinition vd)
                    newIns.Operand = varMap[vd];
                else if (ins.Operand is ParameterDefinition pd)
                    newIns.Operand = newMethod.Parameters[pd.Index];
                else if (ins.Operand is Instruction targetIns)
                    newIns.Operand = insMap[targetIns];
                else if (ins.Operand is Instruction[] targetInss)
                    newIns.Operand = targetInss.Select(t => insMap[t]).ToArray();
                else
                    newIns.Operand = ins.Operand;
            }

            // try/catch blocks live in the exception table, not in the instruction stream.
            // copying only instructions silently strips every catch (chefrpg's helper lost
            // all of them that way). map each boundary through insMap; a null TryEnd or
            // HandlerEnd means "end of body".
            Instruction? MapIns(Instruction? i) => i == null ? null : insMap[i];
            foreach (var eh in m.Body.ExceptionHandlers)
            {
                newMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType)
                {
                    TryStart = MapIns(eh.TryStart),
                    TryEnd = MapIns(eh.TryEnd),
                    HandlerStart = MapIns(eh.HandlerStart),
                    HandlerEnd = MapIns(eh.HandlerEnd),
                    FilterStart = MapIns(eh.FilterStart),
                    CatchType = eh.CatchType == null ? null : targetModule.ImportReference(eh.CatchType),
                });
            }
        }

        Console.WriteLine($"    [OK] Gungeon43 -> injected ({newType.Methods.Count} methods)");
        return newType;
    }

    /// <summary>
    /// the helper ships inside the patcher binary as an embedded resource, so a
    /// downloaded patcher runs on a machine that has never built the helper.
    /// </summary>
    internal static byte[]? ReadHelperAssembly()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith("GungeonHelper.dll", StringComparison.Ordinal)) continue;
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) continue;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        return null;
    }

    /// <summary>
    /// maps a method operand onto the clone: calls inside the helper class resolve to
    /// the cloned type, everything else imports normally.
    /// </summary>
    static MethodReference MapHelperMethod(ModuleDefinition target, TypeDefinition src, TypeDefinition dst, MethodReference mr)
    {
        var element = (mr as GenericInstanceMethod)?.ElementMethod ?? mr;
        if (element.DeclaringType != null && element.DeclaringType.FullName == src.FullName)
        {
            // overloads share a name, so the parameter types have to match too, not just the count
            var match = dst.Methods.FirstOrDefault(m => m.Name == element.Name
                && m.Parameters.Count == element.Parameters.Count
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(
                    element.Parameters.Select(p => p.ParameterType.FullName)));
            if (match != null)
            {
                if (mr is GenericInstanceMethod gim)
                {
                    var ng = new GenericInstanceMethod(match);
                    foreach (var ga in gim.GenericArguments) ng.GenericArguments.Add(target.ImportReference(ga));
                    return ng;
                }
                return match;
            }
        }
        return target.ImportReference(mr);
    }
}
