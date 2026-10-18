// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
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

    private readonly IContainer _container = _BuildContainer();

    // Serializes slot allocation, so a slot is counted only once its proxy exists and a failed create frees it.
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
    /// Creates a proxy that forwards to <paramref name="upstreamHostPort"/> on the Docker host and returns it.
    /// Clients connect to <see cref="ToxiproxyProxy.Host"/>:<see cref="ToxiproxyProxy.Port"/> instead of the broker.
    /// </summary>
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
            using var response = await http.PostAsJsonAsync(
                    "/proxies",
                    new
                    {
                        name,
                        listen = $"0.0.0.0:{listenPort}",
                        upstream = $"host.docker.internal:{upstreamHostPort}",
                        enabled = true,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            await _EnsureSuccessAsync(response, "create proxy", cancellationToken).ConfigureAwait(false);
            _proxies++;

            return new ToxiproxyProxy(http, name, _container.Hostname, _container.GetMappedPublicPort(listenPort));
        }
        finally
        {
            _createLock.Release();
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
