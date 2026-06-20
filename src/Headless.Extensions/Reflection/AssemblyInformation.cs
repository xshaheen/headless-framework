// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Reflection;

/// <summary>
/// Immutable snapshot of an assembly's metadata attributes (title, product, description, company, version,
/// and the commit number parsed from the informational version).
/// </summary>
/// <param name="Title">The assembly title, or <see langword="null"/> when not declared.</param>
/// <param name="Product">The assembly product name, or <see langword="null"/> when not declared.</param>
/// <param name="Description">The assembly description, or <see langword="null"/> when not declared.</param>
/// <param name="Company">The assembly company, or <see langword="null"/> when not declared.</param>
/// <param name="Version">The assembly file version, or <see langword="null"/> when not declared.</param>
/// <param name="CommitNumber">The commit number parsed from the informational version, or <see langword="null"/> when not available.</param>
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
    /// <summary>
    /// Information about the process entry assembly, or <see langword="null"/> when there is no managed entry
    /// assembly (for example unmanaged hosts or some test runners where <see cref="Assembly.GetEntryAssembly"/>
    /// returns <see langword="null"/>).
    /// </summary>
    public static readonly AssemblyInformation? Entry = Assembly.GetEntryAssembly() is { } entryAssembly
        ? new(entryAssembly)
        : null;

    /// <summary>
    /// Initializes a new <see cref="AssemblyInformation"/> by reading the metadata attributes of the given assembly.
    /// The commit number is taken from the last <c>+</c>-delimited segment of the informational version.
    /// </summary>
    /// <param name="assembly">The assembly whose metadata is read.</param>
    public AssemblyInformation(Assembly assembly)
        : this(
            Title: assembly.GetAssemblyTitle(),
            Product: assembly.GetAssemblyProduct(),
            Description: assembly.GetAssemblyDescription(),
            Company: assembly.GetAssemblyCompany(),
            Version: assembly.GetAssemblyVersion(),
            CommitNumber: assembly.GetCommitVersion()?.Split('+', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        ) { }
}
