// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Headless.SourceGenerators;

/// <summary>
/// Value-comparable source code location for caching in incremental generator pipelines.
/// Avoids holding references to <see cref="SyntaxTree"/> instances.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location?.SourceTree is null)
        {
            return null;
        }

        return new(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    /// <summary>
    /// Recreates a <see cref="Location"/> instance using the provided syntax tree when available,
    /// or falls back to an external file-path location.
    /// </summary>
    public Location ToLocation(SyntaxTree? tree) =>
        tree is null ? Location.Create(FilePath, TextSpan, LineSpan) : Location.Create(tree, TextSpan);
}
