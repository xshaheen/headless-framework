// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Signs every call of the forwarding client with the current <see cref="MessagingForwardingOptions.Key"/>. Only key
/// mode signs: under a host policy the host configures the client to present its own service identity instead.
/// </summary>
/// <remarks>
/// The key itself never leaves the node, and <see cref="MessagingForwardingOptions.PreviousKey"/> is never used to
/// sign: it only widens what this node admits while the deployment rotates its key.
/// </remarks>
internal sealed class SigningForwardingHandler(IOptions<MessagingDashboardOptions> options, TimeProvider time)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var key = options.Value.Forwarding.Key;

        if (key is not null)
        {
            await ForwardingSignature.SignAsync(request, key, time, cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
