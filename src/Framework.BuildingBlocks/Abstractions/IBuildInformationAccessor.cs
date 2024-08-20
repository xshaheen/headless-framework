using Framework.BuildingBlocks.Helpers;
using Framework.BuildingBlocks.Helpers.Reflection;

namespace Framework.BuildingBlocks.Abstractions;

public interface IBuildInformationAccessor
{
    string? GetTitle();
    string? GetProduct();
    string? GetDescription();
    string? GetCompany();
    string? GetBuildNumber();
    string? CommitNumber();
}

public sealed class BuildInformationAccessor : IBuildInformationAccessor
{
    public string? GetTitle() => AssemblyInformation.Entry.Title;

    public string? GetProduct() => AssemblyInformation.Entry.Product;

    public string? GetDescription() => AssemblyInformation.Entry.Description;

    public string? GetCompany() => AssemblyInformation.Entry.Company;

    public string? GetBuildNumber() => AssemblyInformation.Entry.Version;

    public string? CommitNumber() => AssemblyInformation.Entry.CommitNumber;
}
