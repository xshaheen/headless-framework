// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Headless.Checks;

namespace Tests;

/// <summary>What one probe of a container's published port read back on the test host.</summary>
public enum PublishedPortReply
{
    /// <summary>Nothing has answered yet: refused, closed, or silent, as while the runtime still sets up its forward.</summary>
    NotYet,

    /// <summary>The container's server answered in its own protocol.</summary>
    Container,

    /// <summary>Another listener answered, so the port is lost to the container for its whole run.</summary>
    AnotherListener,
}

/// <summary>
/// Proves that a container's published port reaches that container from the test host, and starts the container again,
/// on another port, while another listener answers there instead.
/// </summary>
/// <remarks>
/// <para>
/// A desktop Docker engine such as OrbStack or Docker Desktop publishes a port as a listener on the host's wildcard
/// address, and Docker hands out host ports in sequence without looking at the host. A host process that already holds the
/// same port on <c>127.0.0.1</c>, such as another test's listener on a port the kernel gave it, is the more specific
/// match: every connection to <c>127.0.0.1</c>, the container's <see cref="IContainer.Hostname"/>, reaches that process
/// for the container's whole run, while the engine still reports the port published. Each start takes the next port in
/// the sequence, so a restart leaves the shadowed one behind.
/// </para>
/// <para>
/// Add the check to the container's wait strategy, so Testcontainers polls it on every start, a restart included, until
/// the server or another listener answers. Start the container only through <see cref="StartAsync"/>, which reads the
/// verdict of the start it just ran.
/// </para>
/// </remarks>
/// <param name="containerPort">The container port whose host mapping clients dial.</param>
/// <param name="probe">
/// Reads one reply from a fresh connection and names who answered. The listener that shadows a port can be another
/// test's forwarder to the same kind of broker, so the probe checks something only this container answers, such as a
/// name the test started it with, not just the protocol. A probe that writes a request first also gets an answer from
/// a listener that waits for the client to speak, such as an HTTP server.
/// </param>
[PublicAPI]
public sealed class PublishedPortCheck(
    int containerPort,
    Func<NetworkStream, CancellationToken, Task<PublishedPortReply>> probe
) : IWaitUntil
{
    // A start that keeps landing on shadowed ports means something holds a whole run of them; give up and say so.
    private const int _MaxStarts = 3;

    private static readonly TimeSpan _AttemptTimeout = TimeSpan.FromSeconds(1);

    // A runtime forwards a new port within seconds even under suite load; a port silent for this long never will.
    private static readonly TimeSpan _AnswerTimeout = TimeSpan.FromMinutes(2);

    private PublishedPortReply _reply;

    /// <summary>Adds this check after <paramref name="wait"/>'s own strategies.</summary>
    public IWaitForContainerOS AddTo(IWaitForContainerOS wait)
    {
        return wait.AddCustomWaitStrategy(this, strategy => strategy.WithTimeout(_AnswerTimeout));
    }

    /// <summary>Starts <paramref name="container"/>, and starts it again while another listener holds its port.</summary>
    /// <exception cref="InvalidOperationException">Every start of <see cref="_MaxStarts"/> landed on a shadowed port.</exception>
    public async Task StartAsync(IContainer container, CancellationToken cancellationToken)
    {
        var shadowed = new List<ushort>();

        for (var start = 1; ; start++)
        {
            _reply = PublishedPortReply.NotYet;
            await container.StartAsync(cancellationToken).ConfigureAwait(false);

            if (_reply is PublishedPortReply.Container)
            {
                return;
            }

            shadowed.Add(container.GetMappedPublicPort(containerPort));

            if (start == _MaxStarts)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Another listener answered on {container.Hostname} at every port {container.Image.FullName} was published on ({string.Join(", ", shadowed)}), so no client could reach it."
                    )
                );
            }

            await container.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    async Task<bool> IWaitUntil.UntilAsync(IContainer container)
    {
        _reply = await _ProbeAsync(container.Hostname, container.GetMappedPublicPort(containerPort))
            .ConfigureAwait(false);

        return _reply is not PublishedPortReply.NotYet;
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

    private async Task<PublishedPortReply> _ProbeAsync(string host, int port)
    {
        using var attempt = new CancellationTokenSource(_AttemptTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, attempt.Token).ConfigureAwait(false);

            return await probe(client.GetStream(), attempt.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            // Not reachable yet; Testcontainers polls again.
            return PublishedPortReply.NotYet;
        }
    }
}
