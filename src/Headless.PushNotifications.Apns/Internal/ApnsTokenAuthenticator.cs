// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

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

        // Inside the 20-minute limit the source hands back the rejected token itself, and APNs would refuse a
        // byte-identical resend the same way, so the original rejection stands.
        return token.Generation == rejected.Generation ? null : new ApnsCredential(token.Value, token.Generation);
    }
}
