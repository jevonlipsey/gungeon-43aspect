using Mono.Cecil;

namespace EtgVerify;

/// runs the three verifier layers in order against an open module. shared by the
/// standalone harness (verify/Program.cs) and the patcher, which gates its output on it.
public static class VerifyRunner
{
    // input is the unpatched dll the output was made from; helperPayload is the
    // GungeonHelper.dll the patcher embedded; see SemanticVerifier.
    public static void Run(Checks checks, ModuleDefinition module, ModuleDefinition input, byte[]? helperPayload,
        VerifyMode mode = VerifyMode.Full)
    {
        new IlVerifier(checks, module, input, mode).Run();
        new SemanticVerifier(checks, module, input, helperPayload).Run();
        new Simulator(checks).Run();
    }
}
