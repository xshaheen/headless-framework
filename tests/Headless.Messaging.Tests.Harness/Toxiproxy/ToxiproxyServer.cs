// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.Toxiproxy;

/// <summary>
/// A Toxiproxy container a test places between a client and a broker, so it can cut or degrade the connection while
/// the broker keeps running. The control API is plain HTTP, so no client package is needed.
/// </summary>
[PublicAPI]
public sealed class ToxiproxyServer : IAsyncDisposable
{
    // Pinned by digest like every image in Headless.Testing.Testcontainers.TestImages, which the harness does not reference.
    private const string _Image =
        "ghcr.io/shopify/toxiproxy:2.12.0@sha256:9378ed52a28bc50edc1350f936f518f31fa95f0d15917d6eb40b8e376d1a214e";

    private const int _ApiPort = 8474;
    private const int _FirstProxyPort = 8666;
    private const int _MaxProxies = 3;

    // Published proxy ports beyond the cap, so a proxy whose port another host listener holds moves to a spare one.
    private const int _SpareSlots = 3;
    private const int _Slots = _MaxProxies + _SpareSlots;

    // A server whose API port keeps landing on a shadowed port means something holds a whole run of them.
    private const int _MaxStarts = 3;

    private static readonly TimeSpan _ForwardingTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _ProbeAttemptTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _ProbeInterval = TimeSpan.FromMilliseconds(100);

    private readonly IContainer _container = _BuildContainer();

    // Serializes slot allocation, so a slot is counted once its proxy exists and a create Toxiproxy refuses frees it.
    private readonly SemaphoreSlim _createLock = new(1, 1);

    private HttpClient? _http;
    private int _proxies;
    private int _nextSlot;

    private static IContainer _BuildContainer()
    {
        var builder = new ContainerBuilder(_Image).WithPortBinding(_ApiPort, assignRandomHostPort: true);

        for (var index = 0; index < _Slots; index++)
        {
            builder = builder.WithPortBinding(_FirstProxyPort + index, assignRandomHostPort: true);
        }

        return builder
            // Lets the proxy reach a broker published on the Docker host, on Linux CI as well as Docker Desktop.
            .WithExtraHost("host.docker.internal", "host-gateway")
            // The API is checked through its published port after the start, where a reply from another listener is told
            // apart from one that has not come yet; a host HTTP wait would retry such a reply until it timed out.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Starting Toxiproxy HTTP server"))
            .Build();
    }

    /// <summary>
    /// Starts the server and returns once its API answers through its published port, starting it again while another
    /// listener answers there instead.
    /// </summary>
    /// <remarks>
    /// A desktop Docker engine such as OrbStack or Docker Desktop publishes a port as a listener on the host's wildcard
    /// address, and Docker hands out host ports in sequence without looking at the host. A host process that already
    /// holds the same port on <c>127.0.0.1</c>, such as another test's listener on a port the kernel gave it, is the more
    /// specific match, so every connection to the container's host address reaches that process for the container's whole
    /// run. Each start takes the next ports in the sequence, so a restart leaves a shadowed API port behind.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The API never answered, or another listener answered at every API port of every start.
    /// </exception>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var shadowed = new List<string>();

        for (var start = 1; ; start++)
        {
            if (start > 1)
            {
                await _container.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            await _container.StartAsync(cancellationToken).ConfigureAwait(false);
            var host = _container.Hostname;
            var port = _container.GetMappedPublicPort(_ApiPort);
            var foreignReply = await _WaitUntilServedAsync(host, port, cancellationToken).ConfigureAwait(false);

            if (foreignReply is null)
            {
                _http?.Dispose();
                _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
                return;
            }

            shadowed.Add($"{host}:{port} ({foreignReply})");

            if (start == _MaxStarts)
            {
                throw new InvalidOperationException(
                    $"Another listener answered at the Toxiproxy API port of every start: {string.Join(", ", shadowed)}."
                );
            }
        }
    }

    /// <summary>
    /// Creates a proxy that forwards to <paramref name="upstreamHostPort"/> on the Docker host and returns it once a
    /// request from the test host reaches the proxy through its published port.
    /// Clients connect to <see cref="ToxiproxyProxy.Host"/>:<see cref="ToxiproxyProxy.Port"/> instead of the broker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The upstream need not be listening yet: Toxiproxy dials it only when a client connects, so a caller may start
    /// the broker after this returns.
    /// </para>
    /// <para>
    /// A published port another host listener holds (see <see cref="StartAsync"/>) cannot reach the proxy, so the proxy
    /// moves to the next spare published port. Earlier proxies keep theirs, which a restart would not.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The server has not started, already holds its maximum of proxies, ran out of published ports another listener
    /// does not hold, or the proxy never answered through its published port.
    /// </exception>
    public async Task<ToxiproxyProxy> CreateProxyAsync(
        string name,
        int upstreamHostPort,
        CancellationToken cancellationToken = default
    )
    {
        var http = _http ?? throw new InvalidOperationException("The Toxiproxy server has not started.");
        await _createLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_proxies >= _MaxProxies)
            {
                throw new InvalidOperationException($"This server hands out {_MaxProxies} proxies at most.");
            }

            var slot = _NextFreeSlot([]);

            // The proxy first forwards to Toxiproxy's own API, which always answers, so the probe below proves the
            // published port reaches the proxy without depending on an upstream the caller may start later.
            using (
                var created = await http.PostAsJsonAsync(
                        "/proxies",
                        new
                        {
                            name,
                            listen = _Listen(slot),
                            upstream = $"127.0.0.1:{_ApiPort}",
                            enabled = true,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                await _EnsureSuccessAsync(created, "create proxy", cancellationToken).ConfigureAwait(false);
            }

            // The proxy now holds its listen port, so it and the port count even if a later step fails; a create Toxiproxy
            // refused holds neither.
            _proxies++;
            _nextSlot = slot + 1;

            var host = _container.Hostname;
            var shadowed = new List<string>();
            int port;

            while (true)
            {
                port = _container.GetMappedPublicPort(_FirstProxyPort + slot);
                var foreignReply = await _WaitUntilServedAsync(host, port, cancellationToken).ConfigureAwait(false);

                if (foreignReply is null)
                {
                    break;
                }

                shadowed.Add($"{host}:{port} ({foreignReply})");
                slot = _NextFreeSlot(shadowed);

                using var moved = await http.PostAsJsonAsync(
                        $"/proxies/{name}",
                        new { listen = _Listen(slot) },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await _EnsureSuccessAsync(moved, "move proxy to a spare port", cancellationToken).ConfigureAwait(false);

                // The shadowed port stays spent: another listener holds it for the container's whole run.
                _nextSlot = slot + 1;
            }

            using var repointed = await http.PostAsJsonAsync(
                    $"/proxies/{name}",
                    new { upstream = $"host.docker.internal:{upstreamHostPort}" },
                    cancellationToken
                )
                .ConfigureAwait(false);
            await _EnsureSuccessAsync(repointed, "point proxy at upstream", cancellationToken).ConfigureAwait(false);

            return new ToxiproxyProxy(http, name, host, port);
        }
        finally
        {
            _createLock.Release();
        }
    }

    // A slot is spent only once a proxy listens there, so a create or move Toxiproxy refused leaves it free.
    private int _NextFreeSlot(List<string> shadowed)
    {
        if (_nextSlot >= _Slots)
        {
            throw new InvalidOperationException(
                $"This server publishes {_Slots} proxy ports, and another listener answered at each one left: {string.Join(", ", shadowed)}."
            );
        }

        return _nextSlot;
    }

    private static string _Listen(int slot)
    {
        return $"0.0.0.0:{_FirstProxyPort + slot}";
    }

    // A container runtime's port forwarder (OrbStack, Docker Desktop, docker-proxy) accepts a connection on a
    // published port and then closes it when it cannot reach the container's listener, so a client's first connect
    // can fail although the listener exists. Only a reply that came back through the port proves the path. Returns
    // null once Toxiproxy's API answers there, or what another listener answered instead, which no retry changes.
    private async Task<string?> _WaitUntilServedAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_ForwardingTimeout);
        var attempts = 0;
        string failure;

        while (true)
        {
            attempts++;
            var (outcome, detail) = await _ProbeAsync(host, port, deadline.Token).ConfigureAwait(false);

            switch (outcome)
            {
                case ProbeOutcome.Served:
                    return null;
                case ProbeOutcome.AnotherListener:
                    return detail;
            }

            failure = detail;

            try
            {
                await Task.Delay(_ProbeInterval, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        // The Toxiproxy log tells a forwarder that never reached the container from a listener that never accepted.
        var (stdout, stderr) = await _container
            .GetLogsAsync(timestampsEnabled: false, ct: cancellationToken)
            .ConfigureAwait(false);

        throw new InvalidOperationException(
            $"Toxiproxy at {host}:{port} did not answer through its published port within "
                + $"{_ForwardingTimeout.TotalSeconds:0}s ({attempts} attempts; last: {failure}).\n"
                + $"Toxiproxy log:\n{stdout}{stderr}"
        );
    }

    private enum ProbeOutcome
    {
        NotYet,
        Served,
        AnotherListener,
    }

    // Asks Toxiproxy's API for its version. Any reply other than its 200 is another listener's, since a runtime that
    // cannot reach the container closes the connection without a byte.
    private static async Task<(ProbeOutcome Outcome, string Detail)> _ProbeAsync(
        string host,
        int port,
        CancellationToken cancellationToken
    )
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_ProbeAttemptTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, attempt.Token).ConfigureAwait(false);

            var stream = client.GetStream();
            var request = "GET /version HTTP/1.1\r\nHost: toxiproxy\r\nConnection: close\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(request, attempt.Token).ConfigureAwait(false);

            var expected = "HTTP/1.1 200"u8.ToArray();
            var reply = new byte[expected.Length];
            var received = 0;

            while (received < reply.Length)
            {
                var read = await stream.ReadAsync(reply.AsMemory(received), attempt.Token).ConfigureAwait(false);

                if (read == 0)
                {
                    return received == 0
                        ? (ProbeOutcome.NotYet, "accepted, then closed without a reply")
                        : (
                            ProbeOutcome.AnotherListener,
                            $"closed after '{Encoding.ASCII.GetString(reply, 0, received)}'"
                        );
                }

                received += read;
            }

            return reply.AsSpan().SequenceEqual(expected)
                ? (ProbeOutcome.Served, string.Empty)
                : (ProbeOutcome.AnotherListener, $"replied '{Encoding.ASCII.GetString(reply)}'");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (ProbeOutcome.NotYet, $"no reply within {_ProbeAttemptTimeout.TotalSeconds:0}s");
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return (ProbeOutcome.NotYet, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Removes every toxic and enables every proxy on this server. Deleting a single toxic can hang the Toxiproxy 2.12
    /// API, so tests clear their faults with this instead.
    /// </summary>
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var http = _http ?? throw new InvalidOperationException("The Toxiproxy server has not started.");
        return _ResetAsync(http, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        _createLock.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    internal static async Task _ResetAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync("/reset", content: null, cancellationToken).ConfigureAwait(false);
        await _EnsureSuccessAsync(response, "reset", cancellationToken).ConfigureAwait(false);
    }

    internal static async Task _EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken
    )
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"Toxiproxy {operation} failed ({(int)response.StatusCode}): {body}");
    }
}

/// <summary>One proxied route. Disabling it closes every open connection and refuses new ones.</summary>
[PublicAPI]
public sealed class ToxiproxyProxy(HttpClient http, string name, string host, int port) : INetworkLink
{
    /// <summary>Gets the address clients connect to instead of the broker.</summary>
    public string Host { get; } = host;

    public int Port { get; } = port;

    /// <summary>Cuts every open connection and refuses new ones: a network outage the broker does not see.</summary>
    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        _SetEnabledAsync(false, cancellationToken);

    /// <summary>Accepts connections again.</summary>
    public Task EnableAsync(CancellationToken cancellationToken = default) => _SetEnabledAsync(true, cancellationToken);

    Task INetworkLink.CutAsync(CancellationToken cancellationToken) => DisableAsync(cancellationToken);

    Task INetworkLink.RestoreAsync(CancellationToken cancellationToken) => EnableAsync(cancellationToken);

    /// <summary>
    /// Adds a toxic, for example <c>latency</c> with <c>latency</c> and <c>jitter</c> attributes, <c>timeout</c> with
    /// <c>timeout</c> (0 black-holes traffic and holds the connection open), or <c>reset_peer</c>.
    /// </summary>
    public async Task AddToxicAsync(
        string toxicName,
        string type,
        IReadOnlyDictionary<string, object> attributes,
        CancellationToken cancellationToken = default
    )
    {
        using var response = await http.PostAsJsonAsync(
                $"/proxies/{name}/toxics",
                new
                {
                    name = toxicName,
                    type,
                    stream = "downstream",
                    toxicity = 1.0,
                    attributes,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        await ToxiproxyServer._EnsureSuccessAsync(response, "add toxic", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes every toxic and enables every proxy on this proxy's server, as <see cref="ToxiproxyServer.ResetAsync"/>
    /// does. Deleting a single toxic can hang the Toxiproxy 2.12 API, so there is no per-toxic removal.
    /// </summary>
    public Task ResetServerAsync(CancellationToken cancellationToken = default) =>
        ToxiproxyServer._ResetAsync(http, cancellationToken);

    private async Task _SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"/proxies/{name}", new { enabled }, cancellationToken)
            .ConfigureAwait(false);
        await ToxiproxyServer
            ._EnsureSuccessAsync(response, enabled ? "enable proxy" : "disable proxy", cancellationToken)
            .ConfigureAwait(false);
    }
}
