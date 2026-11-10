// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Forwarding;

/// <summary>Presents <c>X-Test-Peer: {name}</c> on every forwarded call, as a host's client certificate would.</summary>
internal sealed class PeerIdentityHeader(string name) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        request.Headers.TryAddWithoutValidation(PeerIdentityHandler.Header, name);

        return base.SendAsync(request, cancellationToken);
    }
}
