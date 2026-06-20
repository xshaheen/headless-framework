// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Reflection;

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
