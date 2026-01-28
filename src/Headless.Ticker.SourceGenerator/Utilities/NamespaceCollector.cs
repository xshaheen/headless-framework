using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Ticker.SourceGenerator.Utilities;

/// <summary>
/// Handles collection and analysis of namespaces required for code generation.
/// </summary>
internal static class NamespaceCollector
{
    /// <summary>
    /// Collects all required namespaces based on the generated content.
    /// Handles global usings and implicit usings automatically through semantic analysis.
    /// </summary>
    public static HashSet<string> CollectRequiredNamespaces(
        IEnumerable<string> delegates,
        IEnumerable<string> ctorCalls,
        IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
        string assemblyName,
        HashSet<string>? additionalNamespaces = null
    )
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        // Add core system namespaces - always needed (using Span for performance)
        var coreNamespaces = SourceGeneratorConstants.CommonNamespaces.AsSpan();
        foreach (var ns in coreNamespaces)
        {
            namespaces.Add(ns);
        }

        // Additional core namespaces for TickerQ
        namespaces.Add("System.Threading.Tasks");

        // Add assembly namespace if valid
        if (!string.IsNullOrWhiteSpace(assemblyName) && _IsValidNamespace(assemblyName))
        {
            namespaces.Add(assemblyName);
        }

        // Always include TickerQ core namespaces
        namespaces.Add("Headless.Ticker.Utilities");
        namespaces.Add("Headless.Ticker.Utilities.Enums");

        // Add additional namespaces if provided
        if (additionalNamespaces != null)
        {
            foreach (var ns in additionalNamespaces)
            {
                if (_IsValidNamespace(ns))
                {
                    namespaces.Add(ns);
                }
            }
        }

        // Analyze generated content for type references
        _AnalyzeContentForNamespaces(delegates, namespaces);
        _AnalyzeContentForNamespaces(ctorCalls, namespaces);
        _AnalyzeRequestTypesForNamespaces(requestTypes, namespaces);

        return namespaces;
    }

    /// <summary>
    /// Collects namespaces from source types used in the generation process.
    /// </summary>
    public static HashSet<string> CollectNamespacesFromSourceTypes(
        ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
        Compilation compilation
    )
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (classDeclaration, methodDeclaration) in methodPairs)
        {
            var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

            // Collect namespaces from class
            _AddTypeNamespace(semanticModel.GetDeclaredSymbol(classDeclaration)?.ContainingNamespace, namespaces);

            // Collect namespaces from method return type
            if (semanticModel.GetDeclaredSymbol(methodDeclaration) is IMethodSymbol methodSymbol)
            {
                _AddTypeNamespace(methodSymbol.ReturnType, namespaces);

                // Collect namespaces from method parameters
                foreach (var parameter in methodSymbol.Parameters)
                {
                    _AddTypeNamespace(parameter.Type, namespaces);
                }
            }

            // Collect namespaces from constructor parameters
            _CollectConstructorParameterNamespaces(classDeclaration, semanticModel, namespaces);

            // Collect namespaces from existing using directives
            _CollectExistingUsingNamespaces(classDeclaration.SyntaxTree, namespaces);
        }

        return namespaces;
    }

    /// <summary>
    /// Collects namespaces from constructor parameters for dependency injection.
    /// </summary>
    private static void _CollectConstructorParameterNamespaces(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        HashSet<string> namespaces
    )
    {
        // Handle primary constructor parameters
        if (classDeclaration.ParameterList?.Parameters.Count > 0)
        {
            foreach (var parameter in classDeclaration.ParameterList.Parameters)
            {
                if (parameter.Type != null)
                {
                    var typeInfo = semanticModel.GetTypeInfo(parameter.Type);
                    _AddTypeNamespace(typeInfo.Type, namespaces);
                }
            }
        }

        // Handle regular constructors
        foreach (var constructor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                if (parameter.Type != null)
                {
                    var typeInfo = semanticModel.GetTypeInfo(parameter.Type);
                    _AddTypeNamespace(typeInfo.Type, namespaces);
                }
            }
        }
    }

    /// <summary>
    /// Collects namespaces from existing using directives in the source file.
    /// This includes global usings and helps ensure we don't miss any required namespaces.
    /// Excludes using static, using alias, and using global directives.
    /// </summary>
    private static void _CollectExistingUsingNamespaces(SyntaxTree syntaxTree, HashSet<string> namespaces)
    {
        var root = syntaxTree.GetRoot();

        // Collect regular using directives (exclude static, alias, and global usings)
        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
        foreach (var usingDirective in usingDirectives)
        {
            // Skip using static directives
            if (usingDirective.StaticKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
            {
                continue;
            }

            // Skip using alias directives (using X = Y;)
            if (usingDirective.Alias != null)
            {
                continue;
            }

            if (usingDirective.Name != null)
            {
                var namespaceName = usingDirective.Name.ToString();
                if (_IsValidNamespace(namespaceName) && !_IsCommonVariable(namespaceName))
                {
                    namespaces.Add(namespaceName);
                }
            }
        }
    }

    /// <summary>
    /// Analyzes content strings for namespace references.
    /// </summary>
    private static void _AnalyzeContentForNamespaces(IEnumerable<string> content, HashSet<string> namespaces)
    {
        foreach (var item in content)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            // Look for fully qualified type names and extract their namespaces
            _ExtractNamespacesFromTypeReferences(item, namespaces);
        }
    }

    /// <summary>
    /// Analyzes request types to extract their namespaces.
    /// </summary>
    private static void _AnalyzeRequestTypesForNamespaces(
        IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
        HashSet<string> namespaces
    )
    {
        foreach (var (genericTypeName, _) in requestTypes)
        {
            if (!string.IsNullOrWhiteSpace(genericTypeName))
            {
                _ExtractNamespacesFromTypeReferences(genericTypeName, namespaces);
            }
        }
    }

    /// <summary>
    /// Extracts namespace from type references in the content.
    /// Uses pre-compiled regex patterns and Span optimizations for better performance.
    /// </summary>
    private static void _ExtractNamespacesFromTypeReferences(string content, HashSet<string> namespaces)
    {
        var patterns = SourceGeneratorConstants.CompiledPatterns.AsSpan();

        foreach (var regex in patterns)
        {
            var matches = regex.Matches(content);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var fullTypeName = match.Groups[1].Value;
                var lastDotIndex = fullTypeName.LastIndexOf('.');

                if (lastDotIndex > 0)
                {
                    // Use Span to avoid substring allocation when possible
                    var namespaceName = fullTypeName.AsSpan(0, lastDotIndex).ToString();
                    if (_IsValidNamespace(namespaceName) && !_IsCommonVariable(namespaceName))
                    {
                        namespaces.Add(namespaceName);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds a type's namespace to the collection if valid.
    /// </summary>
    private static void _AddTypeNamespace(ISymbol? typeSymbol, HashSet<string> namespaces)
    {
        if (typeSymbol == null)
        {
            return;
        }

        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
        if (
            !string.IsNullOrEmpty(namespaceName)
            && namespaceName != "<global namespace>"
            && _IsValidNamespace(namespaceName)
        )
        {
            namespaces.Add(namespaceName!);
        }
    }

    /// <summary>
    /// Validates that a namespace name is valid and should be included.
    /// </summary>
    private static bool _IsValidNamespace(string? namespaceName)
    {
        return !string.IsNullOrWhiteSpace(namespaceName)
            && namespaceName != "System"
            && // Avoid duplicate System namespace
            !namespaceName!.StartsWith("System.Runtime", StringComparison.Ordinal)
            && !namespaceName.Contains("<")
            && !namespaceName.Contains(">")
            && namespaceName.Length > 1;
    }

    /// <summary>
    /// Checks if a name is a common variable name that should not be treated as a namespace.
    /// Optimized with pre-computed static HashSet.
    /// </summary>
    private static bool _IsCommonVariable(string name)
    {
        return SourceGeneratorConstants.CommonVariableNames.Contains(name);
    }
}
