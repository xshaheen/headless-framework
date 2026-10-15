// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Checks;

namespace Headless.Hosting;

/// <summary>
/// The dependency a contributed health check probes, so checks that several packages contribute for one database or
/// one Redis report it once instead of probing it once per package.
/// </summary>
/// <remarks>
/// <para>
/// The key is computed when the health check service first builds its options, from the application's services,
/// because the connection a check probes is often known only then: a connection string from a factory, or a
/// multiplexer the application created. Checks whose keys are equal collapse into one: the check of the package that
/// owns the connection (<see cref="Owned" />), or the first registered when none does. The survivor keeps its own name,
/// failure status, and timeout, and takes the others' tags.
/// </para>
/// <para>
/// A key names a dependency, never a credential: build it from the provider, host, port, and database, as
/// <see cref="DatabaseKey" /> does. A key that is <see langword="null" />, or whose factory throws, leaves its check
/// alone.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessHealthCheckResource
{
    private HeadlessHealthCheckResource(Func<IServiceProvider, string?> key, bool ownsConnection)
    {
        Key = key;
        OwnsConnection = ownsConnection;
    }

    /// <summary>Gets the factory that computes the dependency's key from the application's services.</summary>
    public Func<IServiceProvider, string?> Key { get; }

    /// <summary>
    /// Gets whether the contributing package owns the connection, such as the SQL connection factory or the application's
    /// <c>DbContext</c>, so its check is the one that survives a collapse.
    /// </summary>
    public bool OwnsConnection { get; }

    /// <summary>A dependency whose connection the contributing package owns.</summary>
    /// <param name="key">Computes the dependency's key, or <see langword="null" /> when it cannot be told.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    public static HeadlessHealthCheckResource Owned(Func<IServiceProvider, string?> key) =>
        new(Argument.IsNotNull(key), ownsConnection: true);

    /// <summary>A dependency the contributing package uses through a connection another package may own.</summary>
    /// <param name="key">Computes the dependency's key, or <see langword="null" /> when it cannot be told.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    public static HeadlessHealthCheckResource Shared(Func<IServiceProvider, string?> key) =>
        new(Argument.IsNotNull(key), ownsConnection: false);

    /// <summary>
    /// Builds a database key from a provider family and an ADO.NET connection string: the provider, host, port, and
    /// database, lower-cased, with every credential left out, such as <c>postgresql://db1:5432/orders</c>.
    /// </summary>
    /// <param name="provider">
    /// The provider family, such as <c>postgresql</c> or <c>sqlserver</c>, or an EF Core provider name such as
    /// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>, which maps to its family so EF and ADO.NET checks share a key.
    /// </param>
    /// <param name="connectionString">The connection string; <see langword="null" /> gives no key.</param>
    /// <returns>The key, or <see langword="null" /> when the connection string names no host.</returns>
    /// <remarks>
    /// Two pools that reach one database with different users get one key, so they share one probe: a credential that
    /// stops working on one of them surfaces in the failing query, not in readiness.
    /// </remarks>
    public static string? DatabaseKey(string provider, string? connectionString)
    {
        Argument.IsNotNullOrWhiteSpace(provider);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        DbConnectionStringBuilder builder;

        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            return null;
        }

        var host = _First(builder, "Host", "Server", "Data Source", "Address", "Addr", "Network Address");

        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var port = _First(builder, "Port");

        // SQL Server carries the port in the server name: "tcp:db1,1433".
        var comma = host.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            port ??= host[(comma + 1)..].Trim();
            host = host[..comma];
        }

        host = host.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        var database = _First(builder, "Database", "Initial Catalog") ?? string.Empty;

        return string.Create(
                CultureInfo.InvariantCulture,
                $"{_Family(provider)}://{host}{(string.IsNullOrEmpty(port) ? string.Empty : ":" + port)}/{database}"
            )
            .ToLowerInvariant();
    }

    private static string _Family(string provider) =>
        provider.Contains("npgsql", StringComparison.OrdinalIgnoreCase)
        || provider.Contains("postgres", StringComparison.OrdinalIgnoreCase)
            ? "postgresql"
        : provider.Contains("sqlserver", StringComparison.OrdinalIgnoreCase) ? "sqlserver"
        : provider.Contains("sqlite", StringComparison.OrdinalIgnoreCase) ? "sqlite"
        : provider;

    private static string? _First(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }
}
