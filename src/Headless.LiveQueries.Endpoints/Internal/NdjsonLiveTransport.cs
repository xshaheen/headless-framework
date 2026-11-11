// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Newline-delimited JSON, for callers that read a plain stream without an event parser. Each line is one object:
/// <c>{"kind":"result","version":"…","value":…}</c> for a result, the same with <c>"failure"</c> for a failed run,
/// and <c>{"kind":"heartbeat"}</c> on an idle stream.
/// </summary>
internal sealed class NdjsonLiveTransport(IOptions<LiveEndpointsOptions> options) : ILiveTransport
{
    internal const string MediaType = "application/x-ndjson";

    private readonly TimeSpan _heartbeatInterval = options.Value.HeartbeatInterval;

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

        await LiveStreamResponse
            .PumpAsync(
                updates,
                _heartbeatInterval,
                async (update, token) =>
                {
                    writer.Write(
                        update.Kind is LiveUpdateKind.Failed
                            ? "{\"kind\":\"failure\",\"version\":\""u8
                            : "{\"kind\":\"result\",\"version\":\""u8
                    );
                    writer.Write(JsonEncodedText.Encode(update.Version).EncodedUtf8Bytes);
                    writer.Write("\",\"value\":"u8);
                    LiveStreamResponse.WriteOnOneLine(writer, update.Payload.Span);
                    writer.Write("}\n"u8);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                },
                async token =>
                {
                    writer.Write("{\"kind\":\"heartbeat\"}\n"u8);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
