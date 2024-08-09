using Framework.BuildingBlocks.Helpers;

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
}
