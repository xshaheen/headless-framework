// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination.SqlServer;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.Configuration;
using Extension = Headless.Coordination.RelationalCoordinationProviderExtension<
    Headless.Coordination.SqlServer.SqlServerCoordinationOptions,
    Headless.Coordination.SqlServer.SqlServerCoordinationOptionsValidator,
    Headless.Coordination.SqlServer.SqlServerCoordinationStorageOptionsValidator
>;

namespace Headless.Coordination;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting SQL Server as the
/// coordination backing store.
/// </summary>
[PublicAPI]
public static class SetupCoordinationSqlServer
{
    extension(HeadlessCoordinationSetupBuilder setup)
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
        public HeadlessCoordinationSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>
        /// Selects SQL Server as the coordination backing store using the supplied connection string.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string. Must not be null, empty, or whitespace.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Selects SQL Server as the coordination backing store, binding
        /// <see cref="SqlServerCoordinationOptions"/> from the supplied <see cref="IConfiguration"/> section.
        /// </summary>
        /// <param name="configuration">The configuration section to bind provider options from.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>
        /// Selects SQL Server as the coordination backing store using the supplied options delegate.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="SqlServerCoordinationOptions"/>.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlServer(Action<SqlServerCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Selects SQL Server as the coordination backing store using the supplied options delegate with
        /// access to the DI container.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="SqlServerCoordinationOptions"/> with service-provider access.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlServer(
            Action<SqlServerCoordinationOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalCoordinationProvider<SqlServerCoordinationOptions> _Provider = new(
        SqlServerDialect.Instance,
        static options => SqlServerDialect.Instance.CreateConnection(options.ConnectionString),
        SqlServerMembershipSchemaContribution.Create
    );
}
