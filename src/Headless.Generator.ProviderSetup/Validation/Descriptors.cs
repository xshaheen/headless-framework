// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;

namespace Headless.Generator.ProviderSetup.Validation;

/// <summary>Diagnostic descriptors for the provider-setup generator. ID range HP001–HP019.</summary>
internal static class Descriptors
{
    private const string _Category = "Headless.Generator.ProviderSetup";

    private static DiagnosticDescriptor _Create(string id, string title, string message, string description)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            message,
            _Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description,
            helpLinkUri: $"https://github.com/xshaheen/headless-framework/blob/main/docs/llms/provider-setup.md#{id.ToLowerInvariant()}"
        );
    }

    public static readonly DiagnosticDescriptor MissingProviderIdentity = _Create(
        "HP001",
        "Provider identity arguments are required",
        "The GenerateProviderSetup attribute on {0} is missing required named arguments: {1}",
        "UseMethodName, HttpClientName, and SenderTypeName must all be set so the generated registration surface is complete."
    );

    public static readonly DiagnosticDescriptor SenderNotSingleConstructible = _Create(
        "HP002",
        "Sender type must have exactly one constructor",
        "The sender type {0} cannot drive generation: {1}",
        "The generator emits a factory that news up the sender with its DI-resolved dependencies; it needs exactly one constructor."
    );

    public static readonly DiagnosticDescriptor MissingEffectDeclaration = _Create(
        "HP003",
        "Options class must declare an outbound effect",
        "The options class {0} carries [GenerateProviderSetup] but no [OutboundEffect]; the resilience pipeline is derived from the effect declaration",
        "Add [OutboundEffect(OutboundEffect.Safe | .Idempotent | .Unsafe)] to the options class."
    );

    public static readonly DiagnosticDescriptor UnexpectedSenderParameter = _Create(
        "HP004",
        "Sender constructor parameter is not part of the provider template",
        "The sender {0} has a constructor parameter of type {1}, which the generated factory cannot supply; providers needing it fall back to a hand-written Setup",
        "The template supplies IHttpClientFactory, the client-name string, IOptionsMonitor<TOptions>, the options-name string, ILogger<TSender>, and TimeProvider."
    );

    public static readonly DiagnosticDescriptor DuplicateUseMethod = _Create(
        "HP005",
        "Two attributed options classes declare the same Use{Provider} method",
        "Two options classes in this assembly declare UseMethodName '{0}'; the generated extension members would collide",
        "Give each provider a unique UseMethodName."
    );
}
