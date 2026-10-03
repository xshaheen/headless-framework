// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.SourceGenerators;

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
