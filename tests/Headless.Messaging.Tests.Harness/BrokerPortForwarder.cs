// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Containers;

namespace Tests;

/// <summary>
/// Gives a broker container a host address that survives a stop and start: a loopback port this process holds for the
/// forwarder's whole life, relaying each connection to the port Docker published for the container's current start.
/// </summary>
/// <remarks>
/// <para>
/// Publishing the container on a pre-chosen host port cannot do this safely. A port picked free and released can be
/// taken by another process before Docker binds it, and while the container is stopped its port is free again, with
/// the client under test reconnecting to it the whole time. Docker assigns each start a port of its own instead, and
/// the forwarder's listener never lets go of the address the client holds.
/// </para>
/// <para>
/// While the container is not running, the forwarder accepts a connection and resets it at once, so the client sees
/// the connection fail, as it does through a container runtime's own port forwarder while a broker starts.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class BrokerPortForwarder : IAsyncDisposable
{
    private readonly Func<IContainer> _container;
    private readonly int _containerPort;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<long, Task> _relays = new();
    private readonly Task _accepting;
    private long _nextRelay;
    private int _disposed;

    /// <summary>Starts listening, before the container exists, so its address can go into the container's settings.</summary>
    /// <param name="container">Returns the container to relay to; it is called only when a client connects.</param>
    /// <param name="containerPort">The container port to relay to.</param>
    public BrokerPortForwarder(Func<IContainer> container, int containerPort)
    {
        _container = container;
        _containerPort = containerPort;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = _AcceptAsync(_stopping.Token);
    }

    /// <summary>Gets the host clients connect to, an IPv4 literal because the forwarder listens on IPv4 loopback only.</summary>
    public string Host => "127.0.0.1";

    /// <summary>Gets the port clients connect to; it stays the same across every start of the container.</summary>
    public int Port { get; }

    /// <summary>Gets <see cref="Host"/> and <see cref="Port"/> as <c>host:port</c>.</summary>
    public string Address => $"{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(_relays.Values).ConfigureAwait(false);
        _stopping.Dispose();
    }

    private async Task _AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Socket client;

            try
            {
                client = await _listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
                when (cancellationToken.IsCancellationRequested
                    && e is OperationCanceledException or SocketException or ObjectDisposedException
                )
            {
                return;
            }

            var id = Interlocked.Increment(ref _nextRelay);
            _relays[id] = _RelayAsync(client, id, cancellationToken);
        }
    }

    private async Task _RelayAsync(Socket client, long id, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                await _RelayCoreAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable ERP022 // Dropping the one connection is the whole handling: a client whose connection fails reconnects, as it would against the broker itself.
        catch (Exception e)
            when (e
                    is SocketException
                        or IOException
                        or OperationCanceledException
                        or ObjectDisposedException
                        // The container stopped between the state check and the port lookup.
                        or InvalidOperationException
            ) { }
#pragma warning restore ERP022
        finally
        {
            _relays.TryRemove(id, out _);
        }
    }

    private async Task _RelayCoreAsync(Socket client, CancellationToken cancellationToken)
    {
        var container = _container();

        if (container.State is not TestcontainersStates.Running)
        {
            // A reset rather than a graceful close, so the client fails the connection at once instead of reading an
            // empty reply.
            client.LingerState = new LingerOption(enable: true, seconds: 0);
            return;
        }

        var endpoint = new DnsEndPoint(container.Hostname, container.GetMappedPublicPort(_containerPort));
        using var upstream = new Socket(SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await upstream.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            client.LingerState = new LingerOption(enable: true, seconds: 0);
            return;
        }

        using var relay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clientStream = new NetworkStream(client, ownsSocket: false);
        var upstreamStream = new NetworkStream(upstream, ownsSocket: false);

        await using (clientStream.ConfigureAwait(false))
        await using (upstreamStream.ConfigureAwait(false))
        {
#pragma warning disable CA2025 // False positive: both copies are awaited below, before the streams and the token source are disposed.
            var toBroker = clientStream.CopyToAsync(upstreamStream, relay.Token);
            var toClient = upstreamStream.CopyToAsync(clientStream, relay.Token);
#pragma warning restore CA2025

            // Either side closing ends the connection on both, as a broker that stops closes its client's socket.
            _ = await Task.WhenAny(toBroker, toClient).ConfigureAwait(false);
            await relay.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(toBroker, toClient).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
