// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

namespace Headless.Abstractions;

/// <summary>
/// Exposes build-time metadata about the running application, such as its title, product name,
/// version, and source commit. Implementations typically read <see cref="System.Reflection.Assembly"/>
/// attributes from the entry assembly.
/// </summary>
public interface IBuildInformationAccessor
{
    /// <summary>
    /// Returns the assembly title, or <see langword="null"/> when the attribute is not declared.
    /// </summary>
    /// <returns>The assembly title, or <see langword="null"/>.</returns>
    string? GetTitle();

    /// <summary>
    /// Returns the assembly product name, or <see langword="null"/> when the attribute is not declared.
    /// </summary>
    /// <returns>The product name, or <see langword="null"/>.</returns>
    string? GetProduct();

    /// <summary>
    /// Returns the assembly description, or <see langword="null"/> when the attribute is not declared.
    /// </summary>
    /// <returns>The assembly description, or <see langword="null"/>.</returns>
    string? GetDescription();

    /// <summary>
    /// Returns the assembly company name, or <see langword="null"/> when the attribute is not declared.
    /// </summary>
    /// <returns>The company name, or <see langword="null"/>.</returns>
    string? GetCompany();

    /// <summary>
    /// Returns the assembly file version, or <see langword="null"/> when the attribute is not declared.
    /// </summary>
    /// <returns>The file version string, or <see langword="null"/>.</returns>
    string? GetVersion();

    /// <summary>
    /// Returns the commit identifier parsed from the informational version (the segment after the last
    /// <c>+</c> separator), or <see langword="null"/> when unavailable.
    /// </summary>
    /// <returns>The commit number, or <see langword="null"/>.</returns>
    string? GetCommitNumber();
}

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
