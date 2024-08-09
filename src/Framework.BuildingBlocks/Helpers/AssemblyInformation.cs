using System.Reflection;

namespace Framework.BuildingBlocks.Helpers;

public sealed record AssemblyInformation(string? Product, string? Description, string? Version)
{
    public static readonly AssemblyInformation Current = new(typeof(AssemblyInformation).Assembly);

    public static readonly AssemblyInformation Entry = new(Assembly.GetEntryAssembly()!);

    private AssemblyInformation(Assembly assembly)
        : this(
            Product: assembly.GetAssemblyProduct(),
            Description: assembly.GetAssemblyDescription(),
            Version: assembly.GetAssemblyVersion()
        ) { }
}
