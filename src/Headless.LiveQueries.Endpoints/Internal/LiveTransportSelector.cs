// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Picks the transport of a request: the transports the host added, in the order it added them, then the built-in
/// ones, so an added transport wins a request both could answer.
/// </summary>
internal sealed class LiveTransportSelector(
    IEnumerable<ILiveTransport> added,
    ServerSentEventsLiveTransport serverSentEvents,
    NdjsonLiveTransport ndjson
)
{
    private readonly ILiveTransport[] _transports = [.. added, serverSentEvents, ndjson];

    /// <summary>Returns the transport that answers the request, or <see langword="null" /> for no stream.</summary>
    public ILiveTransport? Select(HttpContext httpContext)
    {
        foreach (var transport in _transports)
        {
            if (transport.CanHandle(httpContext))
            {
                return transport;
            }
        }

        return null;
    }
}
