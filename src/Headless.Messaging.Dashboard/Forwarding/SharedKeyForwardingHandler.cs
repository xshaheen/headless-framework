// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Adds the shared forwarding key to every call of the forwarding client. Only the shared-key mode adds it: under a
/// host policy the host configures the client to present its own service identity instead.
/// </summary>
internal sealed class SharedKeyForwardingHandler(IOptions<MessagingDashboardOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var key = options.Value.Forwarding.Key;

        // The client refuses a non-https peer before it sends, so the key only travels over TLS unless the host
        // allowed insecure peers.
        if (key is not null)
        {
            request.Headers.Remove(ForwardingProtocol.KeyHeader);
            request.Headers.TryAddWithoutValidation(ForwardingProtocol.KeyHeader, key);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
