using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using EtgVerify;
using Mono.Cecil;

/// result of one PatchApi.Patch call. Output is non-null only when Reason is "ok", which
/// also means the verifier ran on exactly these bytes and Failed is 0.
public sealed class PatchResult
{
    /// the patched assembly, or null when nothing should be written
    public byte[]? Output { get; init; }
    /// "ok" | "already_patched" | "not_etg" | "platform_mismatch" | "verify_failed" | "error"
    public string Reason { get; init; } = "error";
    /// the patcher's progress log (what the cli prints)
    public string Log { get; init; } = "";
    /// every line the verifier printed, one per check
    public string VerifyLog { get; init; } = "";
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public string[] Failures { get; init; } = Array.Empty<string>();
    /// sha256 of Output as lowercase hex, or null
    public string? Sha256 { get; init; }
    /// the GetPlatformName literal of the input ("Windows" | "Mac" | "Linux"), or null if it was not read
    public string? Platform { get; init; }
    /// Full when refs were given, ReferenceFree otherwise
    public VerifyMode Mode { get; init; }
    public string Version { get; init; } = PatchApi.Version;
    public long PatchMillis { get; init; }
    public long VerifyMillis { get; init; }

    public bool Ok => Reason == "ok";
    public string Summary => Checks.FormatSummary(Passed, Failed, Skipped);
}

/// bytes in, bytes out, verified. no file i/o of its own beyond reading the refs directory (when given).
public static class PatchApi
{
    public const string Version = "v1.0";

    // the patcher keeps no state, but the log goes through Console, so one patch at a time
    private static readonly object Gate = new();

    /// patches an Enter the Gungeon Assembly-CSharp.dll for `target` (win|mac|linux|auto) and
    /// verifies the result before returning it. `auto` patches whatever platform the dll is.
    /// refs is the game's own Managed folder: given, the verifier resolves every external member
    /// against it (VerifyMode.Full); null, those resolve checks are skipped (ReferenceFree).
    /// the output bytes never depend on refs.
    public static PatchResult Patch(byte[] input, string target, string? refs)
    {
        lock (Gate)
            return PatchLocked(input, target, refs);
    }

    private static PatchResult PatchLocked(byte[] input, string target, string? refsDir)
    {
        var mode = refsDir == null ? VerifyMode.ReferenceFree : VerifyMode.Full;
        var log = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(log);
        try
        {
            return PatchCore(input, target, refsDir, mode, log);
        }
        catch (Exception ex)
        {
            log.WriteLine($"[!] {ex.GetType().Name}: {ex.Message}");
            log.WriteLine(ex.StackTrace);
            return Fail("error", log, mode);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static PatchResult PatchCore(byte[] input, string target, string? refsDir, VerifyMode mode, StringWriter log)
    {
        if (input == null || input.Length == 0)
        {
            log.WriteLine("[!] input is empty");
            return Fail("error", log, mode);
        }
        string? wantPlatform = Sites.TargetToPlatform(target);

        log.WriteLine($"[+] patching assembly ({input.Length} bytes), target {target}, verify mode: {mode}");
        var clock = Stopwatch.StartNew();
        byte[] patched;

        // the patch logic resolves nothing, but cecil's writer does: a method parameter with an
        // enum-typed default (a unity enum here) has to be resolved to pick the constant's
        // encoding. the stub resolver synthesizes just those enums from the input's own constants,
        // so no unity dll is needed and the output cannot depend on one
        var stubs = new StubEnumResolver();
        AssemblyDefinition asm;
        try
        {
            asm = AssemblyDefinition.ReadAssembly(new MemoryStream(input), new ReaderParameters { AssemblyResolver = stubs });
        }
        catch (Exception ex)
        {
            log.WriteLine($"[!] input is not a readable .net assembly ({ex.GetType().Name}: {ex.Message})");
            stubs.Dispose();
            return Fail("not_etg", log, mode);
        }

        string? platform;
        using (stubs)
        using (asm)
        {
            var types = new System.Collections.Generic.HashSet<string>(asm.MainModule.Types.Select(t => t.Name));
            var missing = Sites.IdentityTypes.Where(t => !types.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                log.WriteLine($"[!] this is not Enter the Gungeon's Assembly-CSharp.dll (missing {string.Join(", ", missing)})");
                return Fail("not_etg", log, mode);
            }
            if (types.Contains(Sites.HelperType))
            {
                log.WriteLine("[!] this assembly is already patched (it contains Gungeon43). patch the original Assembly-CSharp.dll");
                return Fail("already_patched", log, mode);
            }

            // the literal is the wwise soundbank folder name; a dll built for another platform
            // makes the sound engine look in a folder that does not exist and the boot hangs
            platform = Program.ReadPlatform(asm.MainModule);
            log.WriteLine($"[+] platform (AkBasePathGetter.GetPlatformName): {platform ?? "<unreadable>"}");
            if (wantPlatform == null)
            {
                // auto: patch whatever this dll is, but only a platform the game ships
                if (platform is not ("Windows" or "Mac" or "Linux"))
                {
                    log.WriteLine("[!] cannot tell which platform this Assembly-CSharp.dll is for (unexpected GetPlatformName)");
                    return Fail("not_etg", log, mode);
                }
            }
            else if (platform != wantPlatform)
            {
                log.WriteLine($"[!] --target {target} needs a '{wantPlatform}' dll but this one is '{platform}'. a mac dll on the device");
                log.WriteLine("    makes wwise look in GeneratedSoundBanks/Mac, the audio never starts and the boot hangs on the");
                log.WriteLine("    first load screen. patch the dll that belongs to the build you are installing into.");
                return Fail("platform_mismatch", log, mode, platform);
            }

            // everything the writer may need to resolve is in the input's constants by now
            stubs.Prime(asm.MainModule);
            patched = Program.PatchAssembly(asm);
        }
        long patchMs = clock.ElapsedMilliseconds;

        // gate on the verifier, run on the bytes we are about to hand out
        clock.Restart();
        var verifyLog = new StringWriter();
        var checks = new Checks(verifyLog);
        using (var ms = new MemoryStream(patched))
        using (var module = ModuleDefinition.ReadModule(ms, new ReaderParameters { AssemblyResolver = mode == VerifyMode.Full ? new RefsResolver(refsDir!) : new NullAssemblyResolver() }))
        using (var inMs = new MemoryStream(input))
        using (var inModule = ModuleDefinition.ReadModule(inMs, new ReaderParameters { AssemblyResolver = new NullAssemblyResolver() }))
        {
            VerifyRunner.Run(checks, module, inModule, Program.ReadHelperAssembly(), mode);
        }
        checks.Summarize();
        long verifyMs = clock.ElapsedMilliseconds;
        log.WriteLine($"[+] patched in {patchMs} ms, verified in {verifyMs} ms");

        bool ok = checks.Failed == 0;
        if (!ok) log.WriteLine($"[!] verification failed ({checks.Failed} check(s)); no output");
        return new PatchResult
        {
            Output = ok ? patched : null,
            Reason = ok ? "ok" : "verify_failed",
            Log = log.ToString(),
            VerifyLog = verifyLog.ToString(),
            Passed = checks.Passed,
            Failed = checks.Failed,
            Skipped = checks.Skipped,
            Failures = checks.Failures.ToArray(),
            Platform = platform,
            Mode = mode,
            PatchMillis = patchMs,
            VerifyMillis = verifyMs,
            Sha256 = ok ? Convert.ToHexString(SHA256.HashData(patched)).ToLowerInvariant() : null,
        };
    }

    private static PatchResult Fail(string reason, StringWriter log, VerifyMode mode, string? platform = null) =>
        new() { Reason = reason, Log = log.ToString(), Mode = mode, Platform = platform };
}
