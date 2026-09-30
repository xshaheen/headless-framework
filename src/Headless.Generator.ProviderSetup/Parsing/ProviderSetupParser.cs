// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.ProviderSetup.Models;
using Headless.Generator.ProviderSetup.Utilities;
using Headless.Generator.ProviderSetup.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Generator.ProviderSetup.Parsing;

/// <summary>
/// Turns one <c>[GenerateProviderSetup]</c> options class into a <see cref="ProviderSetupResult"/>. All semantic work
/// happens here so later steps only ever see values.
/// </summary>
internal static class ProviderSetupParser
{
    // The closed parameter convention of every templated provider sender, matched by exact type name so a sender
    // outside the convention is rejected with HP004 rather than silently mis-wired.
    private const string _HttpClientFactoryTypeName = "global::System.Net.Http.IHttpClientFactory";
    private const string _OptionsMonitorPrefix = "global::Microsoft.Extensions.Options.IOptionsMonitor<";
    private const string _LoggerPrefix = "global::Microsoft.Extensions.Logging.ILogger<";
    private const string _TimeProviderTypeName = "global::System.TimeProvider";
    private const string _BulkSenderTypeName = "global::Headless.Sms.IBulkSmsSender";

    public static bool IsCandidate(SyntaxNode node, CancellationToken _) => node is TypeDeclarationSyntax;

    public static ProviderSetupResult? Parse(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
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

        var format = SymbolDisplayFormat.FullyQualifiedFormat;
        var location = classDeclaration.Identifier.GetLocation();
        var arguments = context.Attributes[0].ConstructorArguments;

        if (arguments.Length != 4 || arguments.Any(static a => a.Value is not string { Length: > 0 }))
        {
            return _Fail(
                location,
                Descriptors.MissingProviderIdentity,
                classSymbol.Name,
                "useMethodName, httpClientName, senderTypeName, and validatorTypeName"
            );
        }

        var useMethodName = (string)arguments[0].Value!;
        var httpClientName = (string)arguments[1].Value!;
        var senderTypeName = (string)arguments[2].Value!;
        var validatorTypeName = (string)arguments[3].Value!;

        var senderSymbol = ResolveType(classSymbol, senderTypeName);
        var validatorSymbol = ResolveType(classSymbol, validatorTypeName);

        if (senderSymbol is null || senderSymbol.InstanceConstructors.Length != 1)
        {
            return _Fail(
                location,
                Descriptors.SenderNotSingleConstructible,
                senderTypeName,
                "it must resolve in the options namespace and expose exactly one constructor"
            );
        }

        if (validatorSymbol is null)
        {
            return _Fail(
                location,
                Descriptors.MissingProviderIdentity,
                classSymbol.Name,
                $"validator type '{validatorTypeName}' was not found in the options namespace"
            );
        }

        if (!TryReadEffect(classSymbol, out var effect, out var header))
        {
            return _Fail(location, Descriptors.MissingEffectDeclaration, classSymbol.Name);
        }

        var parameters = new List<SenderParameterModel>();
        var required = 0;

        foreach (var parameter in senderSymbol.InstanceConstructors[0].Parameters)
        {
            var typeName = parameter.Type.ToDisplayString(format);
            SenderParameterKind kind;

            if (string.Equals(typeName, _HttpClientFactoryTypeName, StringComparison.Ordinal))
            {
                kind = SenderParameterKind.HttpClientFactory;
                required |= 1;
            }
            else if (
                parameter.Type.SpecialType == SpecialType.System_String
                && string.Equals(parameter.Name, "httpClientName", StringComparison.Ordinal)
            )
            {
                kind = SenderParameterKind.HttpClientNameString;
                required |= 2;
            }
            else if (typeName.StartsWith(_OptionsMonitorPrefix, StringComparison.Ordinal))
            {
                kind = SenderParameterKind.OptionsMonitor;
                required |= 4;
            }
            else if (
                parameter.Type.SpecialType == SpecialType.System_String
                && string.Equals(parameter.Name, "optionsName", StringComparison.Ordinal)
            )
            {
                kind = SenderParameterKind.OptionsNameString;
                required |= 8;
            }
            else if (typeName.StartsWith(_LoggerPrefix, StringComparison.Ordinal))
            {
                kind = SenderParameterKind.Logger;
            }
            else if (string.Equals(typeName, _TimeProviderTypeName, StringComparison.Ordinal))
            {
                kind = SenderParameterKind.TimeProvider;
            }
            else
            {
                return _Fail(location, Descriptors.UnexpectedSenderParameter, senderSymbol.Name, typeName);
            }

            parameters.Add(new SenderParameterModel(kind));
        }

        if (required != 0b1111)
        {
            return _Fail(
                location,
                Descriptors.SenderNotSingleConstructible,
                senderSymbol.Name,
                "its constructor must take IHttpClientFactory, string httpClientName, IOptionsMonitor<TOptions>, and string? optionsName"
            );
        }

        var registersBulk = senderSymbol.AllInterfaces.Any(i =>
            string.Equals(i.ToDisplayString(format), _BulkSenderTypeName, StringComparison.Ordinal)
        );

        var model = new ProviderSetupModel(
            classSymbol.ToDisplayString(format),
            classSymbol.ContainingNamespace.ToDisplayString(),
            validatorSymbol.ToDisplayString(format),
            senderSymbol.ToDisplayString(format),
            useMethodName,
            httpClientName,
            effect,
            header,
            parameters.ToEquatableArray(),
            registersBulk
        );

        return new ProviderSetupResult(model, LocationInfo.From(location), EquatableArray<DiagnosticInfo>.Empty);
    }

    private static ProviderSetupResult _Fail(Location location, DiagnosticDescriptor descriptor, params object?[] args)
    {
        return new ProviderSetupResult(
            null,
            LocationInfo.From(location),
            new[] { DiagnosticInfo.Create(descriptor, location, args) }.ToEquatableArray()
        );
    }

    /// <summary>
    /// Reads the co-applied <c>[OutboundEffect]</c> into the member name the generated code writes. Shared by both
    /// templates so the effect declaration means the same thing wherever it appears.
    /// </summary>
    internal static bool TryReadEffect(INamedTypeSymbol classSymbol, out string effect, out string? header)
    {
        effect = string.Empty;
        header = null;

        var attribute = classSymbol
            .GetAttributes()
            .FirstOrDefault(static a =>
                a.AttributeClass?.ToDisplayString() == GeneratorConstants.OutboundEffectAttributeMetadataName
            );

        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return false;
        }

        // The enum constant arrives as its underlying int; map it back to the member name.
        var constant = attribute.ConstructorArguments[0];
        var member = constant.Type is INamedTypeSymbol enumType
            ? enumType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, constant.Value))
                ?.Name
            : null;

        if (member is null)
        {
            return false;
        }

        effect = $"global::Headless.Http.Effects.OutboundEffect.{member}";
        header = attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string : null;

        return true;
    }

    internal static INamedTypeSymbol? ResolveType(INamedTypeSymbol classSymbol, string name)
    {
        // Unqualified names resolve inside the options class's namespace; qualified names as metadata names.
        var metadataName =
            name.IndexOf(".", StringComparison.Ordinal) < 0
                ? $"{classSymbol.ContainingNamespace.ToDisplayString()}.{name}"
                : name;

        return classSymbol.ContainingAssembly.GetTypeByMetadataName(metadataName);
    }
}
