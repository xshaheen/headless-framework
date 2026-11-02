// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data.Common;
using Headless.Checks;

namespace Headless.UnitOfWork;

/// <summary>
/// The one answer every participant gives to "is this unit's connection on the database I am configured for?"
/// before it writes a durable row into the unit's transaction: the messaging outbox storages and the Jobs store
/// share it, so a unit they both enlist in is accepted or refused by both for the same reason.
/// </summary>
/// <remarks>
/// A false match is the dangerous direction — the row would commit into a database whose relay or poller never
/// reads it — so the comparison normalizes only what cannot name a different server: the host is compared
/// case-insensitively (a Unix-socket path is compared exactly), an optional <c>tcp:</c> / <c>tcp://</c> prefix is
/// ignored, and the loopback spellings
/// (<c>localhost</c>, <c>127.0.0.1</c>, <c>::1</c>, <c>[::1]</c>, <c>.</c>, <c>(local)</c>) name the same host.
/// Ports, instance names, and database names are compared exactly; any other difference is a refusal, which
/// fails loudly and names the mismatch. A connection whose host list names several hosts (Npgsql <c>Host=pg1,pg2</c>)
/// stands for each listed host, and the candidate must reach only hosts the configured connection lists.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RelationalDatabaseIdentity
{
    private const string _Loopback = "localhost";

    // Npgsql's key for its host list; its builder resolves the "Server" synonym to it.
    private const string _HostKey = "Host";

    private static readonly string[] _LoopbackAliases = ["localhost", "127.0.0.1", "[::1]", "::1", "(local)", "."];

    /// <summary>
    /// Returns whether <paramref name="configured" /> and <paramref name="candidate" /> reach the same database:
    /// the same provider connection type, the same normalized data source, and the same database name.
    /// </summary>
    /// <param name="configured">A connection built from the participant's own configuration; never opened here.</param>
    /// <param name="candidate">The unit of work's live connection.</param>
    /// <returns>
    /// <see langword="true" /> when both name the same database; <see langword="false" /> when they differ or
    /// either leaves the data source or database name empty.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either connection is <see langword="null" />.</exception>
    public static bool IsSameDatabase(DbConnection configured, DbConnection candidate)
    {
        Argument.IsNotNull(configured);
        Argument.IsNotNull(candidate);

        if (
            configured.GetType() != candidate.GetType()
            || string.IsNullOrEmpty(configured.Database)
            || !string.Equals(configured.Database, candidate.Database, StringComparison.Ordinal)
        )
        {
            return false;
        }

        var configuredSources = _DataSources(configured);
        if (configuredSources.Count == 0)
        {
            return false;
        }

        var candidateSources = _DataSources(candidate);

        // Every host the candidate can reach must be one the participant is configured for: an opened multi-host
        // connection names the one host it reached, which must be in the configured list.
        return candidateSources.Count > 0 && candidateSources.IsSubsetOf(configuredSources);
    }

    /// <summary>
    /// The normalized data sources <paramref name="connection" /> can reach: its own <see cref="DbConnection.DataSource" />,
    /// or, when the provider reports none because the connection lists several hosts, one entry per listed host.
    /// </summary>
    /// <remarks>
    /// Npgsql reports an empty data source for an unopened connection whose <c>Host</c> lists several hosts
    /// (<c>Host=pg1,pg2</c>), and the host it reached once opened. Each listed host is expanded into a single-host
    /// connection through the provider's own factory, so its data source keeps the provider's format, with an
    /// inline port (<c>pg1:5433</c>) or the shared <c>Port</c>. The listed hosts are taken to serve one database: a
    /// multi-host source is a set of replicas or failover peers of the same database name.
    /// </remarks>
    private static HashSet<string> _DataSources(DbConnection connection)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var source = NormalizeDataSource(connection.DataSource);
        if (source.Length > 0)
        {
            sources.Add(source);
            return sources;
        }

        var factory = DbProviderFactories.GetFactory(connection);
        var builder = factory?.CreateConnectionStringBuilder();
        if (factory is null || builder is null || string.IsNullOrEmpty(connection.ConnectionString))
        {
            return sources;
        }

        builder.ConnectionString = connection.ConnectionString;
        if (
            !builder.TryGetValue(_HostKey, out var value)
            || value is not string hosts
            || !hosts.Contains(',', StringComparison.Ordinal)
        )
        {
            return sources;
        }

        foreach (var host in hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            builder[_HostKey] = host;
            using var single = factory.CreateConnection();
            if (single is null)
            {
                sources.Clear();
                return sources;
            }

            single.ConnectionString = builder.ConnectionString;
            var hostSource = NormalizeDataSource(single.DataSource);

            // A listed host the provider cannot name cannot be compared; refuse the whole list rather than guess what the
            // remaining hosts say about the configuration.
            if (hostSource.Length == 0)
            {
                sources.Clear();
                return sources;
            }

            sources.Add(hostSource);
        }

        return sources;
    }

    /// <summary>
    /// Normalizes a provider <see cref="DbConnection.DataSource" /> for comparison: trimmed, lower-cased unless it is a
    /// Unix-socket path, without a <c>tcp:</c> / <c>tcp://</c> prefix, and with a leading loopback alias rewritten to
    /// <c>localhost</c>.
    /// </summary>
    internal static string NormalizeDataSource(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return string.Empty;
        }

        var source = dataSource.Trim();

        // A Unix-socket path (Npgsql reports "/var/run/postgresql/.s.PGSQL.5432") is case-sensitive; a host is not.
        if (source.StartsWith('/'))
        {
            return source;
        }

        source = source.ToLowerInvariant();

        if (source.StartsWith("tcp://", StringComparison.Ordinal))
        {
            source = source[6..];
        }
        else if (source.StartsWith("tcp:", StringComparison.Ordinal))
        {
            source = source[4..];
        }

        foreach (var alias in _LoopbackAliases)
        {
            // The alias must be the whole host: followed by nothing, or by a port (":5432", ",1433") or an
            // instance ("\SQLEXPRESS") separator, so "localhost2" or ".db" are left alone.
            if (
                source.StartsWith(alias, StringComparison.Ordinal)
                && (source.Length == alias.Length || source[alias.Length] is ':' or ',' or '\\')
            )
            {
                return string.Concat(_Loopback, source.AsSpan(alias.Length));
            }
        }

        return source;
    }
}
