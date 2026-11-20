// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Containers;
using Headless.Checks;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Proves that each published port of a started container reaches that container from the test host, and starts the
/// container again, on other ports, while another listener answers or nothing does.
/// </summary>
/// <remarks>
/// <para>
/// A desktop Docker engine such as OrbStack or Docker Desktop publishes a port as a listener on the host's wildcard
/// address, and Docker hands out host ports in sequence without looking at the host. A host process that already holds the
/// same port on <c>127.0.0.1</c>, such as another test's listener on a port the kernel gave it, is the more specific
/// match: every connection to <c>127.0.0.1</c>, the container's <see cref="IContainer.Hostname"/>, reaches that process
/// for the container's whole run, while the engine still reports the port published. A forward that fails outright is
/// refused for the whole run instead. Each start takes the next ports in the sequence, so a restart leaves both behind.
/// </para>
/// <para>
/// The probe checks something only this container answers, such as a server id or a name the test started it with, not
/// just the protocol: the listener that shadows a port can be another test's forwarder to the same kind of server.
/// <see cref="PublishedPortProbes"/> holds one for each server the Headless fixtures run.
/// </para>
/// <para>
/// A restart keeps the container's filesystem, so it suits only a server that reads its published port at connection
/// time. A server that advertises its published port, such as Kafka through its startup script, keeps advertising the
/// lost port after a restart; verify it with <c>allowRestart: false</c> unless it advertises an address of its own.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PublishedPortCheck
{
    // A start that keeps landing on lost ports means something holds a whole run of them; give up and say so.
    private const int _MaxStarts = 3;

    private static readonly TimeSpan _PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly int[] _containerPorts;
    private readonly PublishedPortProbe _probe;

    /// <summary>Creates a check of one published port.</summary>
    /// <param name="containerPort">The container port whose host mapping clients dial.</param>
    /// <param name="probe">Names who answered on the port.</param>
    public PublishedPortCheck(int containerPort, PublishedPortProbe probe)
        : this([containerPort], probe) { }

    /// <summary>Creates a check of several published ports, each probed the same way.</summary>
    /// <param name="containerPorts">The container ports whose host mappings clients dial.</param>
    /// <param name="probe">Names who answered on a port; it reads the port it was called for.</param>
    public PublishedPortCheck(IEnumerable<int> containerPorts, PublishedPortProbe probe)
    {
        Argument.IsNotNull(containerPorts);
        Argument.IsNotNull(probe);

        _containerPorts = [.. containerPorts];
        Argument.IsNotEmpty(_containerPorts);
        _probe = probe;
    }

    /// <summary>Gets how long a TCP connect may take before the attempt counts as refused.</summary>
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets how long the probe may take on a connection before the attempt counts as no answer yet.</summary>
    internal TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets how long every connect may be refused before the port counts as lost. A runtime forwards a new port within
    /// seconds; one refused this long stays refused for the container's run.
    /// </summary>
    internal TimeSpan RefusedTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets how long connections may be accepted without the server answering before the port counts as lost. It covers
    /// a slow server start under suite load, such as SQL Server recovering its databases, and a listener that accepts and
    /// stays silent.
    /// </summary>
    internal TimeSpan AnswerTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Starts <paramref name="container"/>, then verifies its ports as <see cref="VerifyAsync"/> does.</summary>
    /// <exception cref="InvalidOperationException">Every start of three landed on a lost port.</exception>
    public async Task StartAsync(IContainer container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(container);

        await container.StartAsync(cancellationToken).ConfigureAwait(false);
        await VerifyAsync(container, allowRestart: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns once every port of the started <paramref name="container"/> answers with the container's own reply, and
    /// starts the container again while a port is lost.
    /// </summary>
    /// <param name="container">The started container.</param>
    /// <param name="allowRestart">
    /// Whether a lost port may restart the container. A shared container serves other runs, whose sessions and owner
    /// locks a restart would cut, so its fixture passes <see langword="false"/> and fails instead.
    /// </param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="InvalidOperationException">
    /// A port was lost with no restart allowed, or on every start of three.
    /// </exception>
    public async Task VerifyAsync(
        IContainer container,
        bool allowRestart = true,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(container);

        var lost = new List<string>();

        for (var start = 1; ; start++)
        {
            var loss = await _FindLostPortAsync(container, cancellationToken).ConfigureAwait(false);

            if (loss is null)
            {
                return;
            }

            lost.Add(loss);

            if (!allowRestart)
            {
                throw new InvalidOperationException(
                    $"Container {container.Name} ({container.Image.FullName}) cannot be reached from this host at {loss}, "
                        + "and its fixture does not restart it: a shared container serves other runs (remove it to let the "
                        + "next run create a new one), and a server that advertises its published port would keep "
                        + "advertising the lost one (free the port and run again)."
                );
            }

            if (start == _MaxStarts)
            {
                throw new InvalidOperationException(
                    $"{container.Image.FullName} could not be reached from this host on any of its {_MaxStarts.ToString(CultureInfo.InvariantCulture)} starts: "
                        + $"{string.Join("; ", lost)}. Something on the host holds a whole run of the ports Docker hands out."
                );
            }

            await container.StopAsync(cancellationToken).ConfigureAwait(false);
            await container.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads ASCII from <paramref name="stream"/> until <paramref name="isComplete"/> holds, the peer closes, or
    /// <paramref name="maxBytes"/> arrived, for a probe that must see a whole reply before it names who answered.
    /// </summary>
    /// <returns>What arrived, or <see langword="null"/> when the peer closed before sending a byte.</returns>
    public static async Task<string?> ReadReplyAsync(
        NetworkStream stream,
        Func<string, bool> isComplete,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(stream);
        Argument.IsNotNull(isComplete);

        var buffer = new byte[maxBytes];
        var received = 0;

        while (received < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(received), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            received += read;

            if (isComplete(Encoding.ASCII.GetString(buffer, 0, received)))
            {
                break;
            }
        }

        return received == 0 ? null : Encoding.ASCII.GetString(buffer, 0, received);
    }

    /// <summary>Probes the port once, for a wait strategy that polls on its own.</summary>
    /// <returns>Whether the connect succeeded, and what the probe read.</returns>
    internal static async Task<(bool Connected, PublishedPortReply Reply)> ProbeOnceAsync(
        IContainer container,
        int containerPort,
        PublishedPortProbe probe,
        TimeSpan connectTimeout,
        TimeSpan probeTimeout,
        CancellationToken cancellationToken
    )
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(connectTimeout);
        using var client = new TcpClient();

        try
        {
            await client
                .ConnectAsync(container.Hostname, container.GetMappedPublicPort(containerPort), attempt.Token)
                .ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return (false, PublishedPortReply.NotYet);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, PublishedPortReply.NotYet);
        }

        attempt.CancelAfter(probeTimeout);

        try
        {
            var reply = await probe(container, containerPort, client.GetStream(), attempt.Token).ConfigureAwait(false);
            return (true, reply);
        }
        catch (Exception e)
            when (!cancellationToken.IsCancellationRequested
                && e is SocketException or IOException or OperationCanceledException
            )
        {
            // Accepted, then reset, closed, or silent: a runtime that cannot reach the server yet, or a server that
            // has not started answering. The caller's budget tells the two apart from a lost port.
            return (true, PublishedPortReply.NotYet);
        }
    }

    // Returns null when every port answers as the container, else which port was lost and how.
    private async Task<string?> _FindLostPortAsync(IContainer container, CancellationToken cancellationToken)
    {
        foreach (var containerPort in _containerPorts)
        {
            var loss = await _AwaitPortAsync(container, containerPort, cancellationToken).ConfigureAwait(false);

            if (loss is not null)
            {
                return loss;
            }
        }

        return null;
    }

    private async Task<string?> _AwaitPortAsync(
        IContainer container,
        int containerPort,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        long? refusedSince = null;

        while (true)
        {
            var (connected, reply) = await ProbeOnceAsync(
                    container,
                    containerPort,
                    _probe,
                    ConnectTimeout,
                    ProbeTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var address =
                $"{container.Hostname}:{container.GetMappedPublicPort(containerPort).ToString(CultureInfo.InvariantCulture)}";

            switch (reply)
            {
                case PublishedPortReply.Container:
                    return null;
                case PublishedPortReply.AnotherListener:
                    return $"{address} (another listener answered)";
            }

            if (connected)
            {
                refusedSince = null;
            }
            else
            {
                refusedSince ??= Stopwatch.GetTimestamp();

                if (Stopwatch.GetElapsedTime(refusedSince.Value) >= RefusedTimeout)
                {
                    return $"{address} (refused every connection)";
                }
            }

            if (Stopwatch.GetElapsedTime(started) >= AnswerTimeout)
            {
                return connected
                    ? $"{address} (accepted connections but never answered as the container)"
                    : $"{address} (refused every connection)";
            }

            await Task.Delay(_PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
