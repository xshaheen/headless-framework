// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Fencing.SqlServer;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Extension = Headless.Fencing.RelationalFencingProviderExtension<
    Headless.Fencing.SqlServer.SqlServerFencingOptions,
    Headless.Fencing.SqlServer.SqlServerFencingOptionsValidator,
    Headless.Fencing.SqlServer.SqlServerFencingStorageOptionsValidator
>;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Fencing;

/// <summary>Chooses SQL Server as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingSqlServer
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>
        /// Configures SQL Server storage with the connection registered by <c>AddSqlServerSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqlServerSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessFencingSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>Stores leases in SQL Server, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SqlClient connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores leases in SQL Server, binding <see cref="SqlServerFencingOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>Stores leases in SQL Server, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(Action<SqlServerFencingOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Stores leases in SQL Server, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(Action<SqlServerFencingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalFencingProvider _Provider = new(
        SqlServerDialect.Instance,
        "Headless.Fencing.SqlServer",
        static (factory, connection, cancellationToken) =>
            factory.BeginAsync((SqlConnection)connection, IsolationLevel.ReadCommitted, cancellationToken),
        static services => services.AddSqlServerUnitOfWork(),
        SqlServerFencingSchemaContribution.Create
    );
}
