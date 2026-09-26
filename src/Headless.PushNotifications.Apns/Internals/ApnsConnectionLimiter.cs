// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using Headless.IO;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Bounds how many simultaneous TCP connections one APNs instance opens, through a
/// <see cref="SocketsHttpHandler.ConnectCallback"/> permit. The runtime's <c>MaxConnectionsPerServer</c> cannot
/// provide the bound: it is enforced only for HTTP/1.1, while APNs speaks HTTP/2.
/// </summary>
/// <remarks>
/// <para>
/// A dial takes a permit from a per-instance <see cref="SemaphoreSlim"/> and returns the connected stream wrapped in
/// an <see cref="ActionableStream"/> whose one-shot dispose action releases the permit. Once acquired, the permit is
/// held until the wrapper takes ownership — the pool disposes the stream when the connection ends — or the dial
/// faults, which releases it before the exception escapes. Both paths release it exactly once.
/// </para>
/// <para>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> exceptions are swallowed by the connection pool's HTTP/2
/// injection path, so a permit stranded by an exception would permanently shrink the pool: a faulting dial must
/// release its own permit before the exception escapes.
/// </para>
/// <para>
/// One limiter per named or default APNs instance, because the semaphore is the instance's connection budget. Its
/// lifetime is the primary handler's: the callback is the only path that can reach the semaphore, so once the
/// handler is disposed no dial can wait on it again.
/// </para>
/// </remarks>
internal sealed class ApnsConnectionLimiter(int maxConnections) : IDisposable
{
    private readonly SemaphoreSlim _permits = new(maxConnections, maxConnections);

    /// <summary>Releases the permit semaphore. Called when the handler that dials through it is disposed.</summary>
    public void Dispose()
    {
        _permits.Dispose();
    }

    /// <summary>The callback wired into <see cref="SocketsHttpHandler.ConnectCallback"/>.</summary>
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        await _permits.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The default dial the handler would have made: a no-delay TCP socket to the requested endpoint. APNs
            // on port 2197 and a proxy's endpoint both arrive here unchanged, so the bound covers them too.
#pragma warning disable CA2000 // False positive: the socket's ownership transfers to the NetworkStream (ownsSocket) and then to the returned wrapper; every fault path before that disposes it.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
#pragma warning restore CA2000

            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            // Ownership of the socket transfers to the stream, the stream to the wrapper, and the permit releases
            // exactly once when the pool disposes that wrapper at the end of the connection.
            var stream = new NetworkStream(socket, ownsSocket: true);

            return new ActionableStream(stream, _TryRelease);
        }
        catch
        {
            // A faulted dial owns its permit, because no wrapper was created to release it; the pool swallows the
            // exception, so releasing here is the only path that returns it.
            _TryRelease();
            throw;
        }
    }

    private void _TryRelease()
    {
        // A disposed semaphore means the handler is gone and no dial can be waiting on a permit anymore; releasing
        // into it would throw past a connection teardown, so it is a no-op instead.
        try
        {
            _permits.Release();
        }
        catch (ObjectDisposedException) { }
    }
}
