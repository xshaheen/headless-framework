// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sql;

/// <summary>
/// Reads the connection registered by <c>AddPostgreSqlSql</c>, so a feature's PostgreSQL provider can reuse it
/// instead of repeating the connection string.
/// </summary>
[PublicAPI]
public static class HeadlessPostgreSqlSharedConnectionExtensions
{
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Returns the connection string of the <see cref="NpgsqlConnectionFactory"/> registered by
        /// <c>AddPostgreSqlSql</c>.
        /// </summary>
        /// <returns>The shared PostgreSQL connection string.</returns>
        /// <exception cref="InvalidOperationException">
        /// No <see cref="ISqlConnectionFactory"/> is registered, or the registered one is not PostgreSQL's. The
        /// provider check keeps another database's connection string from reaching Npgsql, where it would fail
        /// later with a less useful parse error.
        /// </exception>
        public string GetPostgreSqlConnectionString()
        {
            Argument.IsNotNull(services);

            return services.GetService<ISqlConnectionFactory>() switch
            {
                NpgsqlConnectionFactory factory => factory.GetConnectionString(),
                null => throw new InvalidOperationException(
                    "No shared PostgreSQL connection is registered. Call "
                        + "services.AddPostgreSqlSql(connectionString) before using the parameterless "
                        + "UsePostgreSql(), or pass the feature its own connection string."
                ),
                var other => throw new InvalidOperationException(
                    $"The registered {nameof(ISqlConnectionFactory)} is {other.GetType().Name}, not PostgreSQL's "
                        + $"{nameof(NpgsqlConnectionFactory)}. Call services.AddPostgreSqlSql(connectionString) "
                        + "before using the parameterless UsePostgreSql(), or pass the feature its own connection "
                        + "string."
                ),
            };
        }
    }
}
