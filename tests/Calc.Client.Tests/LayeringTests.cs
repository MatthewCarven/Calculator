using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Calc.Client.Tests;

/// <summary>TODO rule 1: Natural lives only in Calc.Engine and Calc.Worker.</summary>
public class LayeringTests
{
    // Calc.App joins this list in C1.
    public static TheoryData<string> NaturalFree => new("Calc.Syntax", "Calc.Protocol", "Calc.Client", "Calc.Cli");

    // Natural itself, and the two assemblies that would bring it in.
    private static readonly string[] Forbidden = ["Natural", "Calc.Engine", "calc-worker"];

    [Theory]
    [MemberData(nameof(NaturalFree))]
    public void NeverReferencesNatural(string assembly)
    {
        var references = ReferencesOf(assembly);
        Assert.Contains("System.Runtime", references); // the reader really read the assembly
        Assert.DoesNotContain(references, Forbidden.Contains);
    }

    private static List<string> ReferencesOf(string assembly)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assembly + ".dll");
        Assert.True(File.Exists(path), $"{assembly}.dll isn't beside the tests.");
        using var pe = new PEReader(File.OpenRead(path));
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToList();
    }
}
