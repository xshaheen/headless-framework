// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Parsing;

/// <summary>
/// Turns the Jobs middleware attributes on one declaration (the assembly, via a compilation unit, or a method) into
/// value models. Checks that need the whole assembly, such as target resolution and duplicates, run later.
/// </summary>
internal static class MiddlewareParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is CompilationUnitSyntax or MethodDeclarationSyntax { Parent: ClassDeclarationSyntax };

    public static MiddlewareResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compilation = context.SemanticModel.Compilation;
        var declarations = new List<MiddlewareDeclarationModel>();
        var diagnostics = new List<DiagnosticInfo>();

        if (context.TargetNode is CompilationUnitSyntax)
        {
            foreach (var attribute in context.Attributes)
            {
                if (_TryGetMiddleware(attribute, out var middlewareType, out var isSchedule))
                {
                    declarations.Add(
                        _CreateDeclaration(
                            compilation,
                            attribute,
                            middlewareType,
                            isSchedule,
                            MiddlewarePlacement.Assembly,
                            _GetNamedString(attribute, "Function")
                        )
                    );
                }
            }
        }
        else if (context.TargetSymbol is IMethodSymbol method)
        {
            var jobFunction = method.GetAttributes().FirstOrDefault(SourceGeneratorUtilities.IsJobFunctionAttribute);
            foreach (var attribute in context.Attributes)
            {
                if (!_TryGetMiddleware(attribute, out var middlewareType, out var isSchedule))
                {
                    continue;
                }

                var location = _GetAttributeLocation(attribute);
                if (
                    attribute.NamedArguments.Any(argument =>
                        string.Equals(argument.Key, "Function", StringComparison.Ordinal)
                    )
                )
                {
                    diagnostics.Add(
                        DiagnosticInfo.Create(DiagnosticDescriptors.MethodMiddlewareFunctionTarget, location)
                    );
                    continue;
                }

                if (jobFunction is null)
                {
                    diagnostics.Add(
                        DiagnosticInfo.Create(DiagnosticDescriptors.MethodMiddlewareRequiresJobFunction, location)
                    );
                    continue;
                }

                if (
                    jobFunction.ConstructorArguments.Length == 0
                    || jobFunction.ConstructorArguments[0].Value is not string target
                    || string.IsNullOrWhiteSpace(target)
                )
                {
                    // HF004 already reports the missing function name on the [JobFunction] attribute itself.
                    continue;
                }

                declarations.Add(
                    _CreateDeclaration(
                        compilation,
                        attribute,
                        middlewareType,
                        isSchedule,
                        MiddlewarePlacement.Method,
                        target
                    )
                );
            }
        }

        return new(declarations.ToEquatableArray(), diagnostics.ToEquatableArray());
    }

    /// <summary>Reads the durable function names that referenced assemblies publish through generated metadata.</summary>
    public static EquatableArray<string> GetReferencedFunctionNames(
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var functions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var attribute in assembly.GetAttributes())
            {
                if (
                    string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        SourceGeneratorConstants.DescriptorMetadataAttributeName,
                        StringComparison.Ordinal
                    )
                    && attribute.ConstructorArguments.Length >= 1
                    && attribute.ConstructorArguments[0].Value is string name
                )
                {
                    functions.Add(name);
                }
            }
        }

        return functions.ToEquatableArray();
    }

    private static MiddlewareDeclarationModel _CreateDeclaration(
        Compilation compilation,
        AttributeData attribute,
        INamedTypeSymbol middlewareType,
        bool isSchedule,
        MiddlewarePlacement placement,
        string? function
    )
    {
        var priority =
            attribute
                .NamedArguments.FirstOrDefault(argument =>
                    string.Equals(argument.Key, "Priority", StringComparison.Ordinal)
                )
                .Value.Value as int?
            ?? 0;

        return new(
            placement,
            isSchedule,
            function,
            priority,
            middlewareType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            middlewareType.ToDisplayString(),
            _GetMetadataName(middlewareType),
            _IsAccessibleFromGeneratedCode(middlewareType),
            _ImplementsMiddlewareInterface(compilation, middlewareType, isSchedule),
            LocationInfo.From(_GetAttributeLocation(attribute))
        );
    }

    private static bool _TryGetMiddleware(
        AttributeData attribute,
        out INamedTypeSymbol middlewareType,
        out bool isSchedule
    )
    {
        middlewareType = null!;
        isSchedule = false;
        if (
            attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass
            || attributeClass.TypeArguments[0] is not INamedTypeSymbol typeArgument
            || !string.Equals(
                attributeClass.ContainingNamespace.ToDisplayString(),
                "Headless.Jobs",
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        var metadataName = attributeClass.OriginalDefinition.MetadataName;
        if (string.Equals(metadataName, "JobScheduleMiddlewareAttribute`1", StringComparison.Ordinal))
        {
            middlewareType = typeArgument;
            isSchedule = true;
            return true;
        }

        if (!string.Equals(metadataName, "JobExecuteMiddlewareAttribute`1", StringComparison.Ordinal))
        {
            return false;
        }

        middlewareType = typeArgument;
        return true;
    }

    private static bool _IsAccessibleFromGeneratedCode(INamedTypeSymbol middlewareType)
    {
        for (var current = middlewareType; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }

            if (
                current.DeclaredAccessibility
                is not Accessibility.Public
                    and not Accessibility.Internal
                    and not Accessibility.ProtectedOrInternal
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool _ImplementsMiddlewareInterface(
        Compilation compilation,
        INamedTypeSymbol middlewareType,
        bool isSchedule
    )
    {
        var expectedType = compilation.GetTypeByMetadataName(
            isSchedule ? "Headless.Jobs.IJobScheduleMiddleware" : "Headless.Jobs.IJobExecuteMiddleware"
        );
        return expectedType is not null
            && (
                SymbolEqualityComparer.Default.Equals(middlewareType, expectedType)
                || middlewareType.AllInterfaces.Contains(expectedType, SymbolEqualityComparer.Default)
            );
    }

    private static string? _GetNamedString(AttributeData attribute, string name) =>
        attribute
            .NamedArguments.FirstOrDefault(argument => string.Equals(argument.Key, name, StringComparison.Ordinal))
            .Value.Value as string;

    private static Location? _GetAttributeLocation(AttributeData attribute)
    {
        var syntaxReference = attribute.ApplicationSyntaxReference;
        return syntaxReference?.SyntaxTree.GetLocation(syntaxReference.Span);
    }

    private static string _GetMetadataName(INamedTypeSymbol symbol)
    {
        var names = new Stack<string>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            names.Push(_GetMetadataSegment(current));
        }

        var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join("+", names)
            : $"{namespaceName}.{string.Join("+", names)}";
    }

    private static string _GetMetadataSegment(INamedTypeSymbol symbol)
    {
        if (symbol.TypeArguments.Length == 0)
        {
            return symbol.MetadataName;
        }

        return $"{symbol.MetadataName}[{string.Join(",", symbol.TypeArguments.Select(_GetTypeIdentity))}]";
    }

    private static string _GetTypeIdentity(ITypeSymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol namedType => $"{namedType.ContainingAssembly.Name}:{_GetMetadataName(namedType)}",
            IArrayTypeSymbol arrayType =>
                $"{_GetTypeIdentity(arrayType.ElementType)}[{new string(',', arrayType.Rank - 1)}]",
            _ => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        };
}
