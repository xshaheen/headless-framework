// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Headless.LiveQueries.Internal;

/// <summary>What the built-in transports share: negotiation, the response head of a stream, framing and heartbeat.</summary>
internal static class LiveStreamResponse
{
    /// <summary>Returns whether the request's <c>Accept</c> header names <paramref name="mediaType" /> itself.</summary>
    /// <remarks>
    /// A wildcard does not count: a client that accepts anything gets the endpoint's ordinary response, and only a
    /// client that asks for a stream format by name gets a stream.
    /// </remarks>
    public static bool Accepts(HttpContext httpContext, string mediaType)
    {
        foreach (var accepted in httpContext.Request.GetTypedHeaders().Accept)
        {
            if (accepted.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase) && accepted.Quality is not 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets the response head of a stream of <paramref name="contentType" />.</summary>
    public static void Start(HttpContext httpContext, string contentType)
    {
        var response = httpContext.Response;
        response.ContentType = contentType;
        response.Headers.CacheControl = "no-cache, no-store";

        // Tells a buffering reverse proxy to pass each update on as it is written, not when the stream closes.
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    /// <summary>
    /// Writes <paramref name="json" /> on one line. Both built-in formats end a record at a line break, and a
    /// serializer that indents puts line breaks between tokens. Inside a JSON string a line break is always
    /// escaped, so dropping the raw ones changes no value.
    /// </summary>
    public static void WriteOnOneLine(IBufferWriter<byte> writer, ReadOnlySpan<byte> json)
    {
        while (true)
        {
            var lineBreak = json.IndexOfAny((byte)'\n', (byte)'\r');

            if (lineBreak < 0)
            {
                writer.Write(json);

                return;
            }

            writer.Write(json[..lineBreak]);
            json = json[(lineBreak + 1)..];
        }
    }

    /// <summary>
    /// Writes command ids with a comma between them and, when <paramref name="quoted" />, a double quote around
    /// each, which makes them the items of a JSON array. An id is a token of ASCII letters, digits and hyphens, so
    /// it is written as it is.
    /// </summary>
    public static void WriteCommands(IBufferWriter<byte> writer, IReadOnlyList<string> commands, bool quoted)
    {
        for (var index = 0; index < commands.Count; index++)
        {
            if (index > 0)
            {
                writer.Write(","u8);
            }

            if (quoted)
            {
                writer.Write("\""u8);
            }

            Encoding.ASCII.GetBytes(commands[index], writer);

            if (quoted)
            {
                writer.Write("\""u8);
            }
        }
    }

    /// <summary>
    /// Passes each update to <paramref name="writeUpdate" />, and calls <paramref name="writeHeartbeat" /> whenever
    /// <paramref name="heartbeatInterval" /> passes with nothing written.
    /// </summary>
    public static async Task PumpAsync(
        IAsyncEnumerable<LiveUpdate> updates,
        TimeSpan heartbeatInterval,
        Func<LiveUpdate, CancellationToken, ValueTask> writeUpdate,
        Func<CancellationToken, ValueTask> writeHeartbeat,
        CancellationToken cancellationToken
    )
    {
        var enumerator = updates.GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            // Started here, on the request's flow, and only awaited below: the heartbeat must not restart the step.
            var next = enumerator.MoveNextAsync().AsTask();

            while (true)
            {
                // A token of its own: were the wait tied to the stream's token, the end of the stream would finish
                // the wait as well, and a heartbeat could be written to a stream that is over.
                using var idle = new CancellationTokenSource();

                try
                {
                    // Real time, not the application clock: the interval exists to beat the idle timeout of a proxy.
                    var heartbeat = Task.Delay(heartbeatInterval, idle.Token);

                    if (await Task.WhenAny(next, heartbeat).ConfigureAwait(false) != next)
                    {
                        await writeHeartbeat(cancellationToken).ConfigureAwait(false);

                        continue;
                    }
                }
                finally
                {
                    await idle.CancelAsync().ConfigureAwait(false);
                }

                if (!await next.ConfigureAwait(false))
                {
                    return;
                }

                await writeUpdate(enumerator.Current, cancellationToken).ConfigureAwait(false);
                next = enumerator.MoveNextAsync().AsTask();
            }
        }
    }
}
