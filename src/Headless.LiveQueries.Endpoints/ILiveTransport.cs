// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries;

/// <summary>
/// Carries the updates of one live endpoint to one client. A streaming request is answered by the first transport
/// that accepts it: the transports a host added, in the order it added them, then server-sent events, then
/// newline-delimited JSON.
/// </summary>
/// <remarks>
/// <para>
/// Applications and libraries implement this interface to add a wire format, and register it with
/// <c>AddTransport&lt;TTransport&gt;()</c>; the framework calls it. New members arrive as default interface
/// members, so an existing implementation keeps compiling.
/// </para>
/// <para>
/// A transport is a singleton shared by every stream, so it keeps no per-request state in fields.
/// </para>
/// </remarks>
[PublicAPI]
public interface ILiveTransport
{
    /// <summary>Returns whether this transport answers <paramref name="httpContext" />.</summary>
    /// <param name="httpContext">A request to a live endpoint. Nothing has been written to its response.</param>
    /// <returns>
    /// <see langword="true" /> to answer the request with a stream. When no transport accepts it, the endpoint
    /// answers with its ordinary single response.
    /// </returns>
    bool CanHandle(HttpContext httpContext);

    /// <summary>
    /// Writes <paramref name="updates" /> to the response until the sequence ends or
    /// <paramref name="cancellationToken" /> is cancelled.
    /// </summary>
    /// <param name="httpContext">The request this transport accepted. The transport starts its response.</param>
    /// <param name="updates">
    /// The endpoint's updates, starting with its first result, which is already available. The sequence ends after
    /// a <see cref="LiveUpdateKind.Failed" /> update. Enumerate it once, and await each step from this method's own
    /// execution flow: every re-run of the endpoint happens inside that step and must see the request's ambient
    /// state, such as its tenant.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the client disconnects or the host stops.</param>
    /// <returns>A task that completes when the stream has ended.</returns>
    Task WriteAsync(HttpContext httpContext, IAsyncEnumerable<LiveUpdate> updates, CancellationToken cancellationToken);
}
