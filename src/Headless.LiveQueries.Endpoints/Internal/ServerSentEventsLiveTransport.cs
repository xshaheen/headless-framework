// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Server-sent events, for browsers. A result is a <c>result</c> event and a failed run a <c>failure</c> event,
/// each with the update's version as its id and its JSON on one <c>data:</c> line. An idle stream carries a
/// comment line.
/// </summary>
internal sealed class ServerSentEventsLiveTransport(IOptions<LiveEndpointsOptions> options) : ILiveTransport
{
    internal const string MediaType = "text/event-stream";

    private readonly TimeSpan _heartbeatInterval = options.Value.HeartbeatInterval;

    private readonly byte[] _preamble = Encoding.ASCII.GetBytes(
        string.Create(
            CultureInfo.InvariantCulture,
            $"retry: {(long)options.Value.ReconnectDelay.TotalMilliseconds}\n\n"
        )
    );

    public bool CanHandle(HttpContext httpContext)
    {
        return LiveStreamResponse.Accepts(httpContext, MediaType);
    }

    public async Task WriteAsync(
        HttpContext httpContext,
        IAsyncEnumerable<LiveUpdate> updates,
        CancellationToken cancellationToken
    )
    {
        LiveStreamResponse.Start(httpContext, MediaType);
        var writer = httpContext.Response.BodyWriter;
        writer.Write(_preamble);

        await LiveStreamResponse
            .PumpAsync(
                updates,
                _heartbeatInterval,
                async (update, token) =>
                {
                    writer.Write(
                        update.Kind is LiveUpdateKind.Failed ? "event: failure\nid: "u8 : "event: result\nid: "u8
                    );
                    Encoding.UTF8.GetBytes(update.Version, writer);
                    writer.Write("\ndata: "u8);
                    LiveStreamResponse.WriteOnOneLine(writer, update.Payload.Span);
                    writer.Write("\n\n"u8);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                },
                async token =>
                {
                    writer.Write(": heartbeat\n\n"u8);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
