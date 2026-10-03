// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

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
