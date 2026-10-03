// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

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
    /// token, or <see langword="null"/> when the rejection is final: the mode has no token to renew, or no newer
    /// token could be minted yet.
    /// </summary>
    ValueTask<ApnsCredential?> RenewExpiredAsync(
        ApnsOptions options,
        ApnsCredential rejected,
        CancellationToken cancellationToken
    );
}
