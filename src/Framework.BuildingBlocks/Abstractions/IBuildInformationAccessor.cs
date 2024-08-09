using System.Reflection;

namespace Framework.BuildingBlocks.Abstractions;

public interface IBuildInformationAccessor
{
    public string? GetProduct();

    public string? GetDescription();

    string? GetBuildNumber();
}

public sealed class BuildInformationAccessor : IBuildInformationAccessor
{
    // TODO: It will be helpful to add the git commit hash
    // Check this: https://www.hanselman.com/blog/adding-a-git-commit-hash-and-azure-devops-build-number-and-build-id-to-an-aspnet-website

    public string? GetProduct() => AssemblyInformation.Entry.Version;

    public string? GetDescription() => AssemblyInformation.Entry.Version;

    public string? GetBuildNumber() => AssemblyInformation.Entry.Version;

    private sealed record AssemblyInformation(string? Product, string? Description, string? Version)
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
}
