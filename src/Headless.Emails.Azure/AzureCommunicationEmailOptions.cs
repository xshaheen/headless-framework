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
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return "AzureCommunicationEmail: connection-string auth";
        }

        if (Endpoint is not null && TokenCredential is not null)
        {
            return $"AzureCommunicationEmail: {Endpoint} (token-credential auth)";
        }

        if (Endpoint is not null && !string.IsNullOrWhiteSpace(AccessKey))
        {
            return $"AzureCommunicationEmail: {Endpoint} (access-key auth)";
        }

        return "AzureCommunicationEmail: <unconfigured>";
    }
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
        var connectionStringMode = !string.IsNullOrWhiteSpace(options.ConnectionString);
        var accessKeyMode = options.Endpoint is not null && !string.IsNullOrWhiteSpace(options.AccessKey);
        var tokenCredentialMode = options.Endpoint is not null && options.TokenCredential is not null;

        var configuredModes = (connectionStringMode ? 1 : 0) + (accessKeyMode ? 1 : 0) + (tokenCredentialMode ? 1 : 0);

        return configuredModes == 1;
    }
}
