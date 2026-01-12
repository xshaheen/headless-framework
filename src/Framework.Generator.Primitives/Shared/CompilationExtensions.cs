// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Framework.Generator.Primitives.Shared;

/// <summary>Extension methods for working with Roslyn's Compilation and related types.</summary>
public static class CompilationExtensions
{
    /// <summary>Checks if the symbol has public accessibility.</summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <returns>True if the symbol has public accessibility; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPublic(this ISymbol symbol) => symbol.DeclaredAccessibility == Accessibility.Public;

    /// <summary>Gets members of a specific type for a given ITypeSymbol.</summary>
    /// <typeparam name="TMember">The type of members to retrieve.</typeparam>
    /// <param name="self">The ITypeSymbol to retrieve members from.</param>
    /// <returns>An IEnumerable of members of the specified type.</returns>
    public static IEnumerable<TMember> GetMembersOfType<TMember>(this ITypeSymbol? self)
        where TMember : ISymbol
    {
        return self?.GetMembers().OfType<TMember>() ?? [];
    }

    /// <summary>Gets the modifiers for the named type symbol.</summary>
    /// <param name="self">The named type symbol to retrieve modifiers from.</param>
    /// <returns>The modifiers as a string, or null if the type is null or has no modifiers.</returns>
    public static string? GetModifiers(this INamedTypeSymbol? self)
    {
        var declaringSyntax = self?.DeclaringSyntaxReferences;
        if (self is null || declaringSyntax is null or { Length: 0 })
        {
            return null;
        }

        foreach (var syntax in declaringSyntax)
        {
            if (
                syntax.GetSyntax() is TypeDeclarationSyntax typeDeclaration
                && string.Equals(
                    typeDeclaration.GetClassName(),
                    self.GetClassNameWithArguments(),
                    StringComparison.Ordinal
                )
            )
            {
                var modifiers = typeDeclaration.Modifiers.ToString();
                if (typeDeclaration is RecordDeclarationSyntax)
                {
                    modifiers += " record";
                }

                return modifiers;
            }
        }

        return null;
    }

    /// <summary>Gets the class name including generic arguments as a string.</summary>
    /// <param name="type">The named type symbol to get the class name from.</param>
    /// <returns>The class name including generic arguments as a string.</returns>
    public static string GetClassNameWithArguments(this INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(type.Name);

        if (type.TypeArguments.Length == 0)
        {
            return builder.ToString();
        }

        builder.Append('<');
        for (var index = 0; index < type.TypeArguments.Length; index++)
        {
            var arg = type.TypeArguments[index];
            builder.Append(arg.Name);

            if (index != type.TypeArguments.Length - 1)
            {
                builder.Append(", ");
            }
        }

        builder.Append('>');

        return builder.ToString();
    }

    /// <summary>Checks if the specified named type symbol implements the specified interface by its full name.</summary>
    /// <param name="type">The named type symbol to check for interface implementation.</param>
    /// <param name="interfaceFullName">The full name of the interface to check for implementation.</param>
    /// <returns>True if the type implements the interface; otherwise, false.</returns>
    public static bool ImplementsInterface(this INamedTypeSymbol type, string interfaceFullName)
    {
        var span = interfaceFullName.AsSpan();

        if (span.StartsWith("global::".AsSpan(), StringComparison.Ordinal))
        {
            span = span.Slice(8);
        }

        // Extract interface name (after last dot) and namespace parts
        var lastDot = span.LastIndexOf('.');
        if (lastDot < 0)
        {
            // No namespace, just interface name
            foreach (var symbol in type.AllInterfaces)
            {
                if (symbol.ContainingNamespace.IsGlobalNamespace && span.Equals(symbol.Name.AsSpan(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        var interfaceName = span.Slice(lastDot + 1);
        var namespacePart = span.Slice(0, lastDot);

        foreach (var symbol in type.AllInterfaces)
        {
            // Quick check: compare interface name first (cheap)
            if (!interfaceName.Equals(symbol.Name.AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            // Now compare namespace by walking the namespace hierarchy
            if (_NamespaceMatches(symbol.ContainingNamespace, namespacePart))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks if namespace symbol matches the expected namespace string (e.g. "System.Collections").</summary>
    private static bool _NamespaceMatches(INamespaceSymbol ns, ReadOnlySpan<char> expectedNamespace)
    {
        if (ns.IsGlobalNamespace)
        {
            return expectedNamespace.IsEmpty;
        }

        // Find the last segment in the expected namespace
        var lastDot = expectedNamespace.LastIndexOf('.');
        ReadOnlySpan<char> currentSegment;
        ReadOnlySpan<char> remainingNamespace;

        if (lastDot < 0)
        {
            currentSegment = expectedNamespace;
            remainingNamespace = [];
        }
        else
        {
            currentSegment = expectedNamespace.Slice(lastDot + 1);
            remainingNamespace = expectedNamespace.Slice(0, lastDot);
        }

        // Compare current namespace segment
        if (!currentSegment.Equals(ns.Name.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        // Recurse to parent namespace
        return _NamespaceMatches(ns.ContainingNamespace, remainingNamespace);
    }

    /// <summary>
    /// Gets a friendly name for the named type symbol, including nullable types.
    /// </summary>
    /// <param name="type">The named type symbol to get the friendly name from.</param>
    /// <returns>The friendly name of the type, including nullable types if applicable.</returns>
    public static string GetFriendlyName(this INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
                SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining
            )
        )!;

        if (_TypeAliases.TryGetValue(ns + "." + type.MetadataName, out var result))
        {
            return result;
        }

        var friendlyName = new StringBuilder(type.MetadataName);

        if (!type.IsGenericType)
        {
            return friendlyName.ToString();
        }

        var iBacktick = friendlyName.ToString().IndexOf('`');
        if (iBacktick > 0)
        {
            friendlyName.Length = iBacktick;
        }

        friendlyName.Append('<');
        var typeParameters = type.TypeArguments;
        for (var i = 0; i < typeParameters.Length; ++i)
        {
            if (i > 0)
            {
                friendlyName.Append(',');
            }

            friendlyName.Append(typeParameters[i]);
        }
        friendlyName.Append('>');

        return friendlyName.ToString();
    }

    /// <summary>
    /// A dictionary that provides aliases for common .NET types, mapping their full names to shorter aliases.
    /// </summary>
    private static readonly FrozenDictionary<string, string> _TypeAliases = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        { typeof(byte).FullName, "byte" },
        { typeof(sbyte).FullName, "sbyte" },
        { typeof(short).FullName, "short" },
        { typeof(ushort).FullName, "ushort" },
        { typeof(int).FullName, "int" },
        { typeof(uint).FullName, "uint" },
        { typeof(long).FullName, "long" },
        { typeof(ulong).FullName, "ulong" },
        { typeof(float).FullName, "float" },
        { typeof(double).FullName, "double" },
        { typeof(decimal).FullName, "decimal" },
        { typeof(object).FullName, "object" },
        { typeof(bool).FullName, "bool" },
        { typeof(char).FullName, "char" },
        { typeof(string).FullName, "string" },
        { typeof(void).FullName, "void" },
    }.ToFrozenDictionary();
}
