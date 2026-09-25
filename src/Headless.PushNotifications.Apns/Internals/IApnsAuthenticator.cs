// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>The credentials one APNs request carries.</summary>
/// <param name="BearerToken">The provider token for the <c>authorization</c> header, or <see langword="null"/> to send none.</param>
/// <param name="Generation">The provider token's mint generation; 0 when there is no token.</param>
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

/// <summary>How an instance authenticates to APNs, chosen once from its options when the service is built.</summary>
internal interface IApnsAuthenticator
{
    /// <summary>Refuses, before any request, a push type this authentication mode cannot send.</summary>
    /// <param name="pushType">The notification's <c>apns-push-type</c> value.</param>
    /// <param name="paramName">The caller's notification parameter, reported by the exception.</param>
    /// <exception cref="ArgumentException">The mode cannot send <paramref name="pushType"/>.</exception>
    void EnsureSupported(string pushType, string paramName);

    /// <summary>Returns the credentials for the next request.</summary>
    ValueTask<ApnsCredential> GetCredentialAsync(ApnsOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the credentials to retry with after APNs rejected <paramref name="rejected"/> as an expired provider
    /// token, or <see langword="null"/> when the mode has no token to renew and the rejection is final.
    /// </summary>
    ValueTask<ApnsCredential?> RenewExpiredAsync(
        ApnsOptions options,
        ApnsCredential rejected,
        CancellationToken cancellationToken
    );
}

/// <summary>Token mode: a cached ES256 provider token in the <c>authorization: bearer</c> header.</summary>
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

        return new ApnsCredential(token.Value, token.Generation);
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
