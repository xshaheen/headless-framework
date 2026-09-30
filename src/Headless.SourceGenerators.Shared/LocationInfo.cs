// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Headless.SourceGenerators;

/// <summary>
/// A value-equal source location. A <see cref="Location"/> holds its <see cref="SyntaxTree"/>, so keeping one in a
/// pipeline model would pin a compilation in memory and make equality depend on tree identity.
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
    /// Rebuilds the location against <paramref name="tree"/> when the compilation still has it, which keeps
    /// <c>#pragma</c> suppression and IDE navigation working; otherwise falls back to a file-path location.
    /// </summary>
    public Location ToLocation(SyntaxTree? tree) =>
        tree is null ? Location.Create(FilePath, TextSpan, LineSpan) : Location.Create(tree, TextSpan);
}
