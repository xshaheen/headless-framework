// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Toxiproxy;

/// <summary>
/// A Toxiproxy route in front of a test database, so a test can cut the database connection or drop its replies while
/// the database keeps running. Disposing the route removes its Toxiproxy container.
/// </summary>
[PublicAPI]
public sealed class ToxiproxyDatabaseRoute : IAsyncDisposable
{
    private readonly ToxiproxyServer _server;

    private ToxiproxyDatabaseRoute(ToxiproxyServer server, ToxiproxyProxy proxy)
    {
        _server = server;
        Proxy = proxy;
    }

    /// <summary>Gets the route; clients connect to its host and port instead of the database's.</summary>
    public ToxiproxyProxy Proxy { get; }

    /// <summary>Starts a Toxiproxy server that forwards to the database published on <paramref name="databaseHostPort"/>.</summary>
    public static async Task<ToxiproxyDatabaseRoute> StartAsync(
        int databaseHostPort,
        CancellationToken cancellationToken = default
    )
    {
        var server = new ToxiproxyServer();

        try
        {
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            var proxy = await server
                .CreateProxyAsync("database", databaseHostPort, cancellationToken)
                .ConfigureAwait(false);

            return new ToxiproxyDatabaseRoute(server, proxy);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
