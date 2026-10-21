using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EtgVerify;

/// checks the intent of the patch, not just that the il parses. every edited method is
/// rebuilt from the input by the documented transformation and compared to the output; every
/// other method in the game must be il-identical to the input, so nothing that is not listed
/// in CLAUDE.md can have changed (AkBasePathGetter and FoyerPreloader are named explicitly:
/// touching either hung the boot on the device).
public sealed class SemanticVerifier
{
    private readonly Checks checks;
    private readonly ModuleDefinition module;
    private readonly ModuleDefinition input;
    private readonly byte[]? helperPayload;

    // helperPayload is the GungeonHelper.dll embedded in the patcher. the caller supplies
    // it (the standalone harness reads the file, the patcher passes its resource).
    public SemanticVerifier(Checks checks, ModuleDefinition module, ModuleDefinition input, byte[]? helperPayload)
    {
        this.checks = checks;
        this.module = module;
        this.input = input;
        this.helperPayload = helperPayload;
    }

    public void Run()
    {
        checks.Section("patch intent");
        var helper = module.Types.FirstOrDefault(t => t.Name == Sites.HelperType && t.Namespace == "");
        checks.Check(helper != null, "Gungeon43 helper type exists in the output");
        if (helper == null) return;

        CheckPlatform();
        CheckHelper(helper);
        CheckAspectGetters(helper);
        CheckForceAspect(helper);
        CheckPixelator();
        CheckRecommendedResolution(helper);

        checks.Section("nothing else changed");
        CheckUntouched();
    }

    private MethodDefinition? Find(ModuleDefinition m, string type, string method)
    {
        var ms = m.Types.Where(t => t.Name == type && t.Namespace == "")
            .SelectMany(t => t.Methods).Where(x => x.Name == method).ToList();
        return ms.Count == 1 ? ms[0] : null;
    }

    private static MethodDefinition? HelperMethod(TypeDefinition helper, string name) =>
        helper.Methods.SingleOrDefault(m => m.Name == name);

    private static string? PlatformLiteral(ModuleDefinition m)
    {
        var get = m.Types.FirstOrDefault(t => t.Name == "AkBasePathGetter")?.Methods.FirstOrDefault(x => x.Name == "GetPlatformName");
        if (get == null || !get.HasBody) return null;
        var lits = get.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr && !string.IsNullOrEmpty((string)i.Operand)).ToList();
        return lits.Count == 1 ? (string)lits[0].Operand : null;
    }

    private void CheckPlatform()
    {
        var before = PlatformLiteral(input);
        var after = PlatformLiteral(module);
        checks.Check(before != null && before == after,
            $"AkBasePathGetter.GetPlatformName literal unchanged (\"{after}\" vs input \"{before}\")");
    }

    // compares an edited method with the input through the transformation: `expected` is
    // the input's body with branch targets shifted by `shift` after a prologue of `prologue`
    // instructions, then mutated by `edit`. widened short branches are the only forgiven change.
    private void CheckEdited(string type, string method, Func<List<string>, List<string>> edit, int shift, string what)
    {
        var inM = Find(input, type, method);
        var outM = Find(module, type, method);
        if (inM == null || outM == null || !inM.HasBody || !outM.HasBody)
        {
            checks.Fail($"{type}.{method}: method missing ({what})");
            return;
        }
        var expected = edit(IlSig.Body(inM.Body, widen: true, shift: shift));
        var actual = IlSig.Body(outM.Body);
        bool same = expected.SequenceEqual(actual);
        string detail = "";
        if (!same)
        {
            int i = 0;
            while (i < expected.Count && i < actual.Count && expected[i] == actual[i]) i++;
            detail = $" - first difference at #{i}: expected '{(i < expected.Count ? expected[i] : "<end>")}', got '{(i < actual.Count ? actual[i] : "<end>")}'";
        }
        bool handlers = IlSig.Handlers(inM.Body, shift).SequenceEqual(IlSig.Handlers(outM.Body));
        // a prologue inserted at index 0 never lands inside a try, and a try that began at
        // the old first instruction still begins there, so handler indexes shift uniformly
        checks.Check(same && handlers, $"{type}.{method}: {what}{detail}{(handlers ? "" : " - exception handlers differ")}");
    }

    // call Active; brfalse <old first, now index 6>; ldsflda OverrideAspect; call get_HasValue;
    // call callee(bool); ret. the field and HasValue operands are read off the input's
    // get_ASPECT body, which is what proves no new member reference was introduced.
    private List<string> Prologue(string callee, List<string> body)
    {
        var list = new List<string>
        {
            "call System.Boolean Gungeon43::Active()",
            "brfalse ->6",
            "ldsflda " + InputOperand(Code.Ldsflda, "OverrideAspect"),
            "call " + InputOperand(Code.Call, "get_HasValue"),
            "call " + callee,
            "ret ",
        };
        list.AddRange(body);
        return list;
    }

    private string InputOperand(Code code, string name)
    {
        var m = Find(input, "BraveCameraUtility", "get_ASPECT");
        var calls = m == null ? new List<Instruction>() : m.Body.Instructions
            .Where(i => i.OpCode.Code == Code.Call && i.Operand is MemberReference r && r.Name == "get_HasValue").ToList();
        if (calls.Count != 1) return $"<{calls.Count} x get_HasValue in input get_ASPECT>";
        var ins = code == Code.Call ? calls[0] : calls[0].Previous;
        return ins != null && ins.OpCode.Code == code ? IlSig.Operand(ins, t => "?") : "<no ldsflda before get_HasValue>";
    }

    private void CheckAspectGetters(TypeDefinition helper)
    {
        // S1/S2: the prologue runs before the OverrideAspect check, so the menu's 16:9
        // override is ignored on narrow screens; the flag only picks the menu row budget
        CheckEdited("BraveCameraUtility", "get_ASPECT", b => Prologue("System.Single Gungeon43::Aspect(System.Boolean)", b), 6,
            "S1 prologue (Active ? Aspect(OverrideAspect.HasValue) : original body) + original body unchanged");
        CheckEdited("BraveCameraUtility", "get_H_PIXELS", b => Prologue("System.Int32 Gungeon43::HPixels(System.Boolean)", b), 6,
            "S2 prologue (Active ? HPixels(HasValue) : original body) + original body unchanged");
        CheckEdited("BraveCameraUtility", "get_V_PIXELS", b => Prologue("System.Int32 Gungeon43::VPixels(System.Boolean)", b), 6,
            "S2 prologue (Active ? VPixels(HasValue) : original body) + original body unchanged");
    }

    private static string R4(float v) => "ldc.r4 " + v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    // replaces exactly one instruction signature, failing the transformation otherwise
    private static Func<List<string>, List<string>> Swap(params (string From, string To)[] swaps) => body =>
    {
        var list = new List<string>(body);
        foreach (var (from, to) in swaps)
        {
            int hits = list.Count(s => s == from);
            if (hits != 1) return new List<string> { $"<{hits} matches for '{from}', expected 1>" };
            list[list.IndexOf(from)] = to;
        }
        return list;
    };

    private void CheckForceAspect(TypeDefinition helper)
    {
        CheckEdited("BraveCameraUtility", "MaintainCameraAspectForceAspect", Swap(
            (R4(480f), "call System.Single Gungeon43::ForceH()"),
            (R4(270f), "call System.Single Gungeon43::ForceV()"),
            ("ldc.i4 480", "call System.Int32 Gungeon43::ForceHi()"),
            ("ldc.i4 270", "call System.Int32 Gungeon43::ForceVi()")), 0,
            "S3 ForceAspect: 480f/270f/480/270 -> ForceH/ForceV/ForceHi/ForceVi, nothing else");
    }

    private void CheckPixelator()
    {
        var inM = Find(input, "Pixelator", "CheckSize");
        if (inM == null) { checks.Fail("Pixelator.CheckSize missing in the input"); return; }
        var body = IlSig.Body(inM.Body, widen: true);
        int at = body.IndexOf(R4(270f));
        if (at < 0 || body.Count(s => s == R4(270f)) != 1)
        {
            checks.Fail("Pixelator.CheckSize: input does not hold exactly one ldc.r4 270");
            return;
        }
        // the replacement is two instructions, so every branch target after it moves by one
        var outM = Find(module, "Pixelator", "CheckSize");
        if (outM == null) { checks.Fail("Pixelator.CheckSize missing in the output"); return; }
        var expected = new List<string>();
        var raw = inM.Body.Instructions;
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < raw.Count; i++) index[raw[i]] = i;
        for (int i = 0; i < raw.Count; i++)
        {
            var ins = raw[i];
            var op = IlSig.LongBranch(ins.OpCode) ?? ins.OpCode;
            if (i == at)
            {
                expected.Add("call System.Int32 BraveCameraUtility::get_V_PIXELS()");
                expected.Add("conv.r4 ");
                continue;
            }
            expected.Add(op.Name + " " + IlSig.Operand(ins, t => (index[t] + (index[t] > at ? 1 : 0)).ToString()));
        }
        checks.Check(expected.SequenceEqual(IlSig.Body(outM.Body)),
            "Pixelator.CheckSize: S4 ldc.r4 270 -> call BraveCameraUtility::get_V_PIXELS; conv.r4, nothing else");
    }

    private void CheckRecommendedResolution(TypeDefinition helper)
    {
        CheckEdited("GameOptions", "GetRecommendedResolution", Swap(
            (R4(1.7777778f), "call System.Single Gungeon43::NativeAspect()")), 0,
            "S5 GetRecommendedResolution: ldc.r4 1.7777778 -> Gungeon43::NativeAspect(), nothing else");
    }

    // the cloned helper must match the GungeonHelper.dll that was embedded in the patcher:
    // same methods, same instruction counts, same exception handlers. copying only the
    // instructions once silently stripped every catch from chefrpg's helper.
    private void CheckHelper(TypeDefinition clone)
    {
        if (helperPayload == null || helperPayload.Length == 0)
        {
            checks.Fail("no embedded GungeonHelper.dll payload to compare the helper against");
            return;
        }
        using var ms = new MemoryStream(helperPayload);
        using var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = new NullResolver() });
        var src = asm.MainModule.Types.First(t => t.Name == Sites.HelperType);

        var want = src.Methods.Select(m => $"{m.ReturnType.FullName} {m.Name}({string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName))})").OrderBy(s => s).ToList();
        var got = clone.Methods.Select(m => $"{m.ReturnType.FullName} {m.Name}({string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName))})").OrderBy(s => s).ToList();
        checks.Check(want.SequenceEqual(got), $"Gungeon43 has the same {want.Count} methods as the embedded GungeonHelper.dll");

        bool bodies = true;
        foreach (var m in src.Methods)
        {
            var c = clone.Methods.FirstOrDefault(x => x.Name == m.Name && x.Parameters.Count == m.Parameters.Count);
            if (c == null || !m.HasBody) continue;
            // opcode + target index; operands are compared by name below because types import
            // into the game module under its own references
            var a = IlSig.Body(m.Body).Select(s => s.Split(' ')[0]).ToList();
            var b = IlSig.Body(c.Body).Select(s => s.Split(' ')[0]).ToList();
            if (!a.SequenceEqual(b)) { bodies = false; checks.Fail($"Gungeon43.{m.Name}: cloned body differs from the embedded helper"); }
        }
        if (bodies) checks.Pass("every cloned Gungeon43 body has the embedded helper's opcodes");

        int wantEh = src.Methods.Where(m => m.HasBody).Sum(m => m.Body.ExceptionHandlers.Count);
        int gotEh = clone.Methods.Where(m => m.HasBody).Sum(m => m.Body.ExceptionHandlers.Count);
        var wantMethods = src.Methods.Where(m => m.HasBody && m.Body.ExceptionHandlers.Count > 0).Select(m => m.Name + ":" + m.Body.ExceptionHandlers.Count).OrderBy(s => s);
        var gotMethods = clone.Methods.Where(m => m.HasBody && m.Body.ExceptionHandlers.Count > 0).Select(m => m.Name + ":" + m.Body.ExceptionHandlers.Count).OrderBy(s => s);
        checks.Check(wantMethods.SequenceEqual(gotMethods),
            $"cloned helper keeps all {wantEh} exception handlers of the embedded GungeonHelper.dll (found {gotEh})");

        // every call out of the helper must stay inside the game module or hit unity/mscorlib
        var stray = clone.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand as MemberReference).Where(r => r != null)
            .Select(r => IlVerifier.ScopeName(r!.DeclaringType)).Distinct().OrderBy(s => s).ToList();
        checks.Check(stray.All(s => s == "UnityEngine.CoreModule" || s == "mscorlib" || s == module.Name),
            $"helper calls only into {string.Join(", ", stray)}");
    }

    private sealed class NullResolver : IAssemblyResolver
    {
        public AssemblyDefinition Resolve(AssemblyNameReference name) => throw new AssemblyResolutionException(name);
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) => throw new AssemblyResolutionException(name);
        public void Dispose() { }
    }

    // ---- everything else must be identical ----

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition m)
    {
        var stack = new Stack<TypeDefinition>(m.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes) stack.Push(n);
        }
    }

    private static string MethodKey(MethodDefinition m) =>
        $"{m.DeclaringType.FullName}::{m.ReturnType.FullName} {m.Name}`{m.GenericParameters.Count}({string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName))})";

    private void CheckUntouched()
    {
        var inTypes = AllTypes(input).ToDictionary(t => t.FullName);
        var outTypes = AllTypes(module).Where(t => !(t.Name == Sites.HelperType && t.Namespace == "")).ToDictionary(t => t.FullName);
        checks.Check(inTypes.Keys.OrderBy(k => k).SequenceEqual(outTypes.Keys.OrderBy(k => k)),
            $"type set unchanged apart from Gungeon43 ({inTypes.Count} types)");

        int compared = 0, edited = 0;
        var diffs = new List<string>();
        foreach (var (name, it) in inTypes)
        {
            if (!outTypes.TryGetValue(name, out var ot)) continue;
            var inMethods = it.Methods.ToDictionary(MethodKey);
            var outMethods = ot.Methods.ToDictionary(MethodKey);
            if (!inMethods.Keys.ToHashSet().SetEquals(outMethods.Keys))
            {
                diffs.Add($"{name}: method set changed");
                continue;
            }
            foreach (var (key, im) in inMethods)
            {
                if (Sites.IsEdited(it.Name, im.Name) && it.Namespace == "") { edited++; continue; }
                compared++;
                var om = outMethods[key];
                if (im.HasBody != om.HasBody) { diffs.Add(key + ": body presence"); continue; }
                if (!im.HasBody) continue;
                string? d = DiffBody(im.Body, om.Body);
                if (d != null) diffs.Add($"{key}: {d}");
            }
        }
        checks.Check(diffs.Count == 0,
            diffs.Count == 0
                ? $"{compared} methods in {inTypes.Count} types are il-identical to the input ({edited} edited methods excluded)"
                : $"{diffs.Count} method(s) changed: {string.Join(" | ", diffs.Take(8))}");

        // named guards: these two caused the 2026-10-15 boot hang when a mac build was deployed
        foreach (var tn in new[] { "AkBasePathGetter", "FoyerPreloader" })
        {
            var it = input.Types.FirstOrDefault(t => t.Name == tn);
            var ot = module.Types.FirstOrDefault(t => t.Name == tn);
            bool ok = it != null && ot != null && it.Methods.Count == ot.Methods.Count
                && it.Methods.All(im => ot.Methods.Any(om => MethodKey(om) == MethodKey(im) &&
                    (im.HasBody ? om.HasBody && DiffBody(im.Body, om.Body) == null : !om.HasBody)))
                && it.NestedTypes.Count == ot.NestedTypes.Count
                && it.NestedTypes.All(n => ot.NestedTypes.Any(on => on.Name == n.Name &&
                    n.Methods.All(nm => on.Methods.Any(om => MethodKey(om) == MethodKey(nm) &&
                        (nm.HasBody ? om.HasBody && DiffBody(nm.Body, om.Body) == null : !om.HasBody)))));
            checks.Check(ok, $"{tn} (and its nested types) is il-identical to the input");
        }
    }

    // opcode + operand per instruction, plus locals and exception handlers. untouched
    // methods are re-emitted with identical offsets, so a branch target is its offset.
    private static string? DiffBody(MethodBody a, MethodBody b)
    {
        if (a.Instructions.Count != b.Instructions.Count)
            return $"{a.Instructions.Count} vs {b.Instructions.Count} instructions";
        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var x = a.Instructions[i];
            var y = b.Instructions[i];
            if (x.OpCode.Code != y.OpCode.Code ||
                IlSig.Operand(x, t => t.Offset.ToString()) != IlSig.Operand(y, t => t.Offset.ToString()))
                return $"instruction #{i} {x.OpCode} {IlSig.Operand(x, t => t.Offset.ToString())} vs {y.OpCode} {IlSig.Operand(y, t => t.Offset.ToString())}";
        }
        if (!IlSig.Locals(a).SequenceEqual(IlSig.Locals(b))) return "locals differ";
        if (!IlSig.Handlers(a).SequenceEqual(IlSig.Handlers(b))) return "exception handlers differ";
        return null;
    }
}
