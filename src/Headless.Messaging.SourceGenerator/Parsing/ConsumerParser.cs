// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SourceGenerator.Models;
using Headless.Messaging.SourceGenerator.Utilities;
using Headless.Messaging.SourceGenerator.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Messaging.SourceGenerator.Parsing;

/// <summary>
/// Turns one <c>[BusConsumer]</c> or <c>[QueueConsumer]</c> class into a <see cref="ConsumerResult"/>. All semantic
/// work for a consumer happens here, so the result is the only thing a later step sees.
/// </summary>
internal static class ConsumerParser
{
    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax or RecordDeclarationSyntax;

    public static ConsumerResult? ParseBus(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    ) => _Parse(context, ConsumerLane.Bus, cancellationToken);

    public static ConsumerResult? ParseQueue(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    ) => _Parse(context, ConsumerLane.Queue, cancellationToken);

    private static ConsumerResult? _Parse(
        GeneratorAttributeSyntaxContext context,
        ConsumerLane lane,
        CancellationToken cancellationToken
    )
    {
        if (
            context.TargetNode is not TypeDeclarationSyntax declaration
            || context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } classSymbol
            || context.Attributes.IsEmpty
        )
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var diagnostics = new List<DiagnosticInfo>();
        var classIdentifier = declaration.Identifier;
        var attributeLocation = attribute.ApplicationSyntaxReference is { } syntaxReference
            ? syntaxReference.SyntaxTree.GetLocation(syntaxReference.Span)
            : classIdentifier.GetLocation();
        var typeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        ConsumerValidator.ValidateClass(classSymbol, classIdentifier, diagnostics);

        var messageTypes = _ResolveMessageTypes(compilation, classSymbol);
        if (messageTypes.Count == 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MissingConsumeInterface,
                    attributeLocation,
                    classSymbol.Name
                )
            );
        }

        foreach (var messageType in messageTypes.Where(type => !ConsumerValidator.IsAccessible(type)))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InaccessibleConsumer,
                    attributeLocation,
                    messageType.ToDisplayString(),
                    classSymbol.Name
                )
            );
        }

        var values = ConsumerAttributeValues.Read(attribute);
        ConsumerValidator.ValidateAttribute(
            compilation,
            values.Identity,
            values.Policy,
            classSymbol.Name,
            attributeLocation,
            diagnostics
        );

        // The hook only runs for a process-local subscription, so on any other consumer it is dead code.
        var everyInstance = lane == ConsumerLane.Bus && values.EveryInstance;
        if (
            !everyInstance
            && _Implements(compilation, classSymbol, SourceGeneratorConstants.SubscriptionHookMetadataName)
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.SubscriptionHookWithoutEveryInstance,
                    attributeLocation,
                    classSymbol.Name
                )
            );
        }

        var messageTypeNames = messageTypes
            .Select(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToEquatableArray();

        var consumer = diagnostics.Exists(diagnostic =>
            diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Error
        )
            ? null
            : new ConsumerModel(
                typeName,
                classSymbol.ToDisplayString(),
                lane,
                values.Identity!,
                everyInstance,
                values.Policy?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                messageTypeNames,
                _GetDisposal(compilation, classSymbol),
                _Implements(compilation, classSymbol, SourceGeneratorConstants.ConsumerLifecycleMetadataName)
            );

        return new(
            consumer,
            lane,
            typeName,
            values.Identity,
            messageTypeNames,
            LocationInfo.From(attributeLocation),
            diagnostics.ToEquatableArray()
        );
    }

    /// <summary>The <c>T</c> of every <c>IConsume&lt;T&gt;</c> the class implements, directly or through a base type.</summary>
    private static List<ITypeSymbol> _ResolveMessageTypes(Compilation compilation, INamedTypeSymbol classSymbol)
    {
        var consume = compilation.GetTypeByMetadataName(SourceGeneratorConstants.ConsumeInterfaceMetadataName);
        if (consume is null)
        {
            return [];
        }

        return
        [
            .. classSymbol
                .AllInterfaces.Where(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, consume))
                .Select(type => type.TypeArguments[0]),
        ];
    }

    private static ConsumerDisposal _GetDisposal(Compilation compilation, INamedTypeSymbol classSymbol)
    {
        if (_Implements(compilation, classSymbol, "System.IAsyncDisposable"))
        {
            return ConsumerDisposal.Async;
        }

        return _Implements(compilation, classSymbol, "System.IDisposable")
            ? ConsumerDisposal.Sync
            : ConsumerDisposal.None;
    }

    private static bool _Implements(Compilation compilation, INamedTypeSymbol classSymbol, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        return type is not null && classSymbol.AllInterfaces.Contains(type, SymbolEqualityComparer.Default);
    }

    /// <summary>The values of one consumer attribute application, read without interpreting them.</summary>
    private readonly record struct ConsumerAttributeValues(string? Identity, bool EveryInstance, ITypeSymbol? Policy)
    {
        public static ConsumerAttributeValues Read(AttributeData attribute)
        {
            var identity =
                attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
            var everyInstance = false;
            ITypeSymbol? policy = null;

            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "EveryInstance" when named.Value.Value is bool value:
                        everyInstance = value;
                        break;
                    case "Policy":
                        policy = named.Value.Value as ITypeSymbol;
                        break;
                }
            }

            return new(identity, everyInstance, policy);
        }
    }
}
