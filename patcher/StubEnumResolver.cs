using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace EtgVerify;

/// lets cecil write the game's assembly without any unity dll on disk. the writer has to resolve
/// a unity enum to encode a parameter's (or field's) enum-typed default constant, but all it
/// needs from the enum is that it is one and which primitive it wraps. Prime reads that off
/// the constants already in the input (the boxed value's runtime type is the underlying type)
/// and Resolve hands back a synthesized assembly holding just those enums. everything else
/// throws: a resolve this does not expect must fail loudly, never bind to the host framework.
public sealed class StubEnumResolver : IAssemblyResolver
{
    // one recorded enum: where it lives and what it wraps, nesting kept as the declaring chain
    private sealed record EnumInfo(string Assembly, Version Version, string[] Chain, string Namespace, string Name, string Underlying);

    private readonly List<EnumInfo> enums = new();
    private readonly Dictionary<string, AssemblyDefinition> built = new();

    /// scans every parameter, field and property that carries a constant whose type is a value
    /// type from another assembly (an enum) and records it. call after the module is read and
    /// before it is written.
    public void Prime(ModuleDefinition module)
    {
        var seen = new Dictionary<string, EnumInfo>();
        void Visit(TypeReference type, object? constant)
        {
            if (constant == null || type.MetadataType != MetadataType.ValueType) return;
            var outer = type;
            var chain = new List<string>();
            while (outer.DeclaringType != null) { chain.Insert(0, outer.Name); outer = outer.DeclaringType; }
            // defined in this module (a TypeDefinition, or a reference scoped to it): cecil resolves it itself
            if (outer is TypeDefinition || outer.Scope is not AssemblyNameReference asm) return;

            var underlying = UnderlyingName(constant);
            var key = $"{asm.Name}|{type.FullName}";
            if (seen.TryGetValue(key, out var prev))
            {
                if (prev.Underlying != underlying)
                    throw new InvalidOperationException($"enum {type.FullName} has constants of both {prev.Underlying} and {underlying}");
                return;
            }
            seen[key] = new EnumInfo(asm.Name, asm.Version, chain.ToArray(), outer.Namespace, outer.Name, underlying);
            enums.Add(seen[key]);
        }

        foreach (var t in AllTypes(module))
        {
            foreach (var f in t.Fields)
                if (f.HasConstant) Visit(f.FieldType, f.Constant);
            foreach (var p in t.Properties)
                if (p.HasConstant) Visit(p.PropertyType, p.Constant);
            foreach (var m in t.Methods)
                foreach (var p in m.Parameters)
                    if (p.HasConstant) Visit(p.ParameterType, p.Constant);
        }
        built.Clear();
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        if (built.TryGetValue(name.Name, out var done)) return done;
        var mine = enums.Where(e => e.Assembly == name.Name).ToList();
        if (mine.Count == 0) throw new AssemblyResolutionException(name);

        var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name.Name, name.Version), name.Name, ModuleKind.Dll);
        var module = asm.MainModule;
        var enumBase = new TypeReference("System", "Enum", module, new AssemblyNameReference("mscorlib", new Version(2, 0, 0, 0)));
        foreach (var e in mine.OrderBy(e => e.Chain.Length))
        {
            var type = new TypeDefinition(e.Chain.Length == 0 ? e.Namespace : "", e.Chain.Length == 0 ? e.Name : e.Chain[^1],
                TypeAttributes.Sealed | (e.Chain.Length == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic), enumBase);
            type.Fields.Add(new FieldDefinition("value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, Primitive(module, e.Underlying)));
            if (e.Chain.Length == 0) module.Types.Add(type);
            else
            {
                // the declaring chain is rebuilt as plain stubs; only the leaf needs value__
                var holder = Outer(module, e);
                holder.NestedTypes.Add(type);
            }
        }
        built[name.Name] = asm;
        return asm;
    }

    // the outermost types of a nested enum's chain, created on demand (public, no base type needed)
    private static TypeDefinition Outer(ModuleDefinition module, EnumInfo e)
    {
        var top = module.Types.FirstOrDefault(t => t.Namespace == e.Namespace && t.Name == e.Name);
        if (top == null)
        {
            top = new TypeDefinition(e.Namespace, e.Name, TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(top);
        }
        var cur = top;
        for (int i = 0; i < e.Chain.Length - 1; i++)
        {
            var next = cur.NestedTypes.FirstOrDefault(t => t.Name == e.Chain[i]);
            if (next == null)
            {
                next = new TypeDefinition("", e.Chain[i], TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object);
                cur.NestedTypes.Add(next);
            }
            cur = next;
        }
        return cur;
    }

    private static string UnderlyingName(object constant) => constant switch
    {
        sbyte => "SByte",
        byte => "Byte",
        short => "Int16",
        ushort => "UInt16",
        int => "Int32",
        uint => "UInt32",
        long => "Int64",
        ulong => "UInt64",
        char => "Char",
        bool => "Boolean",
        _ => throw new InvalidOperationException($"unexpected enum constant type {constant.GetType().Name}"),
    };

    private static TypeReference Primitive(ModuleDefinition module, string name)
    {
        var ts = module.TypeSystem;
        return name switch
        {
            "SByte" => ts.SByte, "Byte" => ts.Byte, "Int16" => ts.Int16, "UInt16" => ts.UInt16,
            "Int32" => ts.Int32, "UInt32" => ts.UInt32, "Int64" => ts.Int64, "UInt64" => ts.UInt64,
            "Char" => ts.Char, "Boolean" => ts.Boolean,
            _ => throw new InvalidOperationException($"unexpected underlying type {name}"),
        };
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes) stack.Push(n);
        }
    }

    public void Dispose()
    {
        foreach (var a in built.Values) a.Dispose();
        built.Clear();
    }
}
