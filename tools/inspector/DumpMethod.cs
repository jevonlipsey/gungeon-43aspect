using System;
using System.IO;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

class DumpMethod
{
    static void Main(string[] args)
    {
        string dllPath = args[0];
        var resolver = new UniversalAssemblyResolver(dllPath, false, null);
        resolver.AddSearchDirectory(Path.GetDirectoryName(dllPath));
        var decompiler = new CSharpDecompiler(dllPath, resolver, new DecompilerSettings());
        var name = new FullTypeName("BraveCameraUtility");
        var type = decompiler.TypeSystem.FindType(name).GetDefinition();
        foreach (var m in type.Methods)
        {
            if (m.Name == "MaintainCameraAspect")
            {
                Console.WriteLine(decompiler.DecompileAsString(m.MetadataToken));
            }
        }
    }
}
