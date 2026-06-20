// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

namespace Headless.Abstractions;

public interface IBuildInformationAccessor
{
    string? GetTitle();
    string? GetProduct();
    string? GetDescription();
    string? GetCompany();
    string? GetVersion();
    string? GetCommitNumber();
}

public sealed class BuildInformationAccessor : IBuildInformationAccessor
{
    public string? GetTitle() => AssemblyInformation.Entry?.Title;

    public string? GetProduct() => AssemblyInformation.Entry?.Product;

    public string? GetDescription() => AssemblyInformation.Entry?.Description;

    public string? GetCompany() => AssemblyInformation.Entry?.Company;

    public string? GetVersion() => AssemblyInformation.Entry?.Version;

    public string? GetCommitNumber() => AssemblyInformation.Entry?.CommitNumber;
}
