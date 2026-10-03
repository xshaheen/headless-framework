// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

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
