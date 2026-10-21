using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EtgVerify;

/// textual fingerprints of il, so two modules can be compared instruction by instruction
/// without caring which module object an operand belongs to.
public static class IlSig
{
    /// cecil writes a short branch as a signed byte and never widens it, so the patcher
    /// widens every short branch in a method it edits. the verifier compares edited
    /// methods through this so the widening is the only difference it forgives.
    public static OpCode? LongBranch(OpCode op)
    {
        switch (op.Code)
        {
            case Code.Br_S: return OpCodes.Br;
            case Code.Brfalse_S: return OpCodes.Brfalse;
            case Code.Brtrue_S: return OpCodes.Brtrue;
            case Code.Beq_S: return OpCodes.Beq;
            case Code.Bge_S: return OpCodes.Bge;
            case Code.Bgt_S: return OpCodes.Bgt;
            case Code.Ble_S: return OpCodes.Ble;
            case Code.Blt_S: return OpCodes.Blt;
            case Code.Bne_Un_S: return OpCodes.Bne_Un;
            case Code.Bge_Un_S: return OpCodes.Bge_Un;
            case Code.Bgt_Un_S: return OpCodes.Bgt_Un;
            case Code.Ble_Un_S: return OpCodes.Ble_Un;
            case Code.Blt_Un_S: return OpCodes.Blt_Un;
            case Code.Leave_S: return OpCodes.Leave;
            default: return null;
        }
    }

    public static string Operand(Instruction ins, Func<Instruction, string> target)
    {
        switch (ins.Operand)
        {
            case null: return "";
            case Instruction t: return "->" + target(t);
            case Instruction[] ts: return "[" + string.Join(",", ts.Select(target)) + "]";
            case MemberReference m: return m.FullName;
            case string s: return "\"" + s + "\"";
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case VariableDefinition v: return "V" + v.Index;
            case ParameterDefinition p: return "A" + p.Index;
            case CallSite cs: return cs.ToString() ?? "callsite";
            case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
            default: return ins.Operand.ToString() ?? "";
        }
    }

    /// opcode + operand, with branch targets as instruction indexes. widen=true maps short
    /// branches to their long form, shift offsets every target index (a prologue was inserted).
    public static List<string> Body(MethodBody body, bool widen = false, int shift = 0)
    {
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) index[body.Instructions[i]] = i;
        string Target(Instruction t) => index.TryGetValue(t, out var i) ? (i + shift).ToString() : "?";
        var list = new List<string>(body.Instructions.Count);
        foreach (var ins in body.Instructions)
        {
            var op = ins.OpCode;
            if (widen && LongBranch(op) is OpCode wide) op = wide;
            list.Add(op.Name + " " + Operand(ins, Target));
        }
        return list;
    }

    public static List<string> Handlers(MethodBody body, int shift = 0)
    {
        if (body.ExceptionHandlers.Count == 0) return new List<string>();
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) index[body.Instructions[i]] = i;
        string P(Instruction? t) => t == null ? "end" : index.TryGetValue(t, out var i) ? (i + shift).ToString() : "?";
        return body.ExceptionHandlers.Select(h =>
            $"{h.HandlerType} try {P(h.TryStart)}-{P(h.TryEnd)} handler {P(h.HandlerStart)}-{P(h.HandlerEnd)} filter {P(h.FilterStart)} catch {h.CatchType?.FullName}").ToList();
    }

    public static List<string> Locals(MethodBody body) =>
        body.Variables.Select(v => v.VariableType.FullName).ToList();
}
