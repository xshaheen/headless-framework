using System.Reflection;

namespace Framework.BuildingBlocks.Helpers;

public sealed record AssemblyInformation(
    string? Title,
    string? Product,
    string? Description,
    string? Company,
    string? Version,
    string? CommitNumber
)
{
    public static readonly AssemblyInformation Current = new(typeof(AssemblyInformation).Assembly);

    public static readonly AssemblyInformation Entry = new(Assembly.GetEntryAssembly()!);

    private AssemblyInformation(Assembly assembly)
        : this(
            Title: assembly.GetAssemblyTitle(),
            Product: assembly.GetAssemblyProduct(),
            Description: assembly.GetAssemblyDescription(),
            Company: assembly.GetAssemblyCompany(),
            Version: assembly.GetAssemblyVersion(),
            CommitNumber: assembly.GetInformationalVersion()?.Split("+", StringSplitOptions.RemoveEmptyEntries).Last()
        ) { }
}
