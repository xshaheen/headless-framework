// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using Headless.IO;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Limits concurrent TCP connections for an APNs HTTP/2 client handler using socket connection permits.
/// </summary>
internal sealed class ApnsConnectionLimiter(int maxConnections) : IDisposable
{
    private readonly SemaphoreSlim _permits = new(maxConnections, maxConnections);

    /// <summary>Disposes underlying synchronization primitives.</summary>
    public void Dispose()
    {
        _permits.Dispose();
    }

    /// <summary>Connects to the endpoint using a socket while enforcing concurrency limits.</summary>
    /// <param name="context">The socket connection context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A connected network stream wrapped to release permits on closure.</returns>
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
