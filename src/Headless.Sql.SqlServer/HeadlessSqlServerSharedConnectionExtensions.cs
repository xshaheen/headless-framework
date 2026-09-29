// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sql;

/// <summary>
/// Reads the connection registered by <c>AddSqlServerSql</c>, so a feature's SQL Server provider can reuse it
/// instead of repeating the connection string.
/// </summary>
[PublicAPI]
public static class HeadlessSqlServerSharedConnectionExtensions
{
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Returns the connection string of the <see cref="SqlServerConnectionFactory"/> registered by
        /// <c>AddSqlServerSql</c>.
        /// </summary>
        /// <returns>The shared SQL Server connection string.</returns>
        /// <exception cref="InvalidOperationException">
        /// No <see cref="ISqlConnectionFactory"/> is registered, or the registered one is not SQL Server's. The
        /// provider check keeps another database's connection string from reaching SqlClient, where it would fail
        /// later with a less useful parse error.
        /// </exception>
        public string GetSqlServerConnectionString()
        {
            Argument.IsNotNull(services);

            return services.GetService<ISqlConnectionFactory>() switch
            {
                SqlServerConnectionFactory factory => factory.GetConnectionString(),
                null => throw new InvalidOperationException(
                    "No shared SQL Server connection is registered. Call "
                        + "services.AddSqlServerSql(connectionString) before using the parameterless "
                        + "UseSqlServer(), or pass the feature its own connection string."
                ),
                var other => throw new InvalidOperationException(
                    $"The registered {nameof(ISqlConnectionFactory)} is {other.GetType().Name}, not SQL Server's "
                        + $"{nameof(SqlServerConnectionFactory)}. Call services.AddSqlServerSql(connectionString) "
                        + "before using the parameterless UseSqlServer(), or pass the feature its own connection "
                        + "string."
                ),
            };
        }
    }
}
