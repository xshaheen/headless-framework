// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Provides authentication credentials for APNs HTTP/2 requests.
/// </summary>
internal interface IApnsAuthenticator
{
    /// <summary>Validates that the current authentication strategy supports the specified push type.</summary>
    /// <param name="pushType">The APNs push type header value.</param>
    /// <param name="paramName">The parameter name to report in exceptions.</param>
    /// <exception cref="ArgumentException">The push type is not supported by this authentication mode.</exception>
    void EnsureSupported(string pushType, string paramName);

    /// <summary>Retrieves credentials for the next request.</summary>
    ValueTask<ApnsCredential> GetCredentialAsync(ApnsOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to refresh credentials following an authentication rejection.
    /// </summary>
    ValueTask<ApnsCredential?> RenewExpiredAsync(
        ApnsOptions options,
        ApnsCredential rejected,
        CancellationToken cancellationToken
    );
}

/// <summary>Represents HTTP authentication credentials for an APNs request.</summary>
/// <param name="BearerToken">The bearer JWT provider token, or <see langword="null"/> when using certificate authentication.</param>
/// <param name="Generation">The mint generation of the token.</param>
internal readonly record struct ApnsCredential(string? BearerToken, long Generation)
{
    public void Apply(HttpRequestMessage message)
    {
        if (BearerToken is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("bearer", BearerToken);
        }
    }
}

/// <summary>Provides token-based authentication using bearer JWT tokens.</summary>
internal sealed class ApnsTokenAuthenticator(ApnsTokenSource tokenSource) : IApnsAuthenticator
{
    public void EnsureSupported(string pushType, string paramName) { }

    public async ValueTask<ApnsCredential> GetCredentialAsync(ApnsOptions options, CancellationToken cancellationToken)
    {
        var token = await tokenSource.GetTokenAsync(options, cancellationToken).ConfigureAwait(false);

        return new ApnsCredential(token.Value, token.Generation);
    }

    public async ValueTask<ApnsCredential?> RenewExpiredAsync(
        ApnsOptions options,
        ApnsCredential rejected,
        CancellationToken cancellationToken
    )
    {
        // The source re-mints once per rejected generation (and never sooner than Apple's 20-minute update limit),
        // so a second rejection is a persistent problem, not a stale token.
        var token = await tokenSource
            .InvalidateAsync(options, rejected.Generation, cancellationToken)
            .ConfigureAwait(false);

        // Inside the 20-minute limit the source hands back the rejected token itself, and APNs would refuse a
        // byte-identical resend the same way, so the original rejection stands.
        return token.Generation == rejected.Generation ? null : new ApnsCredential(token.Value, token.Generation);
    }
}

/// <summary>
/// Certificate mode: the provider certificate is presented during the TLS handshake by the primary handler, so a
/// request carries no <c>authorization</c> header and there is no token to renew.
/// </summary>
internal sealed class ApnsCertificateAuthenticator : IApnsAuthenticator
{
    // Apple documents these push types as token-only, or instructs token authentication for them. Apple does not
    // state certificate support for controls, so it is refused conservatively.
    private static readonly FrozenSet<string> _TokenOnlyPushTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        ApnsPushTypes.Location,
        ApnsPushTypes.FileProvider,
        ApnsPushTypes.LiveActivity,
        ApnsPushTypes.Widgets,
        ApnsPushTypes.Controls
    );

    public static ApnsCertificateAuthenticator Instance { get; } = new();

    private ApnsCertificateAuthenticator() { }

    public void EnsureSupported(string pushType, string paramName)
    {
        Argument.IsNotNull(pushType);

        if (_TokenOnlyPushTypes.Contains(pushType))
        {
            throw new ArgumentException(
                $"APNs push type '{pushType}' requires token authentication and cannot be sent by an instance that authenticates with a certificate.",
                paramName
            );
        }
    }

    public ValueTask<ApnsCredential> GetCredentialAsync(ApnsOptions options, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(default(ApnsCredential));
    }

    public ValueTask<ApnsCredential?> RenewExpiredAsync(
        ApnsOptions options,
        ApnsCredential rejected,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult<ApnsCredential?>(null);
    }
}
