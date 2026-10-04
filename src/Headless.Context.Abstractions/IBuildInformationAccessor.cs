// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// Exposes build-time metadata about the running application, such as its title, product name,
/// version, and source commit.
/// </summary>
/// <remarks>
/// Implementations typically read <see cref="System.Reflection.Assembly"/> attributes from the
/// application entry assembly.
/// </remarks>
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
    /// <returns>The commit identifier, or <see langword="null"/>.</returns>
    string? GetCommitNumber();
}
