// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.SourceGenerators;

/// <summary>The source shapes every Headless handler generator emits the same way.</summary>
internal static class HandlerSource
{
    /// <summary>A C# string literal for <paramref name="value"/>, or <see langword="null"/> when there is no value.</summary>
    public static string Literal(string? value) =>
        value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// Emits the construction of a handler instance into <paramref name="variable"/>, the body that uses it, and the
    /// release its <paramref name="disposal"/> calls for.
    /// </summary>
    /// <param name="awaitUsingTarget">
    /// The expression an <c>await using</c> statement disposes, which lets each generator configure the await its own
    /// way.
    /// </param>
    public static void AppendHandlerInstance(
        this SourceCodeBuilder writer,
        HandlerDisposal disposal,
        string variable,
        string createExpression,
        string awaitUsingTarget,
        Action<SourceCodeBuilder> writeBody
    )
    {
        switch (disposal)
        {
            case HandlerDisposal.Async:
                writer.AppendLine($"var {variable} = {createExpression};");
                writer.AppendLine($"await using ({awaitUsingTarget})");
                writer.OpenBracket();
                writeBody(writer);
                writer.CloseBracket();
                break;
            case HandlerDisposal.Sync:
                writer.AppendLine($"using var {variable} = {createExpression};");
                writeBody(writer);
                break;
            default:
                writer.AppendLine($"var {variable} = {createExpression};");
                writeBody(writer);
                break;
        }
    }
}
