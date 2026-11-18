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

    private static readonly TimeSpan _ForwardingTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _ProbeAttemptTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _ProbeInterval = TimeSpan.FromMilliseconds(100);

    private readonly IContainer _container = _BuildContainer();

    // Serializes slot allocation, so a slot is counted once its proxy exists and a create Toxiproxy refuses frees it.
    private readonly SemaphoreSlim _createLock = new(1, 1);

    private HttpClient? _http;
    private int _proxies;

    private static IContainer _BuildContainer()
    {
        var builder = new ContainerBuilder(_Image).WithPortBinding(_ApiPort, assignRandomHostPort: true);

        // One published port per proxy slot, so the bindings follow the cap CreateProxyAsync enforces.
        for (var index = 0; index < _MaxProxies; index++)
        {
            builder = builder.WithPortBinding(_FirstProxyPort + index, assignRandomHostPort: true);
        }

        return builder
            // Lets the proxy reach a broker published on the Docker host, on Linux CI as well as Docker Desktop.
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPath("/version").ForPort(_ApiPort))
            )
            .Build();
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _container.StartAsync(cancellationToken).ConfigureAwait(false);
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(_ApiPort)}"),
        };
    }

    /// <summary>
    /// Creates a proxy that forwards to <paramref name="upstreamHostPort"/> on the Docker host and returns it once a
    /// request from the test host reaches the proxy through its published port.
    /// Clients connect to <see cref="ToxiproxyProxy.Host"/>:<see cref="ToxiproxyProxy.Port"/> instead of the broker.
    /// </summary>
    /// <remarks>
    /// The upstream need not be listening yet: Toxiproxy dials it only when a client connects, so a caller may start
    /// the broker after this returns.
    /// </remarks>
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
                throw new InvalidOperationException($"This server publishes {_MaxProxies} proxy ports at most.");
            }

            var listenPort = _FirstProxyPort + _proxies;

            // The proxy first forwards to Toxiproxy's own API, which always answers, so the probe below proves the
            // published port reaches the proxy without depending on an upstream the caller may start later.
            using (
                var created = await http.PostAsJsonAsync(
                        "/proxies",
                        new
                        {
                            name,
                            listen = $"0.0.0.0:{listenPort}",
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

            // The proxy now holds its listen port, so the slot is taken even if a later step fails.
            _proxies++;

            var host = _container.Hostname;
            var port = _container.GetMappedPublicPort(listenPort);
            await _WaitUntilForwardingAsync(host, port, cancellationToken).ConfigureAwait(false);

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

    // A container runtime's port forwarder (OrbStack, Docker Desktop, docker-proxy) accepts a connection on a
    // published port and then closes it when it cannot reach the container's listener, so a client's first connect
    // can fail although the proxy exists. Only a reply that came back through the proxy proves the path.
    private async Task _WaitUntilForwardingAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_ForwardingTimeout);
        var attempts = 0;
        string? failure;

        while (true)
        {
            attempts++;
            failure = await _ProbeAsync(host, port, deadline.Token).ConfigureAwait(false);

            if (failure is null)
            {
                return;
            }

            try
            {
                await Task.Delay(_ProbeInterval, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        // The Toxiproxy log tells a forwarder that never reached the container from a proxy that never accepted.
        var (stdout, stderr) = await _container
            .GetLogsAsync(timestampsEnabled: false, ct: cancellationToken)
            .ConfigureAwait(false);

        throw new InvalidOperationException(
            $"Toxiproxy proxy at {host}:{port} did not answer through its published port within "
                + $"{_ForwardingTimeout.TotalSeconds:0}s ({attempts} attempts; last: {failure}).\n"
                + $"Toxiproxy log:\n{stdout}{stderr}"
        );
    }

    // Returns null once Toxiproxy's API answers through the proxy, otherwise what the attempt saw.
    private static async Task<string?> _ProbeAsync(string host, int port, CancellationToken cancellationToken)
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
                    return $"accepted, then closed after {received} reply bytes";
                }

                received += read;
            }

            return reply.AsSpan().SequenceEqual(expected)
                ? null
                : $"unexpected reply '{Encoding.ASCII.GetString(reply)}'";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"no reply within {_ProbeAttemptTimeout.TotalSeconds:0}s";
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return $"{e.GetType().Name}: {e.Message}";
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
