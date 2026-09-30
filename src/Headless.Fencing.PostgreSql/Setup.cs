// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Fencing.PostgreSql;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Extension = Headless.Fencing.RelationalFencingProviderExtension<
    Headless.Fencing.PostgreSql.PostgreSqlFencingOptions,
    Headless.Fencing.PostgreSql.PostgreSqlFencingOptionsValidator,
    Headless.Fencing.PostgreSql.PostgreSqlFencingStorageOptionsValidator
>;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Fencing;

/// <summary>Chooses PostgreSQL as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingPostgreSql
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL storage with the connection registered by <c>AddPostgreSqlSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessFencingSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>Stores leases in PostgreSQL, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores leases in PostgreSQL, binding <see cref="PostgreSqlFencingOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>Stores leases in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(Action<PostgreSqlFencingOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Stores leases in PostgreSQL, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(Action<PostgreSqlFencingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalFencingProvider _Provider = new(
        PostgreSqlDialect.Instance,
        "Headless.Fencing.PostgreSql",
        static (factory, connection, cancellationToken) =>
            factory.BeginAsync((NpgsqlConnection)connection, IsolationLevel.ReadCommitted, cancellationToken),
        static services => services.AddPostgreSqlUnitOfWork()
    );
}
