// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>
/// Value-comparable diagnostic model computed in cached pipeline stages and converted to <see cref="Diagnostic"/>
/// during reporting.
/// </summary>
/// <remarks>
/// Message arguments are stored as invariant strings to keep the record comparable without pinning symbol instances.
/// </remarks>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs
)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object?[] args) =>
        new(
            descriptor,
            LocationInfo.From(location),
            args.Select(arg => Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty).ToEquatableArray()
        );

    public Diagnostic ToDiagnostic(Func<string, SyntaxTree?> findTree)
    {
        var location = Location is null
            ? Microsoft.CodeAnalysis.Location.None
            : Location.ToLocation(findTree(Location.FilePath));

        return Diagnostic.Create(Descriptor, location, MessageArgs.Cast<object?>().ToArray());
    }
}
