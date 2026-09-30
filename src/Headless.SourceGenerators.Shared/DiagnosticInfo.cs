// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Headless.SourceGenerators;

/// <summary>
/// A value-equal description of a diagnostic, computed inside a cached pipeline step and turned into a real
/// <see cref="Diagnostic"/> only when it is reported.
/// </summary>
/// <remarks>
/// Message arguments are captured as invariant strings: a diagnostic's arguments are formatted into text anyway, and
/// strings keep the record comparable without holding symbols or boxed values.
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
