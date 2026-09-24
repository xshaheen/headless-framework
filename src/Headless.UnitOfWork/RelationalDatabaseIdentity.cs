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
/// fails loudly and names the mismatch.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RelationalDatabaseIdentity
{
    private const string _Loopback = "localhost";

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

        var configuredSource = NormalizeDataSource(configured.DataSource);

        return configuredSource.Length > 0
            && string.Equals(configuredSource, NormalizeDataSource(candidate.DataSource), StringComparison.Ordinal);
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
