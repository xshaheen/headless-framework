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
        var responders = _ResolveResponders(compilation, classSymbol);
        if (messageTypes.Count == 0 && responders.Count == 0)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MissingConsumeInterface,
                    attributeLocation,
                    classSymbol.Name
                )
            );
        }

        _ValidateResponders(lane, classSymbol, messageTypes, responders, attributeLocation, diagnostics);

        var handledTypes = messageTypes
            .Concat(responders.SelectMany(responder => new[] { responder.Request, responder.Response }))
            .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var messageType in handledTypes.Where(type => !ConsumerValidator.IsAccessible(type)))
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
        ConsumerValidator.ValidateAttribute(values.Identity, classSymbol.Name, attributeLocation, diagnostics);

        // The hook only runs for a process-local subscription, so on any other consumer it is dead code.
        var everyInstance = lane == ConsumerLane.Bus && values.EveryInstance;
        var implementsSubscriptionHook = HandlerSymbols.Implements(
            compilation,
            classSymbol,
            SourceGeneratorConstants.SubscriptionHookMetadataName
        );
        if (!everyInstance && implementsSubscriptionHook)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.SubscriptionHookWithoutEveryInstance,
                    attributeLocation,
                    classSymbol.Name
                )
            );
        }

        var messageTypeNames = _SortedNames(messageTypes);
        var responderModels = responders
            .Select(responder => new ResponderModel(_Name(responder.Request), _Name(responder.Response)))
            .Distinct()
            .OrderBy(responder => responder.RequestTypeName, StringComparer.Ordinal)
            .ThenBy(responder => responder.ResponseTypeName, StringComparer.Ordinal)
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
                messageTypeNames,
                responderModels,
                HandlerSymbols.GetDisposal(compilation, classSymbol),
                HandlerSymbols.Implements(
                    compilation,
                    classSymbol,
                    SourceGeneratorConstants.ConsumerLifecycleMetadataName
                ),
                everyInstance && implementsSubscriptionHook
            );

        return new(
            consumer,
            lane,
            typeName,
            values.Identity,
            _SortedNames(messageTypes.Concat(responders.Select(responder => responder.Request))),
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

    /// <summary>
    /// The request and response of every <c>IRespond&lt;TRequest, TResponse&gt;</c> the class implements, directly or through
    /// a base type.
    /// </summary>
    private static List<(ITypeSymbol Request, ITypeSymbol Response)> _ResolveResponders(
        Compilation compilation,
        INamedTypeSymbol classSymbol
    )
    {
        var respond = compilation.GetTypeByMetadataName(SourceGeneratorConstants.RespondInterfaceMetadataName);
        if (respond is null)
        {
            return [];
        }

        return
        [
            .. classSymbol
                .AllInterfaces.Where(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, respond))
                .Select(type => (type.TypeArguments[0], type.TypeArguments[1])),
        ];
    }

    /// <summary>
    /// The per-class responder rules: a responder answers one caller, so it is a Queue consumer; a message reaches a class
    /// either as a consumed message or as a request, never both; and a request has one response type.
    /// </summary>
    private static void _ValidateResponders(
        ConsumerLane lane,
        INamedTypeSymbol classSymbol,
        List<ITypeSymbol> messageTypes,
        List<(ITypeSymbol Request, ITypeSymbol Response)> responders,
        Location attributeLocation,
        List<DiagnosticInfo> diagnostics
    )
    {
        if (responders.Count == 0)
        {
            return;
        }

        if (lane == ConsumerLane.Bus)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.ResponderOnBusLane, attributeLocation, classSymbol.Name)
            );
        }

        var consumed = new HashSet<string>(messageTypes.Select(_Name), StringComparer.Ordinal);
        foreach (
            var request in responders
                .Select(responder => _Name(responder.Request))
                .Distinct(StringComparer.Ordinal)
                .Where(consumed.Contains)
                .OrderBy(name => name, StringComparer.Ordinal)
        )
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.ConsumerAndResponderForOneMessage,
                    attributeLocation,
                    classSymbol.Name,
                    _DisplayName(request)
                )
            );
        }

        foreach (
            var group in responders
                .GroupBy(responder => _Name(responder.Request), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            var responses = group
                .Select(responder => _Name(responder.Response))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(_DisplayName)
                .ToList();
            if (responses.Count > 1)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.MultipleResponseTypes,
                        attributeLocation,
                        classSymbol.Name,
                        _DisplayName(group.Key),
                        string.Join(", ", responses)
                    )
                );
            }
        }
    }

    private static string _Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string _DisplayName(string fullyQualifiedName) =>
        fullyQualifiedName.Replace("global::", string.Empty);

    private static EquatableArray<string> _SortedNames(IEnumerable<ITypeSymbol> types) =>
        types
            .Select(_Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToEquatableArray();

    /// <summary>The values of one consumer attribute application, read without interpreting them.</summary>
    private readonly record struct ConsumerAttributeValues(string? Identity, bool EveryInstance)
    {
        public static ConsumerAttributeValues Read(AttributeData attribute)
        {
            var identity =
                attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
            var everyInstance = false;

            foreach (var named in attribute.NamedArguments)
            {
                if (
                    string.Equals(named.Key, "EveryInstance", StringComparison.Ordinal)
                    && named.Value.Value is bool value
                )
                {
                    everyInstance = value;
                }
            }

            return new(identity, everyInstance);
        }
    }
}
