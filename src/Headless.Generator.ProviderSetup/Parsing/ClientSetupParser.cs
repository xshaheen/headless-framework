// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.ProviderSetup.Models;
using Headless.Generator.ProviderSetup.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Generator.ProviderSetup.Parsing;

/// <summary>Turns one <c>[GenerateClientSetup]</c> options class into a <see cref="ClientSetupResult"/>.</summary>
internal static class ClientSetupParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) => node is TypeDeclarationSyntax;

    public static ClientSetupResult? Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (
            context.TargetNode is not TypeDeclarationSyntax classDeclaration
            || context.TargetSymbol is not INamedTypeSymbol classSymbol
            || context.Attributes.IsEmpty
        )
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var location = classDeclaration.Identifier.GetLocation();
        var arguments = context.Attributes[0].ConstructorArguments;

        if (arguments.Length != 3 || arguments.Any(static a => a.Value is not string { Length: > 0 }))
        {
            return _Fail(
                location,
                Descriptors.MissingProviderIdentity,
                classSymbol.Name,
                "addMethodName, httpClientName, and validatorTypeName"
            );
        }

        var validatorTypeName = (string)arguments[2].Value!;
        var validatorSymbol = ProviderSetupParser.ResolveType(classSymbol, validatorTypeName);

        if (validatorSymbol is null)
        {
            return _Fail(
                location,
                Descriptors.MissingProviderIdentity,
                classSymbol.Name,
                $"validator type '{validatorTypeName}' was not found"
            );
        }

        if (!ProviderSetupParser.TryReadEffect(classSymbol, out var effect, out var header))
        {
            return _Fail(location, Descriptors.MissingEffectDeclaration, classSymbol.Name);
        }

        var format = SymbolDisplayFormat.FullyQualifiedFormat;
        var model = new ClientSetupModel(
            classSymbol.ToDisplayString(format),
            classSymbol.ContainingNamespace.ToDisplayString(),
            // The registration surface lives in the package root namespace (namespace policy tier 2), which is
            // the assembly name for every single-backend client package.
            classSymbol.ContainingAssembly.Name,
            validatorSymbol.ToDisplayString(format),
            (string)arguments[0].Value!,
            (string)arguments[1].Value!,
            effect,
            header
        );

        return new ClientSetupResult(model, EquatableArray<DiagnosticInfo>.Empty);
    }

    private static ClientSetupResult _Fail(Location location, DiagnosticDescriptor descriptor, params object?[] args)
    {
        return new ClientSetupResult(
            Model: null,
            new[] { DiagnosticInfo.Create(descriptor, location, args) }.ToEquatableArray()
        );
    }
}
