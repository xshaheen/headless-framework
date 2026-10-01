// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.SourceGenerators;

/// <summary>Which dispose pattern a handler class implements, decided at build time so the generated call stays typed.</summary>
internal enum HandlerDisposal
{
    None,
    Sync,
    Async,
}

/// <summary>The symbol checks every Headless handler generator applies to a declared handler class.</summary>
internal static class HandlerSymbols
{
    public static bool Implements(Compilation compilation, INamedTypeSymbol classSymbol, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && classSymbol.AllInterfaces.Contains(type, SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Reads the dispose pattern the class implements. <c>IAsyncDisposable</c> wins over <c>IDisposable</c>, because an
    /// asynchronous release is the one the class chose when it offers both.
    /// </summary>
    public static HandlerDisposal GetDisposal(Compilation compilation, INamedTypeSymbol classSymbol)
    {
        if (Implements(compilation, classSymbol, "System.IAsyncDisposable"))
        {
            return HandlerDisposal.Async;
        }

        return Implements(compilation, classSymbol, "System.IDisposable") ? HandlerDisposal.Sync : HandlerDisposal.None;
    }

    /// <summary>
    /// Flattens a namespace-qualified class name into a method name after <paramref name="prefix"/>, replacing every
    /// character that cannot appear in an identifier with <c>_</c>.
    /// </summary>
    public static string ToMemberName(string prefix, string displayName)
    {
        var builder = new StringBuilder(prefix, prefix.Length + displayName.Length);
        foreach (var character in displayName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}

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
