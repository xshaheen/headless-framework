// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.LiveQueries;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Tests;

/// <summary>
/// A transport written against the public contract alone, as an application would write one: it answers
/// <c>application/x-test-live</c> with one <c>kind|version|acknowledged commands|payload</c> line for each update.
/// </summary>
public sealed class TestLiveTransport : ILiveTransport
{
    public const string MediaType = "application/x-test-live";

    public bool CanHandle(HttpContext httpContext)
    {
        return httpContext
            .Request.GetTypedHeaders()
            .Accept.Any(accepted => accepted.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase));
    }

    public async Task WriteAsync(
        HttpContext httpContext,
        IAsyncEnumerable<LiveUpdate> updates,
        CancellationToken cancellationToken
    )
    {
        httpContext.Response.ContentType = MediaType;
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            var payload = Encoding.UTF8.GetString(update.Payload.Span);
            var commands = string.Join(',', update.AcknowledgedCommands);
            var line = $"{update.Kind}|{update.Version}|{commands}|{payload}\n";
            await httpContext.Response.WriteAsync(line, cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
