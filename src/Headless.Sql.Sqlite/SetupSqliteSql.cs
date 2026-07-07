// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sql;

/// <summary>
/// Registration extensions for the SQLite SQL data-access provider.
/// </summary>
[PublicAPI]
public static class SetupSqliteSql
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the SQLite provider: an <see cref="ISqlConnectionFactory"/> bound to
        /// <paramref name="connectionString"/>, the <see cref="IConnectionStringChecker"/> health probe,
        /// and a scoped <see cref="ISqlCurrentConnection"/> that shares one lazily-opened connection per scope.
        /// </summary>
        /// <param name="connectionString">
        /// The SQLite connection string used for every created connection. Must not be
        /// <see langword="null"/>, empty, or whitespace.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or whitespace.</exception>
        public IServiceCollection AddSqliteSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            services.AddSingleton<ISqlConnectionFactory>(new SqliteConnectionFactory(connectionString));

            return _AddSqliteSqlCore(services);
        }

        /// <summary>
        /// Registers the SQLite provider, resolving the connection string from the
        /// <see cref="IServiceProvider"/> at first use. Use this overload when the connection string
        /// depends on other registered services (for example a secrets provider).
        /// </summary>
        /// <param name="connectionStringFactory">
        /// Factory that produces the SQLite connection string from the resolved service provider.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddSqliteSql(Func<IServiceProvider, string> connectionStringFactory)
        {
            Argument.IsNotNull(connectionStringFactory);

            services.AddSingleton<ISqlConnectionFactory>(sp => new SqliteConnectionFactory(
                connectionStringFactory(sp)
            ));

            return _AddSqliteSqlCore(services);
        }
    }

    private static IServiceCollection _AddSqliteSqlCore(IServiceCollection services)
    {
        services.TryAddSingleton<IConnectionStringChecker, SqliteConnectionStringChecker>();
        services.TryAddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();

        return services;
    }
}
