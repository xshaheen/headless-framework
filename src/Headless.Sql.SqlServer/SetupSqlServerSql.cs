// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sql;

/// <summary>
/// Registration extensions for the SQL Server SQL data-access provider.
/// </summary>
[PublicAPI]
public static class SetupSqlServerSql
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the SQL Server provider: an <see cref="ISqlConnectionFactory"/> bound to
        /// <paramref name="connectionString"/>, the <see cref="IConnectionStringChecker"/> health probe,
        /// and a scoped <see cref="ISqlCurrentConnection"/> that shares one lazily-opened connection per scope.
        /// </summary>
        /// <param name="connectionString">
        /// The SQL Server connection string used for every created connection. Must not be
        /// <see langword="null"/>, empty, or whitespace.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or whitespace.</exception>
        public IServiceCollection AddSqlServerSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            services.AddSingleton<ISqlConnectionFactory>(new SqlServerConnectionFactory(connectionString));

            return _AddSqlServerSqlCore(services);
        }

        /// <summary>
        /// Registers the SQL Server provider, resolving the connection string from the
        /// <see cref="IServiceProvider"/> at first use. Use this overload when the connection string
        /// depends on other registered services (for example a secrets provider).
        /// </summary>
        /// <param name="connectionStringFactory">
        /// Factory that produces the SQL Server connection string from the resolved service provider.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionStringFactory"/> is <see langword="null"/>.</exception>
        public IServiceCollection AddSqlServerSql(Func<IServiceProvider, string> connectionStringFactory)
        {
            Argument.IsNotNull(connectionStringFactory);

            services.AddSingleton<ISqlConnectionFactory>(sp => new SqlServerConnectionFactory(
                connectionStringFactory(sp)
            ));

            return _AddSqlServerSqlCore(services);
        }
    }

    private static IServiceCollection _AddSqlServerSqlCore(IServiceCollection services)
    {
        services.TryAddSingleton<IConnectionStringChecker, SqlServerConnectionStringChecker>();
        services.TryAddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();

        return services;
    }
}
