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
/// <remarks>
/// The commands a result acknowledges follow the version in the id, as <c>;</c> and the ids separated by
/// <c>,</c>. The id is the one field besides the data that every event parser hands to its caller, and the result
/// and the commands it holds must arrive as one event: a client that learned them apart would show its optimistic
/// result twice or not at all in between. Commands acknowledged without a result are an <c>acknowledged</c> event
/// whose data is a JSON array of the ids; it has no id, so the last event id stays the last result's.
/// </remarks>
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
                    if (update.Kind is LiveUpdateKind.Acknowledged)
                    {
                        writer.Write("event: acknowledged\ndata: ["u8);
                        LiveStreamResponse.WriteCommands(writer, update.AcknowledgedCommands, quoted: true);
                        writer.Write("]\n\n"u8);
                    }
                    else
                    {
                        writer.Write(
                            update.Kind is LiveUpdateKind.Failed ? "event: failure\nid: "u8 : "event: result\nid: "u8
                        );
                        Encoding.UTF8.GetBytes(update.Version, writer);

                        if (update.AcknowledgedCommands.Count > 0)
                        {
                            writer.Write(";"u8);
                            LiveStreamResponse.WriteCommands(writer, update.AcknowledgedCommands, quoted: false);
                        }

                        writer.Write("\ndata: "u8);
                        LiveStreamResponse.WriteOnOneLine(writer, update.Payload.Span);
                        writer.Write("\n\n"u8);
                    }

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
