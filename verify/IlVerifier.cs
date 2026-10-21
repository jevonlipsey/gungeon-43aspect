using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EtgVerify;

/// structural verifier for the patched assembly. it re-derives every stack height from
/// the actual instruction stream instead of trusting the writer, checks every branch lands
/// on an instruction boundary, resolves every external member through the game's own
/// Managed folder only, and rejects any reference that would drag the patcher's runtime
/// (CoreLib, netstandard, System.Runtime) into unity's mono, where it dies at JIT.
///
/// stack heights are computed for the edited methods and the cloned helper only: those are
/// the bodies this patch wrote. every other body is proven byte-for-byte unchanged by the
/// semantic verifier, so a stack walk over the whole game would only re-check the compiler.
public sealed class IlVerifier
{
    private readonly Checks checks;
    private readonly ModuleDefinition module;
    private readonly ModuleDefinition input;
    private readonly VerifyMode mode;
    private readonly HashSet<Instruction> instructionSet = new();
    private readonly List<string> bodyErrors = new();

    public long InstructionCount { get; private set; }
    public long MethodCount { get; private set; }
    public long StackCheckedCount { get; private set; }
    public int MaxComputedStack { get; private set; }
    public int MaxDeclaredStack { get; private set; }

    public IlVerifier(Checks checks, ModuleDefinition module, ModuleDefinition input, VerifyMode mode = VerifyMode.Full)
    {
        this.checks = checks;
        this.module = module;
        this.input = input;
        this.mode = mode;
    }

    private static readonly string[] BannedRefs =
    {
        "System.Private.CoreLib", "netstandard", "System.Runtime", "System.Collections",
        "System.Linq", "System.Reflection", "GungeonHelper",
    };

    internal static bool IsForbiddenScope(string? scope) =>
        scope != null && BannedRefs.Any(b => scope.StartsWith(b, StringComparison.Ordinal));

    public void Run()
    {
        checks.Section("assembly structure");

        var refs = module.AssemblyReferences.Select(r => r.Name).ToList();
        foreach (var banned in BannedRefs)
            checks.Check(!refs.Any(r => r.StartsWith(banned, StringComparison.Ordinal)),
                $"no {banned} assembly reference");
        checks.Check(refs.Contains("mscorlib"), "unity-compatible core library present (mscorlib)");

        // the output may only reference what the game already referenced: a new row is a
        // dll the device has to find at load time
        var want = new SortedSet<string>(input.AssemblyReferences.Select(r => $"{r.Name} {r.Version}"));
        var got = new SortedSet<string>(module.AssemblyReferences.Select(r => $"{r.Name} {r.Version}"));
        checks.Check(want.SetEquals(got),
            want.SetEquals(got)
                ? $"assembly references identical to the input ({got.Count})"
                : $"assembly references differ: added [{string.Join(", ", got.Except(want))}] removed [{string.Join(", ", want.Except(got))}]");

        foreach (var type in module.Types)
            WalkType(type);

        checks.Check(bodyErrors.Count == 0,
            bodyErrors.Count == 0
                ? $"{MethodCount} methods / {InstructionCount} instructions decode; branch, handler and local checks clean; stack balanced in {StackCheckedCount} edited/helper methods"
                : $"{bodyErrors.Count} body error(s): {string.Join(" | ", bodyErrors.Take(8))}");
        checks.Note($"max computed stack {MaxComputedStack}, max declared {MaxDeclaredStack} (checked methods)");

        if (mode == VerifyMode.Full) ResolveExternals();
        else
        {
            checks.Section("external references");
            checks.Skip("external members and types resolve through --refs only: skipped (reference-free mode, needs the game's Unity assemblies)");
        }
    }

    // true when the type lives in another assembly. specs (arrays, generic instances,
    // by-refs) take the scope of their element type.
    private static bool IsExternal(TypeReference type)
    {
        while (type is TypeSpecification spec) type = spec.ElementType;
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type.Scope is AssemblyNameReference;
    }

    // every member reference whose scope is another assembly has to resolve against the
    // game's Managed folder alone. one that already failed to resolve in the input (a
    // platform dll the dev folder lacks) is reported but not blamed on the patch.
    private void ResolveExternals()
    {
        checks.Section("external references");
        var preexisting = new HashSet<string>(input.GetMemberReferences().Select(m => m.FullName));
        int resolved = 0, inherited = 0;
        var errors = new List<string>();
        foreach (var mr in module.GetMemberReferences())
        {
            if (!IsExternal(mr.DeclaringType)) continue;
            string? problem = null;
            try
            {
                switch (mr)
                {
                    case MethodReference m:
                        var def = m.Resolve();
                        if (def == null)
                        {
                            if (!(m.DeclaringType is ArrayType at && IsWellFormedArrayMember(at, m)))
                                problem = $"cannot resolve {m.FullName}";
                        }
                        else if ((m.CallingConvention & MethodCallingConvention.VarArg) == 0 &&
                                 m.Parameters.Count != def.Parameters.Count)
                            problem = $"arity mismatch {m.FullName}";
                        break;
                    case FieldReference f:
                        if (f.Resolve() == null) problem = $"cannot resolve field {f.FullName}";
                        break;
                }
            }
            catch (Exception ex)
            {
                problem = $"resolve {mr.FullName} threw {ex.GetType().Name}";
            }
            if (problem == null) resolved++;
            else if (preexisting.Contains(mr.FullName)) inherited++;
            else errors.Add(problem);
        }
        int types = 0;
        foreach (var tr in module.GetTypeReferences())
        {
            if (!IsExternal(tr) || tr.IsGenericParameter) continue;
            string? problem = null;
            try { if (tr.Resolve() == null) problem = $"cannot resolve type {tr.FullName}"; }
            catch (Exception ex) { problem = $"resolve type {tr.FullName} threw {ex.GetType().Name}"; }
            if (problem == null) types++;
            else if (input.GetTypeReferences().Any(t => t.FullName == tr.FullName)) inherited++;
            else errors.Add(problem);
        }
        checks.Check(errors.Count == 0,
            errors.Count == 0
                ? $"{resolved} external members and {types} external types resolve through --refs only"
                : $"{errors.Count} external reference(s) do not resolve: {string.Join(" | ", errors.Take(8))}");
        if (inherited > 0)
            checks.Note($"{inherited} external reference(s) also fail to resolve in the input (not caused by the patch)");
    }

    private void WalkType(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes) WalkType(nested);
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            MethodCount++;
            ValidateBody(type, method);
        }
    }

    private void ValidateBody(TypeDefinition type, MethodDefinition method)
    {
        var body = method.Body;
        var label = $"{type.FullName}::{method.Name}";
        InstructionCount += body.Instructions.Count;

        if (body.Instructions.Count == 0)
        {
            bodyErrors.Add($"{label}: empty body");
            return;
        }
        if (method.IsAbstract || method.IsPInvokeImpl) return;

        instructionSet.Clear();
        foreach (var ins in body.Instructions) instructionSet.Add(ins);

        ValidateVariables(label, method);
        ValidateHandlers(label, method);
        ValidateBranches(label, method);
        ValidateMembers(label, method);
        if (type.Name == Sites.HelperType || Sites.IsEdited(type.Name, method.Name))
        {
            StackCheckedCount++;
            ComputeStack(label, method);
        }
    }

    // an array of rank n carries .ctor/Get/Address with n int32 args and Set with the
    // n indices plus the element value, all generated by the runtime; they only need
    // the element type to be resolvable
    private static bool IsWellFormedArrayMember(ArrayType at, MethodReference element)
    {
        if (at.Rank < 1) return false;
        if (element.Name is not (".ctor" or "Get" or "Set" or "Address")) return false;
        int expected = element.Name == "Set" ? at.Rank + 1 : at.Rank;
        if (element.Parameters.Count != expected) return false;
        for (int i = 0; i < at.Rank; i++)
            if (element.Parameters[i].ParameterType.MetadataType != MetadataType.Int32) return false;
        if (element.Name == "Set" && element.Parameters[at.Rank].ParameterType.FullName != at.ElementType.FullName) return false;
        // reference-free: an external element type cannot be resolved, the shape is all we have
        if (IsExternal(at.ElementType)) return true;
        try { return at.ElementType.Resolve() != null; }
        catch { return false; }
    }

    private void ValidateVariables(string label, MethodDefinition method)
    {
        var body = method.Body;
        foreach (var ins in body.Instructions)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    if (ins.Operand is VariableDefinition v && v.Index >= body.Variables.Count)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: local index {v.Index} outside {body.Variables.Count}");
                    break;
                case Code.Stloc:
                case Code.Stloc_S:
                    if (ins.Operand is VariableDefinition s && s.Index >= body.Variables.Count)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: local index {s.Index} outside {body.Variables.Count}");
                    break;
                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarga:
                case Code.Ldarga_S:
                case Code.Starg:
                case Code.Starg_S:
                    if (ins.Operand is ParameterDefinition p && p.Index >= method.Parameters.Count)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: arg index {p.Index} outside {method.Parameters.Count}");
                    break;
            }
        }
    }

    // a `leave` only exists to exit a protected region. a body with leaves and an empty
    // exception table is a try/catch whose handlers were dropped on the way in - exactly
    // how InjectSafeAreaHelper once stripped every catch from the helper (it copied
    // instructions but not Body.ExceptionHandlers). the handler checks below only look at
    // handlers that exist, so this is the only thing that can notice them missing.
    private void ValidateHandlers(string label, MethodDefinition method)
    {
        var body = method.Body;
        if (body.ExceptionHandlers.Count > 0) return;
        var leave = body.Instructions.FirstOrDefault(i => i.OpCode.Code is Code.Leave or Code.Leave_S);
        if (leave != null)
            bodyErrors.Add($"{label}@{leave.Offset:x4}: {leave.OpCode} in a body with no exception handlers (try/catch stripped?)");
    }

    private void ValidateBranches(string label, MethodDefinition method)
    {
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            if (handler.TryStart != null && !instructionSet.Contains(handler.TryStart))
                bodyErrors.Add($"{label}: handler try start is not an instruction");
            if (handler.HandlerStart != null && !instructionSet.Contains(handler.HandlerStart))
                bodyErrors.Add($"{label}: handler start is not an instruction");
            if (handler.FilterStart != null && !instructionSet.Contains(handler.FilterStart))
                bodyErrors.Add($"{label}: filter start is not an instruction");
            if (handler.TryEnd != null && !instructionSet.Contains(handler.TryEnd))
                bodyErrors.Add($"{label}: handler try end is not an instruction");
            if (handler.HandlerEnd != null && !instructionSet.Contains(handler.HandlerEnd))
                bodyErrors.Add($"{label}: handler end is not an instruction");
        }

        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is Instruction target)
            {
                if (!instructionSet.Contains(target))
                    bodyErrors.Add($"{label}@{ins.Offset:x4}: {ins.OpCode} target is not an instruction");
                else if (IsShortBranch(ins.OpCode))
                {
                    long distance = (long)target.Offset - (ins.Offset + ins.GetSize());
                    if (distance < sbyte.MinValue || distance > sbyte.MaxValue)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: short branch distance {distance} overflows");
                }
            }
            else if (ins.Operand is Instruction[] targets)
            {
                foreach (var t in targets)
                    if (!instructionSet.Contains(t))
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: switch target is not an instruction");
            }
        }
    }

    // scope bans per instruction; resolving happens once per member reference in ResolveExternals
    private void ValidateMembers(string label, MethodDefinition method)
    {
        foreach (var ins in method.Body.Instructions)
        {
            TypeReference? decl = ins.Operand switch
            {
                MethodReference mr => ((mr as GenericInstanceMethod)?.ElementMethod ?? mr).DeclaringType,
                FieldReference fr => fr.DeclaringType,
                TypeReference tr => tr is GenericInstanceType git ? git.ElementType : tr,
                _ => null,
            };
            if (decl == null || decl.IsGenericParameter) continue;
            var scope = ScopeName(decl);
            if (IsForbiddenScope(scope))
                bodyErrors.Add($"{label}@{ins.Offset:x4}: forbidden scope {scope} for {ins.Operand}");
        }
    }

    private void ComputeStack(string label, MethodDefinition method)
    {
        var body = method.Body;
        var heights = new Dictionary<Instruction, int>();
        var queue = new Queue<Instruction>();
        var maxStack = 0;

        void Seed(Instruction? ins, int height)
        {
            if (ins == null || !instructionSet.Contains(ins)) return;
            if (heights.TryGetValue(ins, out var existing))
            {
                if (existing != height)
                    bodyErrors.Add($"{label}@{ins.Offset:x4}: stack merge {existing} vs {height}");
                return;
            }
            heights[ins] = height;
            queue.Enqueue(ins);
        }

        Seed(body.Instructions[0], 0);
        foreach (var h in body.ExceptionHandlers)
        {
            if (h.HandlerType == ExceptionHandlerType.Finally || h.HandlerType == ExceptionHandlerType.Fault)
                Seed(h.HandlerStart, 0);
            else
                Seed(h.HandlerStart, 1);
            if (h.FilterStart != null) Seed(h.FilterStart, 1);
        }

        while (queue.Count > 0)
        {
            var ins = queue.Dequeue();
            int height = heights[ins];
            var (pop, push) = StackEffect(ins, height);
            if (pop < 0)
            {
                bodyErrors.Add($"{label}@{ins.Offset:x4}: unsupported stack behaviour {ins.OpCode.StackBehaviourPop}");
                continue;
            }
            if (height < pop)
            {
                bodyErrors.Add($"{label}@{ins.Offset:x4}: {ins.OpCode} needs {pop} stack slot(s), has {height}");
                continue;
            }

            int after = height - pop + push;
            maxStack = Math.Max(maxStack, Math.Max(height, after));

            switch (ins.OpCode.Code)
            {
                case Code.Ret:
                {
                    int expected = method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                    if (height != expected)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: ret with {height} stack slot(s), expected {expected}");
                    break;
                }
                case Code.Endfilter:
                    if (height != 1)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: endfilter with {height} stack slot(s), expected 1");
                    break;
                case Code.Endfinally:
                    if (height != 0)
                        bodyErrors.Add($"{label}@{ins.Offset:x4}: endfinally with {height} stack slot(s), expected 0");
                    break;
            }

            foreach (var next in Successors(ins))
                Seed(next, next == ins.Operand && ins.OpCode.Code is Code.Leave or Code.Leave_S ? 0 : after);
        }

        if (maxStack > body.MaxStackSize)
            bodyErrors.Add($"{label}: computed max stack {maxStack} exceeds declared {body.MaxStackSize}");

        MaxComputedStack = Math.Max(MaxComputedStack, maxStack);
        MaxDeclaredStack = Math.Max(MaxDeclaredStack, body.MaxStackSize);
    }

    private static IEnumerable<Instruction> Successors(Instruction ins)
    {
        var code = ins.OpCode.Code;
        switch (code)
        {
            case Code.Ret:
            case Code.Throw:
            case Code.Rethrow:
            case Code.Endfinally:
            case Code.Endfilter:
            case Code.Jmp:
                yield break;
        }

        bool unconditional = code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S;
        bool conditional = ins.OpCode.FlowControl == FlowControl.Cond_Branch && code != Code.Switch;

        if (code == Code.Switch)
        {
            if (ins.Operand is Instruction[] targets)
                foreach (var t in targets) yield return t;
            if (ins.Next != null) yield return ins.Next;
            yield break;
        }

        if (unconditional)
        {
            if (ins.Operand is Instruction target) yield return target;
            yield break;
        }

        if (conditional && ins.Operand is Instruction ctarget)
            yield return ctarget;

        if (ins.Next != null) yield return ins.Next;
    }

    private static (int pop, int push) StackEffect(Instruction ins, int currentHeight)
    {
        var code = ins.OpCode.Code;
        switch (code)
        {
            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
            {
                if (ins.Operand is not MethodReference mr) return (-1, 0);
                int args = mr.Parameters.Count;
                int pop = args + (code == Code.Newobj ? 0 : mr.HasThis ? 1 : 0);
                int push = code == Code.Newobj || mr.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
                return (pop, push);
            }
            case Code.Calli:
            {
                if (ins.Operand is not CallSite site) return (-1, 0);
                int pop = site.Parameters.Count + (site.HasThis ? 1 : 0) + 1;
                int push = site.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                return (pop, push);
            }
            case Code.Ret:
                return (0, 0);
            case Code.Endfilter:
                return (1, 0);
            case Code.Pop:
                return (1, 0);
            case Code.Dup:
                return (1, 2);
        }

        int popCount = PopCount(ins.OpCode.StackBehaviourPop);
        if (popCount == int.MaxValue) return (-1, 0);
        if (ins.OpCode.StackBehaviourPop == StackBehaviour.PopAll) popCount = currentHeight;
        int pushCount = PushCount(ins.OpCode.StackBehaviourPush);
        if (pushCount == int.MaxValue) return (-1, 0);
        return (popCount, pushCount);
    }

    private static int PopCount(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 => 1,
        StackBehaviour.Pop1_pop1 => 2,
        StackBehaviour.Popi => 1,
        StackBehaviour.Popi_pop1 => 2,
        StackBehaviour.Popi_popi => 2,
        StackBehaviour.Popi_popi8 => 2,
        StackBehaviour.Popi_popi_popi => 3,
        StackBehaviour.Popi_popr4 => 2,
        StackBehaviour.Popi_popr8 => 2,
        StackBehaviour.Popref => 1,
        StackBehaviour.Popref_pop1 => 2,
        StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popref_popi_popi => 3,
        StackBehaviour.Popref_popi_popi8 => 3,
        StackBehaviour.Popref_popi_popr4 => 3,
        StackBehaviour.Popref_popi_popr8 => 3,
        StackBehaviour.Popref_popi_popref => 3,
        StackBehaviour.PopAll => int.MaxValue - 1,
        StackBehaviour.Varpop => int.MaxValue,
        _ => int.MaxValue,
    };

    private static int PushCount(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1 => 1,
        StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Pushi => 1,
        StackBehaviour.Pushi8 => 1,
        StackBehaviour.Pushr4 => 1,
        StackBehaviour.Pushr8 => 1,
        StackBehaviour.Pushref => 1,
        _ => int.MaxValue,
    };

    private static bool IsShortBranch(OpCode op) => op.OperandType == OperandType.ShortInlineBrTarget;

    internal static string? ScopeName(TypeReference type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type.Scope switch
        {
            AssemblyNameReference a => a.Name,
            ModuleDefinition m => m.Name,
            ModuleReference mr => mr.Name,
            _ => null,
        };
    }
}
