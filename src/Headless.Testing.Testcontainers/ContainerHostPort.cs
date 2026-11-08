// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net.Sockets;
using DotNet.Testcontainers.Containers;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Checks a started container through its host-mapped port, and restarts it once when nothing answers there.
/// </summary>
/// <remarks>
/// A desktop Docker engine runs in a VM and picks the host port there, then forwards it to the host. When the host
/// already holds that port the forward fails, and the only trace is in the engine's own log, so the container is healthy
/// and every connection to it is refused. A wait strategy cannot recover from this because the port stays dead for the
/// container's whole run. A restart makes Docker pick another port.
/// <para>
/// A restart keeps the container's filesystem, so it suits only a module that reads the mapped port at connection time.
/// Kafka writes the mapped port into a startup script once and keeps advertising the dead port after a restart, so its
/// fixture does not use this check.
/// </para>
/// <para>
/// The check proves that something on the host accepts the connection, not that this container does. A forward that
/// failed because a client socket held the port is refused and caught here; one that failed because another listener
/// held the port reaches that listener and passes.
/// </para>
/// </remarks>
internal static class ContainerHostPort
{
    private static readonly TimeSpan _DefaultReachableTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _AttemptTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Returns once <paramref name="containerPort"/> accepts a connection from the host.</summary>
    /// <exception cref="InvalidOperationException">The port stayed closed after one restart.</exception>
    public static async ValueTask EnsureReachableAsync(
        IContainer container,
        int containerPort,
        TimeSpan? reachableTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var timeout = reachableTimeout ?? _DefaultReachableTimeout;

        if (await _WaitUntilReachableAsync(container, containerPort, timeout, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var deadPort = container.GetMappedPublicPort(containerPort);
        await container.StopAsync(cancellationToken).ConfigureAwait(false);
        await container.StartAsync(cancellationToken).ConfigureAwait(false);

        if (await _WaitUntilReachableAsync(container, containerPort, timeout, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var restartedPort = container.GetMappedPublicPort(containerPort);
        throw new InvalidOperationException(
            $"Container port {containerPort.ToString(CultureInfo.InvariantCulture)} of {container.Image.FullName} "
                + $"refused host connections on {container.Hostname}:{deadPort.ToString(CultureInfo.InvariantCulture)} "
                + $"and, after a restart, on {container.Hostname}:{restartedPort.ToString(CultureInfo.InvariantCulture)}. "
                + "Check the Docker engine's log for a failed port forward."
        );
    }

    private static async ValueTask<bool> _WaitUntilReachableAsync(
        IContainer container,
        int containerPort,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var host = container.Hostname;
        var port = container.GetMappedPublicPort(containerPort);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        while (!deadline.IsCancellationRequested)
        {
            if (await _TryConnectAsync(host, port, deadline.Token).ConfigureAwait(false))
            {
                return true;
            }

            try
            {
                await Task.Delay(_PollInterval, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return false;
    }

    private static async ValueTask<bool> _TryConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_AttemptTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, attempt.Token).ConfigureAwait(false);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // The caller's loop tells its own deadline from the test's cancellation.
            return false;
        }
    }
}
