// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.SourceGenerators;

/// <summary>Turns an arbitrary dotted name, such as an assembly name, into a namespace the compiler accepts.</summary>
internal static class NamespaceName
{
    /// <summary>
    /// Maps each <c>.</c>-separated segment of <paramref name="name"/> to a valid C# identifier: a character that
    /// cannot appear in an identifier becomes <c>_</c>, a segment that cannot start an identifier (a leading digit) gets
    /// a <c>_</c> prefix, an empty segment becomes <c>_</c>, and a reserved keyword is escaped with <c>@</c>.
    /// </summary>
    /// <remarks>
    /// Assembly names are file names, so <c>Orders-Api</c>, <c>2024.Billing</c>, or <c>Orders.class</c> are legal
    /// there and break the build when emitted verbatim as a namespace. The character mapping matches the one MSBuild
    /// applies when it derives <c>RootNamespace</c> from such a name, so <c>Orders-Api</c> becomes <c>Orders_Api</c> in
    /// both places.
    /// </remarks>
    public static string From(string name)
    {
        var builder = new StringBuilder(name.Length + 2);
        var segments = name.Split('.');

        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('.');
            }

            _AppendSegment(builder, segments[i]);
        }

        return builder.ToString();
    }

    private static void _AppendSegment(StringBuilder builder, string segment)
    {
        if (segment.Length == 0)
        {
            builder.Append('_');
            return;
        }

        if (SyntaxFacts.GetKeywordKind(segment) != SyntaxKind.None)
        {
            builder.Append('@').Append(segment);
            return;
        }

        if (!SyntaxFacts.IsIdentifierStartCharacter(segment[0]))
        {
            builder.Append('_');

            // A character that may continue an identifier, such as a digit, is kept after the prefix.
            builder.Append(SyntaxFacts.IsIdentifierPartCharacter(segment[0]) ? segment[0] : '_');
        }
        else
        {
            builder.Append(segment[0]);
        }

        for (var i = 1; i < segment.Length; i++)
        {
            var c = segment[i];
            builder.Append(SyntaxFacts.IsIdentifierPartCharacter(c) ? c : '_');
        }
    }
}
