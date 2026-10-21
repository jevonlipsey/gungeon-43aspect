using System.IO;
using System.Linq;
using Mono.Cecil;

namespace EtgVerify;

/// resolves only from the --refs directory (the game's own Managed folder). the stock
/// DefaultAssemblyResolver also searches ".", "bin", the gac and the host framework, so a
/// member that exists only in the patcher's runtime would resolve and then fail at JIT on
/// the device. resolving is rejected unless the file it found lives under refs.
public sealed class RefsResolver : DefaultAssemblyResolver
{
    private readonly string refsDir;

    public RefsResolver(string refsDir)
    {
        this.refsDir = Path.GetFullPath(refsDir);
        foreach (var d in GetSearchDirectories().ToArray()) RemoveSearchDirectory(d);
        AddSearchDirectory(this.refsDir);
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        var asm = base.Resolve(name, parameters);
        var file = asm.MainModule.FileName;
        if (file == null || !Path.GetFullPath(file).StartsWith(refsDir, System.StringComparison.Ordinal))
            throw new AssemblyResolutionException(name);
        return asm;
    }
}

/// an assembly resolver that resolves nothing. the patcher never needs a unity assembly
/// (its unity calls come from the cloned helper), so this is what it reads with. anything
/// that does try to resolve fails loudly instead of quietly binding to the host runtime.
public sealed class NullAssemblyResolver : IAssemblyResolver
{
    public AssemblyDefinition Resolve(AssemblyNameReference name) =>
        throw new AssemblyResolutionException(name);

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) =>
        throw new AssemblyResolutionException(name);

    public void Dispose() { }
}
