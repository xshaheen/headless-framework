// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

namespace Headless.Abstractions;

/// <summary>
/// Reads build metadata from <see cref="AssemblyInformation.Entry"/>, which reflects the
/// process entry assembly's attributes. All members return <see langword="null"/> when there
/// is no managed entry assembly (for example in some test runners).
/// </summary>
public sealed class BuildInformationAccessor : IBuildInformationAccessor
{
    /// <inheritdoc/>
    public string? GetTitle()
    {
        return AssemblyInformation.Entry?.Title;
    }

    /// <inheritdoc/>
    public string? GetProduct()
    {
        return AssemblyInformation.Entry?.Product;
    }

    /// <inheritdoc/>
    public string? GetDescription()
    {
        return AssemblyInformation.Entry?.Description;
    }

    /// <inheritdoc/>
    public string? GetCompany()
    {
        return AssemblyInformation.Entry?.Company;
    }

    /// <inheritdoc/>
    public string? GetVersion()
    {
        return AssemblyInformation.Entry?.Version;
    }

    /// <inheritdoc/>
    public string? GetCommitNumber()
    {
        return AssemblyInformation.Entry?.CommitNumber;
    }
}
