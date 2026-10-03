// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator.Models;
using Headless.Messaging.SourceGenerator.Utilities;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Messaging.SourceGenerator.Parsing;

/// <summary>
/// Finds <c>IRequestClient.RequestAsync&lt;TRequest, TResponse&gt;</c> calls, and reads the responders that referenced
/// assemblies publish through generated <c>ResponderMetadataAttribute</c>s.
/// </summary>
internal static class RequestCallParser
{
    /// <summary>
    /// A syntax-only filter: a call of a generic member named <c>RequestAsync</c> with two type arguments, through
    /// <c>client.RequestAsync&lt;…&gt;</c> or <c>client?.RequestAsync&lt;…&gt;</c>. Every call spells both type
    /// arguments, because <c>TResponse</c> appears in no parameter and so cannot be inferred.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is InvocationExpressionSyntax invocation
        && _GenericName(invocation) is { } name
        && string.Equals(
            name.Identifier.ValueText,
            SourceGeneratorConstants.RequestMethodName,
            StringComparison.Ordinal
        )
        && name.TypeArgumentList.Arguments.Count == 2;

    public static RequestCallModel? Parse(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (
            context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
                is not IMethodSymbol { TypeArguments.Length: 2 } method
            || !string.Equals(
                method.ContainingType?.OriginalDefinition.ToDisplayString(),
                SourceGeneratorConstants.RequestClientMetadataName,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        // A generic helper passes its own type parameters through, so only a call with concrete types names a pair.
        var request = method.TypeArguments[0];
        var response = method.TypeArguments[1];
        if (!_IsConcrete(request) || !_IsConcrete(response))
        {
            return null;
        }

        return new RequestCallModel(
            _Name(request),
            _Name(response),
            LocationInfo.From(_GenericName(invocation)!.GetLocation())
        );
    }

    /// <summary>
    /// Reads the responders that referenced assemblies declare in generated metadata, as distinct pairs in ordinal
    /// order.
    /// </summary>
    public static EquatableArray<ResponderModel> GetReferencedResponders(
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var responders = new HashSet<ResponderModel>();
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
                        SourceGeneratorConstants.ResponderMetadataAttributeName,
                        StringComparison.Ordinal
                    )
                    && attribute.ConstructorArguments.Length == 2
                    && attribute.ConstructorArguments[0].Value is ITypeSymbol request
                    && attribute.ConstructorArguments[1].Value is ITypeSymbol response
                )
                {
                    responders.Add(new ResponderModel(_Name(request), _Name(response)));
                }
            }
        }

        return responders
            .OrderBy(responder => responder.RequestTypeName, StringComparer.Ordinal)
            .ThenBy(responder => responder.ResponseTypeName, StringComparer.Ordinal)
            .ToEquatableArray();
    }

    private static GenericNameSyntax? _GenericName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax name } => name,
            MemberBindingExpressionSyntax { Name: GenericNameSyntax name } => name,
            _ => null,
        };

    private static bool _IsConcrete(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => false,
            IArrayTypeSymbol array => _IsConcrete(array.ElementType),
            INamedTypeSymbol named => named.TypeKind != TypeKind.Error && named.TypeArguments.All(_IsConcrete),
            _ => false,
        };

    private static string _Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
