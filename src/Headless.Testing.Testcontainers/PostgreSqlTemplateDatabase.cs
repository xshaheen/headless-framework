// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Builds a provisioned template database once per key on a PostgreSQL server other fixtures and processes may share,
/// so every fixture that names the key clones it instead of repeating the provisioning.
/// </summary>
/// <remarks>
/// <para>
/// A template is named <see cref="Prefix"/> plus a hash of the key and the framework version. The prefix is not the
/// owner prefix <c>hf_</c>, so the dead-owner sweep never lists a template and no owner name can equal one.
/// </para>
/// <para>
/// <c>datistemplate</c> is the ready marker: the builder sets it last, after the provisioning succeeded and every
/// session in the database ended. A database with the name but without the marker is a build that is running or one
/// whose process died; only the holder of the build lock may drop and rebuild it.
/// </para>
/// <para>
/// The server keeps at most <see cref="MaxTemplates"/> templates. Each new build drops the oldest beyond that bound
/// (by database oid, which grows with every created database), skipping any whose build lock another session holds.
/// Old keys therefore never pile up on a long-lived shared server, and a key that is evicted while still in use costs
/// one rebuild on its next fixture start.
/// </para>
/// </remarks>
#pragma warning disable CA2100 // False positive: SQL text interpolates only template names this class generates (prefix plus hex); values are parameters.
internal static class PostgreSqlTemplateDatabase
{
    /// <summary>The prefix of every template database.</summary>
    public const string Prefix = "hftpl_";

    /// <summary>The most templates one server keeps; a new build evicts the oldest beyond it.</summary>
    public const int MaxTemplates = 8;

    // Part of every template's name. Bump it whenever what a template holds or how it is sealed changes, so a checkout
    // on the new protocol never clones a template the old one built.
    private const string _ProtocolVersion = "t1";

    // 24 hex digits (96 bits) keep distinct keys apart and the name well under the 63-byte identifier limit.
    private const int _HashLength = 24;

    // Waiting for another process's build covers its whole provisioning, which can run several migrations, so the
    // bound is generous; it only stops a stuck builder from hanging every waiter until the test run's own timeout.
    private const int _BuildLockTimeoutSeconds = 600;

    // A clone can lose a race with a provisioning session the seal is still terminating, which PostgreSQL reports as
    // 55006 "source database is being accessed by other users".
    private const int _MaxCloneAttempts = 5;

    private static readonly string _FrameworkVersion =
        typeof(PostgreSqlTemplateDatabase)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(PostgreSqlTemplateDatabase).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>Returns the template name for <paramref name="key"/> under this framework version.</summary>
    public static string NameFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(_ProtocolVersion + "\n" + _FrameworkVersion + "\n" + key));
        return Prefix + Convert.ToHexStringLower(hash, 0, _HashLength / 2);
    }

    /// <summary>
    /// Returns the name of the ready template for <paramref name="key"/>, building it through
    /// <paramref name="provision"/> first when no process has.
    /// </summary>
    /// <param name="adminConnectionString">A superuser connection string to the <c>postgres</c> database.</param>
    /// <param name="key">The cache key; a different key gives a different template.</param>
    /// <param name="provision">Builds the schema and seed data, given the template database's connection string.</param>
    /// <param name="cancellationToken">Cancels the build or the wait for another process's build.</param>
    /// <exception cref="TimeoutException">Another session held the build lock for longer than ten minutes.</exception>
    public static async Task<string> EnsureAsync(
        string adminConnectionString,
        string key,
        Func<string, CancellationToken, Task> provision,
        CancellationToken cancellationToken
    )
    {
        var name = NameFor(key);

        // The build lock lives exactly as long as this session, so the connection must never come from a pool.
        var unpooled = new NpgsqlConnectionStringBuilder(adminConnectionString) { Pooling = false }.ToString();

        if (await _IsReadyAsync(unpooled, name, cancellationToken).ConfigureAwait(false))
        {
            return name;
        }

        var lockConnection = new NpgsqlConnection(unpooled);
        await using var _ = lockConnection.ConfigureAwait(false);
        await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _LockAsync(lockConnection, name, cancellationToken).ConfigureAwait(false);

        // Another process may have finished the build while this one waited for the lock.
        if (await _IsReadyAsync(unpooled, name, cancellationToken).ConfigureAwait(false))
        {
            return name;
        }

        // Holding the lock, an unmarked database can only be what a dead builder left behind.
        await _DropAsync(lockConnection, name, cancellationToken).ConfigureAwait(false);
        await _ExecuteAsync(lockConnection, $"CREATE DATABASE \"{name}\";", cancellationToken).ConfigureAwait(false);

        var templateConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = name,
        }.ToString();

        try
        {
            await provision(templateConnectionString, cancellationToken).ConfigureAwait(false);
            await _SealAsync(lockConnection, name, templateConnectionString, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A half-provisioned template must never be marked ready; the next fixture builds it again.
            await _DropAsync(lockConnection, name, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await _EvictStaleAsync(lockConnection, name, cancellationToken).ConfigureAwait(false);

        return name;
    }

    /// <summary>
    /// Runs <c>CREATE DATABASE <paramref name="database"/> TEMPLATE <paramref name="template"/></c>, retrying while a
    /// session the template's seal is ending still counts as connected.
    /// </summary>
    public static async Task CloneAsync(
        NpgsqlConnection connection,
        string database,
        string template,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _ExecuteAsync(
                        connection,
                        $"CREATE DATABASE \"{database}\" TEMPLATE \"{template}\";",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return;
            }
            catch (PostgresException e)
                when (string.Equals(e.SqlState, PostgresErrorCodes.ObjectInUse, StringComparison.Ordinal)
                    && attempt < _MaxCloneAttempts
                )
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task _SealAsync(
        NpgsqlConnection connection,
        string name,
        string templateConnectionString,
        CancellationToken cancellationToken
    )
    {
        // A clone fails while any session is connected to the template. Idle pooled connections the provisioning
        // returned would stay open, so clear this process's pool, then refuse new connections and end every session
        // still in the database, whichever pool or process opened it.
        await using (var pooled = new NpgsqlConnection(templateConnectionString))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await _ExecuteAsync(connection, $"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS false;", cancellationToken)
            .ConfigureAwait(false);

        var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid();",
            connection
        );
        await using (terminate.ConfigureAwait(false))
        {
            terminate.Parameters.AddWithValue("name", name);
            await terminate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _ExecuteAsync(connection, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE true;", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task _EvictStaleAsync(
        NpgsqlConnection connection,
        string current,
        CancellationToken cancellationToken
    )
    {
        var stale = new List<string>();

        var list = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE starts_with(datname, @prefix) ORDER BY oid DESC OFFSET @keep;",
            connection
        );
        await using (list.ConfigureAwait(false))
        {
            list.Parameters.AddWithValue("prefix", Prefix);
            list.Parameters.AddWithValue("keep", MaxTemplates);

            var reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    stale.Add(reader.GetString(0));
                }
            }
        }

        foreach (var name in stale.Where(name => !string.Equals(name, current, StringComparison.Ordinal)))
        {
            // A held lock means another process is building or rebuilding that template right now.
            if (await _TryLockAsync(connection, name, cancellationToken).ConfigureAwait(false))
            {
                await _DropAsync(connection, name, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> _IsReadyAsync(
        string connectionString,
        string name,
        CancellationToken cancellationToken
    )
    {
        var connection = new NpgsqlConnection(connectionString);
        await using var _ = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new NpgsqlCommand("SELECT datistemplate FROM pg_database WHERE datname = @name;", connection);
        await using var __ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("name", name);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static async Task _DropAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        var exists = new NpgsqlCommand("SELECT datistemplate FROM pg_database WHERE datname = @name;", connection);
        object? isTemplate;
        await using (exists.ConfigureAwait(false))
        {
            exists.Parameters.AddWithValue("name", name);
            isTemplate = await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        if (isTemplate is null)
        {
            return;
        }

        // PostgreSQL refuses to drop a database still marked as a template.
        if (isTemplate is true)
        {
            await _ExecuteAsync(connection, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE false;", cancellationToken)
                .ConfigureAwait(false);
        }

        // FORCE ends a provisioning session a dead builder's pool may still hold.
        await _ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);", cancellationToken, 120)
            .ConfigureAwait(false);
    }

    private static async Task _LockAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@name, 0));", connection);
        await using var _ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("name", _LockName(name));
        command.CommandTimeout = _BuildLockTimeoutSeconds;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException e) when (e.InnerException is TimeoutException)
        {
            throw new TimeoutException(
                $"Waited {_BuildLockTimeoutSeconds} seconds for another session to build test template database {name}.",
                e
            );
        }
    }

    private static async Task<bool> _TryLockAsync(
        NpgsqlConnection connection,
        string name,
        CancellationToken cancellationToken
    )
    {
        var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@name, 0));", connection);
        await using var _ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("name", _LockName(name));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static async Task _ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        int? commandTimeout = null
    )
    {
        var command = new NpgsqlCommand(sql, connection);
        await using var _ = command.ConfigureAwait(false);

        if (commandTimeout is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _LockName(string name)
    {
        return "headless_testing_template:" + name;
    }
}
#pragma warning restore CA2100
