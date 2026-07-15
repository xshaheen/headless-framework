// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Generators;

internal static class MiddlewareRegistrationCollector
{
    public static bool IsMiddlewareAttribute(AttributeData attribute) => _TryGetMiddleware(attribute, out _, out _);

    public static IEnumerable<MiddlewareRegistrationInfo> Collect(
        Compilation compilation,
        IEnumerable<string> ownFunctionNames,
        ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
        SourceProductionContext context
    )
    {
        var ownFunctions = new HashSet<string>(ownFunctionNames, StringComparer.Ordinal);
        var referencedFunctions = _GetReferencedFunctionNames(compilation);
        var candidates = new List<MiddlewareDeclaration>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!_TryGetMiddleware(attribute, out var middlewareType, out var isSchedule))
            {
                continue;
            }

            var target = _GetNamedString(attribute, "Function");
            var location = _GetAttributeLocation(attribute);
            if (target is not null && ownFunctions.Contains(target))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.LocalAssemblyMiddlewareTarget, location, target)
                );
                continue;
            }

            if (target is not null && !referencedFunctions.Contains(target))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.UnknownMiddlewareTarget, location, target)
                );
                continue;
            }

            _AddCandidate(compilation, attribute, middlewareType, isSchedule, target, location, candidates, context);
        }

        foreach (var (_, method) in methodPairs)
        {
            var methodSymbol = compilation.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method);
            if (methodSymbol is null)
            {
                continue;
            }

            var jobFunction = methodSymbol.GetAttributes().FirstOrDefault(_IsJobFunctionAttribute);
            foreach (var attribute in methodSymbol.GetAttributes())
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
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagnosticDescriptors.MethodMiddlewareFunctionTarget, location)
                    );
                    continue;
                }

                if (jobFunction is null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagnosticDescriptors.MethodMiddlewareRequiresJobFunction, location)
                    );
                    continue;
                }

                if (
                    jobFunction.ConstructorArguments.Length == 0
                    || jobFunction.ConstructorArguments[0].Value is not string target
                    || string.IsNullOrWhiteSpace(target)
                )
                {
                    continue;
                }

                _AddCandidate(
                    compilation,
                    attribute,
                    middlewareType,
                    isSchedule,
                    target,
                    location,
                    candidates,
                    context
                );
            }
        }

        var declarations = new HashSet<(bool IsSchedule, string? Function, int Priority, string Identity)>();
        foreach (
            var candidate in candidates
                .OrderBy(x => x.Registration.IsSchedule ? 0 : 1)
                .ThenBy(x => x.Registration.Function, StringComparer.Ordinal)
                .ThenBy(x => x.Registration.Priority)
                .ThenBy(x => x.Registration.Identity, StringComparer.Ordinal)
                .ThenBy(x => x.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(x => x.Location.SourceSpan.Start)
        )
        {
            var registration = candidate.Registration;
            var declarationKey = (
                registration.IsSchedule,
                registration.Function,
                registration.Priority,
                registration.Identity
            );
            if (!declarations.Add(declarationKey))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateMiddleware,
                        candidate.Location,
                        registration.Identity
                    )
                );
                continue;
            }

            yield return registration;
        }
    }

    private static void _AddCandidate(
        Compilation compilation,
        AttributeData attribute,
        INamedTypeSymbol middlewareType,
        bool isSchedule,
        string? target,
        Location location,
        ICollection<MiddlewareDeclaration> candidates,
        SourceProductionContext context
    )
    {
        if (!_IsAccessibleFromGeneratedCode(middlewareType))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.InaccessibleMiddlewareType,
                    location,
                    middlewareType.ToDisplayString()
                )
            );
            return;
        }

        if (!_ImplementsMiddlewareInterface(compilation, middlewareType, isSchedule))
        {
            // The generic attribute constraint already reports the actionable compiler diagnostic.
            // Avoid generating a second, misleading dispatch-signature failure for the same declaration.
            return;
        }

        var typeName = middlewareType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var identity = $"{compilation.Assembly.Name}:{_GetMetadataName(middlewareType)}";
        var priority =
            attribute
                .NamedArguments.FirstOrDefault(argument =>
                    string.Equals(argument.Key, "Priority", StringComparison.Ordinal)
                )
                .Value.Value as int?
            ?? 0;
        candidates.Add(new(new(typeName, identity, target, priority, isSchedule), location));
    }

    private static HashSet<string> _GetReferencedFunctionNames(Compilation compilation)
    {
        var functions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (
                var descriptor in assembly
                    .GetAttributes()
                    .Where(attribute =>
                        string.Equals(
                            attribute.AttributeClass?.ToDisplayString(),
                            "Headless.Jobs.JobFunctionDescriptorMetadataAttribute",
                            StringComparison.Ordinal
                        )
                    )
            )
            {
                if (
                    descriptor.ConstructorArguments.Length == 1
                    && descriptor.ConstructorArguments[0].Value is string name
                )
                {
                    functions.Add(name);
                }
            }
        }

        return functions;
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

    private static bool _IsJobFunctionAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.Name,
            SourceGeneratorConstants.JobFunctionAttributeName,
            StringComparison.Ordinal
        );

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

    private static Location _GetAttributeLocation(AttributeData attribute)
    {
        var syntaxReference = attribute.ApplicationSyntaxReference;
        return syntaxReference is null ? Location.None : syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span);
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

    private sealed class MiddlewareDeclaration(MiddlewareRegistrationInfo registration, Location location)
    {
        public MiddlewareRegistrationInfo Registration { get; } = registration;
        public Location Location { get; } = location;
    }
}
