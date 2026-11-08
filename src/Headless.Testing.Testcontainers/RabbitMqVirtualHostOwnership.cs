// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;
using RabbitMQ.Client;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// The virtual host one fixture owns on a RabbitMQ broker it may share. A connection the owner holds into the virtual
/// host marks it alive, and a sweep at startup deletes the virtual hosts no connection is using.
/// </summary>
/// <remarks>
/// A virtual host is created before its owner can connect to it, so the sweep spares one younger than
/// <see cref="_CreationGrace"/>; each virtual host records its creation time in its description for that check.
/// </remarks>
internal sealed class RabbitMqVirtualHostOwnership : IAsyncDisposable
{
    private static readonly TimeSpan _CreationGrace = TimeSpan.FromMinutes(2);

    private readonly IContainer _container;
    private readonly IConnection _leaseConnection;

    private RabbitMqVirtualHostOwnership(IContainer container, string virtualHost, IConnection leaseConnection)
    {
        _container = container;
        VirtualHost = virtualHost;
        _leaseConnection = leaseConnection;
    }

    /// <summary>The virtual host this fixture owns.</summary>
    public string VirtualHost { get; }

    /// <summary>
    /// Sweeps the virtual hosts of dead owners, creates a new one that <paramref name="userName"/> may use, and opens
    /// the connection that keeps it alive.
    /// </summary>
    public static async Task<RabbitMqVirtualHostOwnership> CreateAsync(
        IContainer container,
        ushort port,
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        var virtualHost = TestDatabaseOwner.NewId();

        await _SweepDeadOwnersAsync(container, cancellationToken).ConfigureAwait(false);

        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await _RunAsync(container, ["add_vhost", virtualHost, "--description", created], cancellationToken)
            .ConfigureAwait(false);
        await _RunAsync(
                container,
                ["set_permissions", "-p", virtualHost, userName, ".*", ".*", ".*"],
                cancellationToken
            )
            .ConfigureAwait(false);

        var factory = new ConnectionFactory
        {
            HostName = container.Hostname,
            Port = port,
            UserName = userName,
            Password = password,
            VirtualHost = virtualHost,
            ClientProvidedName = "headless-testing-owner",
            // The lease is the connection itself; a recovered connection after a broker restart would claim a
            // virtual host a sweep may already have judged dead.
            AutomaticRecoveryEnabled = false,
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        return new RabbitMqVirtualHostOwnership(container, virtualHost, connection);
    }

    /// <summary>Deletes the virtual host, then closes the connection that kept it alive.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _RunAsync(_container, ["delete_vhost", VirtualHost], CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _leaseConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task _SweepDeadOwnersAsync(IContainer container, CancellationToken cancellationToken)
    {
        var virtualHosts = await _RunAsync(
                container,
                ["list_vhosts", "-q", "--no-table-headers", "name", "description"],
                cancellationToken
            )
            .ConfigureAwait(false);

        var candidates = _Rows(virtualHosts)
            .Where(row => TestDatabaseOwner.OwnerOf(row[0]) == row[0])
            .Where(row =>
                row.Length < 2
                || !long.TryParse(row[1], CultureInfo.InvariantCulture, out var created)
                || DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(created) > _CreationGrace
            )
            .Select(row => row[0])
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var connections = await _RunAsync(
                container,
                ["list_connections", "-q", "--no-table-headers", "vhost"],
                cancellationToken
            )
            .ConfigureAwait(false);

        var live = _Rows(connections).Select(row => row[0]).ToHashSet(StringComparer.Ordinal);

        foreach (var virtualHost in candidates.Where(virtualHost => !live.Contains(virtualHost)))
        {
            await _RunAsync(container, ["delete_vhost", virtualHost], cancellationToken, allowMissing: true)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<string[]> _Rows(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split('\t', StringSplitOptions.TrimEntries));
    }

    private static async Task<string> _RunAsync(
        IContainer container,
        IList<string> arguments,
        CancellationToken cancellationToken,
        bool allowMissing = false
    )
    {
        var result = await container.ExecAsync(["rabbitmqctl", .. arguments], cancellationToken).ConfigureAwait(false);

        // Another sweeper may delete the same dead virtual host first.
        if (result.ExitCode != 0 && !(allowMissing && result.Stderr.Contains("not_found", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"rabbitmqctl {string.Join(' ', arguments)} failed ({result.ExitCode}): {result.Stderr}"
                )
            );
        }

        return result.Stdout;
    }
}
