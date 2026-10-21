using Mono.Cecil;
using EtgVerify;

// loads the patched dll, runs the il, semantic, and numeric verifiers, then exits
// non-zero if anything failed so a deploy step can gate on this harness.
//   verify <patched.dll> --input <original.dll> --refs <ManagedDir>
string? dllPath = null, inputPath = null, refsDir = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--input" && i + 1 < args.Length) inputPath = args[++i];
    else if (args[i] == "--refs" && i + 1 < args.Length) refsDir = args[++i];
    else if (!args[i].StartsWith('-')) dllPath = args[i];
}
if (dllPath == null || inputPath == null)
{
    Console.Error.WriteLine("usage: verify <patched.dll> --input <original.dll> [--refs <ManagedDir>]  (no --refs: reference-free)");
    return 2;
}
foreach (var f in new[] { dllPath, inputPath })
    if (!File.Exists(f)) { Console.Error.WriteLine($"verify: missing dll '{f}'"); return 2; }
if (refsDir != null && !Directory.Exists(refsDir)) { Console.Error.WriteLine($"verify: missing refs dir '{refsDir}'"); return 2; }

string full = Path.GetFullPath(dllPath);
var checks = new Checks();
Console.WriteLine($"enter the gungeon 4:3 verification: {Path.GetFileName(full)}");
Console.WriteLine($"  sha256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant()}");

// with --refs every member resolves against the game's own dlls (Full); without it the resolve
// checks are skipped (ReferenceFree) and nothing may try to resolve
var mode = refsDir != null ? VerifyMode.Full : VerifyMode.ReferenceFree;
IAssemblyResolver NewResolver() => refsDir != null ? new RefsResolver(refsDir) : new NullAssemblyResolver();
using var module = ModuleDefinition.ReadModule(full, new ReaderParameters { AssemblyResolver = NewResolver() });
using var input = ModuleDefinition.ReadModule(inputPath, new ReaderParameters { AssemblyResolver = NewResolver() });

// the helper payload is read from disk here so a stale embed in the patcher still fails
byte[]? helperPayload = null;
for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null && helperPayload == null; dir = dir.Parent)
{
    var p = Path.Combine(dir.FullName, "patcher", "gungeonhelper", "GungeonHelper.dll");
    if (File.Exists(p)) helperPayload = File.ReadAllBytes(p);
}
if (helperPayload == null)
    Console.Error.WriteLine("verify: patcher/gungeonhelper/GungeonHelper.dll not found (run from inside the repo)");

VerifyRunner.Run(checks, module, input, helperPayload, mode);

checks.Summarize();
return checks.Failed == 0 ? 0 : 1;
