// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using FluentValidation;

namespace Headless.Emails.Azure;

/// <summary>
/// Configuration options for the Azure Communication Services email sender.
/// </summary>
/// <remarks>
/// Exactly one authentication mode must be configured:
/// <list type="bullet">
/// <item><description><see cref="ConnectionString"/> — the resource connection string.</description></item>
/// <item><description><see cref="Endpoint"/> + <see cref="AccessKey"/> — the resource endpoint and an access key.</description></item>
/// <item><description><see cref="Endpoint"/> + <see cref="TokenCredential"/> — the resource endpoint and a
/// Microsoft Entra ID credential (for example <c>DefaultAzureCredential</c>) for managed-identity auth.</description></item>
/// </list>
/// </remarks>
[PublicAPI]
public sealed class AzureCommunicationEmailOptions
{
    /// <summary>
    /// The Communication Services resource connection string. When set, <see cref="Endpoint"/>,
    /// <see cref="AccessKey"/>, and <see cref="TokenCredential"/> must be left unset.
    /// Store in user-secrets or a key vault — do not commit to source control.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The Communication Services resource endpoint (for example
    /// <c>https://my-resource.communication.azure.com</c>). Pair with either <see cref="AccessKey"/>
    /// or <see cref="TokenCredential"/>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The Communication Services access key, paired with <see cref="Endpoint"/>.
    /// Store in user-secrets or a key vault — do not commit to source control.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// A Microsoft Entra ID credential (for example <c>DefaultAzureCredential</c>), paired with
    /// <see cref="Endpoint"/>. Supply your own credential via the <c>UseAzure</c> delegate overload —
    /// it cannot be bound from configuration.
    /// </summary>
    public TokenCredential? TokenCredential { get; set; }

    /// <summary>Returns a diagnostics-safe description that never includes the connection string or access key.</summary>
    public override string ToString()
    {
        return ResolveAuthMode() switch
        {
            AzureCommunicationEmailAuthMode.ConnectionString => "AzureCommunicationEmail: connection-string auth",
            AzureCommunicationEmailAuthMode.TokenCredential =>
                $"AzureCommunicationEmail: {Endpoint} (token-credential auth)",
            AzureCommunicationEmailAuthMode.AccessKey => $"AzureCommunicationEmail: {Endpoint} (access-key auth)",
            _ => "AzureCommunicationEmail: <unconfigured>",
        };
    }

    /// <summary>
    /// Resolves which single authentication mode these options describe. Returns
    /// <see cref="AzureCommunicationEmailAuthMode.Unconfigured"/> when none is set and
    /// <see cref="AzureCommunicationEmailAuthMode.Ambiguous"/> when more than one is set — the validator
    /// rejects both. Centralizing the precedence here keeps the client factory, <see cref="ToString"/>,
    /// and the validator from drifting when an authentication mode is added.
    /// </summary>
    internal AzureCommunicationEmailAuthMode ResolveAuthMode()
    {
        var connectionStringMode = !string.IsNullOrWhiteSpace(ConnectionString);
        var accessKeyMode = Endpoint is not null && !string.IsNullOrWhiteSpace(AccessKey);
        var tokenCredentialMode = Endpoint is not null && TokenCredential is not null;

        var configuredModes = (connectionStringMode ? 1 : 0) + (accessKeyMode ? 1 : 0) + (tokenCredentialMode ? 1 : 0);

        if (configuredModes == 0)
        {
            return AzureCommunicationEmailAuthMode.Unconfigured;
        }

        if (configuredModes > 1)
        {
            return AzureCommunicationEmailAuthMode.Ambiguous;
        }

        if (connectionStringMode)
        {
            return AzureCommunicationEmailAuthMode.ConnectionString;
        }

        return tokenCredentialMode
            ? AzureCommunicationEmailAuthMode.TokenCredential
            : AzureCommunicationEmailAuthMode.AccessKey;
    }
}

/// <summary>The single authentication mode an <see cref="AzureCommunicationEmailOptions"/> instance describes.</summary>
internal enum AzureCommunicationEmailAuthMode
{
    /// <summary>No authentication mode is configured.</summary>
    Unconfigured,

    /// <summary>More than one authentication mode is configured.</summary>
    Ambiguous,

    /// <summary>The resource connection string.</summary>
    ConnectionString,

    /// <summary>Endpoint paired with an access key.</summary>
    AccessKey,

    /// <summary>Endpoint paired with a Microsoft Entra ID token credential.</summary>
    TokenCredential,
}

[UsedImplicitly]
internal sealed class AzureCommunicationEmailOptionsValidator : AbstractValidator<AzureCommunicationEmailOptions>
{
    public AzureCommunicationEmailOptionsValidator()
    {
        RuleFor(x => x)
            .Must(_HasExactlyOneAuthMode)
            .WithMessage(
                "AzureCommunicationEmailOptions requires exactly one authentication mode: set ConnectionString, "
                    + "or Endpoint + AccessKey, or Endpoint + TokenCredential."
            );
    }

    private static bool _HasExactlyOneAuthMode(AzureCommunicationEmailOptions options)
    {
        return options.ResolveAuthMode()
            is not (AzureCommunicationEmailAuthMode.Unconfigured or AzureCommunicationEmailAuthMode.Ambiguous);
    }
}
