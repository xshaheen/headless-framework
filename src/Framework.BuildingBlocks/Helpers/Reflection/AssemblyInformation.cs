// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Framework.BuildingBlocks.Helpers.Reflection;

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
            CommitNumber: assembly.GetCommitVersion()?.Split("+", StringSplitOptions.RemoveEmptyEntries)[^1]
        ) { }
}
