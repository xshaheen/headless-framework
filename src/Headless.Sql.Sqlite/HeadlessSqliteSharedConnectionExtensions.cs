// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql.Sqlite;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sql;

/// <summary>
/// Reads the connection registered by <c>AddSqliteSql</c>, so a feature's SQLite provider can reuse it
/// instead of repeating the connection string.
/// </summary>
[PublicAPI]
public static class HeadlessSqliteSharedConnectionExtensions
{
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Returns the connection string of the <see cref="SqliteConnectionFactory"/> registered by
        /// <c>AddSqliteSql</c>.
        /// </summary>
        /// <returns>The shared SQLite connection string.</returns>
        /// <exception cref="InvalidOperationException">
        /// No <see cref="ISqlConnectionFactory"/> is registered, or the registered one is not SQLite's. The
        /// provider check keeps another database's connection string from reaching Microsoft.Data.Sqlite, where it would fail
        /// later with a less useful parse error.
        /// </exception>
        public string GetSqliteConnectionString()
        {
            Argument.IsNotNull(services);

            return services.GetService<ISqlConnectionFactory>() switch
            {
                SqliteConnectionFactory factory => factory.GetConnectionString(),
                null => throw new InvalidOperationException(
                    "No shared SQLite connection is registered. Call "
                        + "services.AddSqliteSql(connectionString) before using the parameterless "
                        + "UseSqlite(), or pass the feature its own connection string."
                ),
                var other => throw new InvalidOperationException(
                    $"The registered {nameof(ISqlConnectionFactory)} is {other.GetType().Name}, not SQLite's "
                        + $"{nameof(SqliteConnectionFactory)}. Call services.AddSqliteSql(connectionString) "
                        + "before using the parameterless UseSqlite(), or pass the feature its own connection "
                        + "string."
                ),
            };
        }
    }
}
