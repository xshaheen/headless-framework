// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Sql;

/// <summary>
/// Registration extensions for the PostgreSQL SQL data-access provider.
/// </summary>
/// <remarks>
/// Both overloads also contribute the <c>sql-postgresql</c> readiness health check, which runs <c>SELECT 1</c> on a new
/// connection each time health is checked. Remove it with <c>services.RemoveHealthChecks(...)</c>.
/// </remarks>
[PublicAPI]
public static class SetupSqlPostgreSql
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the PostgreSQL provider: an <see cref="ISqlConnectionFactory"/> bound to
        /// <paramref name="connectionString"/>, the <see cref="IConnectionStringChecker"/> health probe,
        /// and a scoped <see cref="ISqlCurrentConnection"/> that shares one lazily-opened connection per scope.
        /// </summary>
        /// <param name="connectionString">
        /// The Npgsql connection string used for every created connection. Must not be
        /// <see langword="null"/>, empty, or whitespace.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or whitespace.</exception>
        public IServiceCollection AddPostgreSqlSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            services.AddSingleton<ISqlConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

            return _AddPostgreSqlSqlCore(services);
        }

        /// <summary>
        /// Registers the PostgreSQL provider, resolving the connection string from the
        /// <see cref="IServiceProvider"/> at first use. Use this overload when the connection string
        /// depends on other registered services (for example a secrets provider).
        /// </summary>
        /// <param name="connectionStringFactory">
        /// Factory that produces the Npgsql connection string from the resolved service provider.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddPostgreSqlSql(Func<IServiceProvider, string> connectionStringFactory)
        {
            Argument.IsNotNull(connectionStringFactory);

            services.AddSingleton<ISqlConnectionFactory>(sp => new NpgsqlConnectionFactory(
                connectionStringFactory(sp)
            ));

            return _AddPostgreSqlSqlCore(services);
        }
    }

    private static IServiceCollection _AddPostgreSqlSqlCore(IServiceCollection services)
    {
        services.TryAddSingleton<IConnectionStringChecker, NpgsqlConnectionStringChecker>();
        services.TryAddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
        services.AddHeadlessHealthCheck(
            "sql-postgresql",
            static (provider, cancellationToken) =>
                provider
                    .GetRequiredService<ISqlConnectionFactory>()
                    .PingAsync(
                        provider
                            .GetRequiredService<IOptionsMonitor<HeadlessHealthCheckOptions>>()
                            .Get("sql-postgresql")
                            .TestCommand
                            ?? "SELECT 1",
                        cancellationToken
                    ),
            HeadlessHealthCheckTags.Database
        );

        return services;
    }
}
