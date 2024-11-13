// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Framework.Generator.Primitives.Shared;

/// <summary>
/// A collection of extension methods for working with Roslyn syntax and symbols.
/// See: https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm
/// </summary>
public static class RoslynExtensions
{
    /// <summary>Gets the location of the attribute data within the source code.</summary>
    /// <param name="self">The attribute data to retrieve the location for.</param>
    /// <returns>The location of the attribute data in the source code, or null if not found.</returns>
    public static Location? GetAttributeLocation(this AttributeData self)
    {
        var syntaxReference = self.ApplicationSyntaxReference;

        var syntax = (AttributeSyntax?)syntaxReference?.GetSyntax();

        return syntax?.GetLocation();
    }

    /// <summary>Checks if the symbol has a default constructor (parameterless constructor) defined and retrieves its location.</summary>
    /// <param name="self">The symbol to check for a default constructor.</param>
    /// <param name="location">When this method returns, contains the location of the default constructor, if found; otherwise, null.</param>
    /// <returns>True if a default constructor is found; otherwise, false.</returns>
    public static bool HasDefaultConstructor(this ISymbol? self, out Location? location)
    {
        var constructors = self.GetConstructorsFromSyntaxTree();

        var ctor = constructors?.Find(x => x.ParameterList.Parameters.Count == 0);
        location = ctor?.GetLocation();

        return ctor is not null;
    }

    /// <summary>Retrieves a list of constructor declarations associated with the symbol from the syntax tree.</summary>
    /// <param name="self">The symbol for which to retrieve constructor declarations.</param>
    /// <returns>A list of constructor declarations or null if none are found.</returns>
    public static List<ConstructorDeclarationSyntax>? GetConstructorsFromSyntaxTree(this ISymbol? self)
    {
        var declaringSyntaxReferences = self?.DeclaringSyntaxReferences;

        if (self is null || declaringSyntaxReferences is null or { Length: 0 })
        {
            return null;
        }

        List<ConstructorDeclarationSyntax>? result = null;

        foreach (var syntax in declaringSyntaxReferences)
        {
            if (
                syntax.GetSyntax() is TypeDeclarationSyntax classDeclaration
                && string.Equals(classDeclaration.GetClassFullName(), self.ToString(), StringComparison.Ordinal)
            )
            {
                var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>();

                result ??= [];
                result.AddRange(constructors);
            }
        }

        return result;
    }

    /// <summary>
    /// Get namespace for BaseTypeDeclarationSyntax and its child type
    /// (which includes EnumDeclarationSyntax, ClassDeclarationSyntax, StructDeclarationSyntax,
    /// RecordDeclarationSyntax etc.) Its handles all the namespace cases including nested namespaces and file scoped namespaces.
    /// This method is based no <a href="https://github.com/dotnet/runtime/blob/25c675ff78e0446fe596cea25c7e3969b0936a33/src/libraries/Microsoft.Extensions.Logging.Abstractions/gen/LoggerMessageGenerator.Parser.cs#L438">Logger Message generator</a>
    /// </summary>
    /// <returns>
    /// Its returns null to indicates to the source generator to not emit a namespace declaration.
    /// That ensures the generated code will be in the same namespace as the target type, whether it's global:: or some other value defined in &lt;RootNamespace&gt;.
    /// </returns>
    public static string? GetNamespace(this BaseTypeDeclarationSyntax syntax)
    {
        // If we don't have a namespace at all we'll return an empty string
        // This accounts for the "default namespace" case
        var nameSpace = string.Empty;

        // Get the containing syntax node for the type declaration
        // (could be a nested type, for example)
        var potentialNamespaceParent = syntax.Parent;

        // Keep moving "out" of nested classes etc. until we get to a namespace
        // or until we run out of parents
        while (
            potentialNamespaceParent
                is not null
                    and not NamespaceDeclarationSyntax
                    and not FileScopedNamespaceDeclarationSyntax
        )
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        // Build up the final namespace by looping until we no longer have a namespace declaration
        if (potentialNamespaceParent is BaseNamespaceDeclarationSyntax namespaceParent)
        {
            // We have a namespace. Use that as the type
            nameSpace = namespaceParent.Name.ToString();

            // Keep moving "out" of the namespace declarations until we
            // run out of nested namespace declarations
            while (namespaceParent.Parent is NamespaceDeclarationSyntax parent)
            {
                // Add the outer namespace as a prefix to the final namespace
                nameSpace = $"{namespaceParent.Name}.{nameSpace}";
                namespaceParent = parent;
            }
        }

        // return the final namespace
        return nameSpace is "" ? null : nameSpace;
    }

    /// <summary>Gets the fully qualified name of the specified type declaration syntax, including its namespace.</summary>
    /// <param name="self">The type declaration syntax to retrieve the fully qualified name from.</param>
    /// <returns>The fully qualified name of the type declaration.</returns>
    public static string GetClassFullName(this TypeDeclarationSyntax self)
    {
        var ns = self.GetNamespace();

        return ns is null ? self.GetClassName() : ns + "." + self.GetClassName();
    }

    /// <summary>
    /// Gets the name of the class specified in the type declaration syntax.
    /// </summary>
    /// <param name="proxy">The type declaration syntax to retrieve the class name from.</param>
    /// <returns>The name of the class.</returns>
    public static string GetClassName(this TypeDeclarationSyntax proxy)
    {
        return proxy.Identifier.Text + proxy.TypeParameterList?.ToFullString();
    }

    /// <summary>Builds up the linked list of ParentClass starting from the type closest to our target type.</summary>
    /// <param name="typeSyntax"></param>
    /// <returns></returns>
    public static ParentClass? GetParentClasses(BaseTypeDeclarationSyntax typeSyntax)
    {
        // Try and get the parent syntax. If it isn't a type like class/struct, this will be null
        var parentSyntax = typeSyntax.Parent as TypeDeclarationSyntax;
        ParentClass? parentClassInfo = null;

        // Keep looping while we're in a supported nested type
        while (parentSyntax != null && isAllowedKind(parentSyntax.Kind()))
        {
            // Record the parent type keyword (class/struct etc.), name, and constraints
            parentClassInfo = new ParentClass(
                keyword: parentSyntax.Keyword.ValueText,
                name: parentSyntax.Identifier.ToString() + parentSyntax.TypeParameterList,
                constraints: parentSyntax.ConstraintClauses.ToString(),
                child: parentClassInfo
            ); // set the child link (null initially)

            // Move to the next outer type
            parentSyntax = (parentSyntax.Parent as TypeDeclarationSyntax);
        }

        // return a link to the outermost parent type
        return parentClassInfo;

        // We can only be nested in class/struct/record
        static bool isAllowedKind(SyntaxKind kind)
        {
            return kind is SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration or SyntaxKind.RecordDeclaration;
        }
    }
}
