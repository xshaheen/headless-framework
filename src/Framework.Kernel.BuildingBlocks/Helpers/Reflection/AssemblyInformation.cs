using System.Reflection;
using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public sealed record AssemblyInformation(
    string? Title,
    string? Product,
    string? Description,
    string? Company,
    string? Version,
    string? CommitNumber
)
{
    public static readonly AssemblyInformation Entry = new(Assembly.GetEntryAssembly()!);

    internal AssemblyInformation(Assembly assembly)
        : this(
            Title: assembly.GetAssemblyTitle(),
            Product: assembly.GetAssemblyProduct(),
            Description: assembly.GetAssemblyDescription(),
            Company: assembly.GetAssemblyCompany(),
            Version: assembly.GetAssemblyVersion(),
            CommitNumber: assembly.GetInformationalVersion()?.Split("+", StringSplitOptions.RemoveEmptyEntries).Last()
        ) { }
}
