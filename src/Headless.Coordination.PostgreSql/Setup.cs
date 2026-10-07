// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination.PostgreSql;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Extension = Headless.Coordination.RelationalCoordinationProviderExtension<
    Headless.Coordination.PostgreSql.PostgreSqlCoordinationOptions,
    Headless.Coordination.PostgreSql.PostgreSqlCoordinationOptionsValidator,
    Headless.Coordination.PostgreSql.PostgreSqlCoordinationStorageOptionsValidator
>;

namespace Headless.Coordination;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting PostgreSQL as the
/// coordination backing store.
/// </summary>
[PublicAPI]
public static class SetupCoordinationPostgreSql
{
    extension(HeadlessCoordinationSetupBuilder setup)
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
        public HeadlessCoordinationSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>
        /// Selects PostgreSQL as the coordination backing store using the supplied connection string.
        /// </summary>
        /// <param name="connectionString">The Npgsql connection string. Must not be null, empty, or whitespace.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
        public HeadlessCoordinationSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Selects PostgreSQL as the coordination backing store, binding
        /// <see cref="PostgreSqlCoordinationOptions"/> from the supplied <see cref="IConfiguration"/> section.
        /// </summary>
        /// <param name="configuration">The configuration section to bind provider options from.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>
        /// Selects PostgreSQL as the coordination backing store using the supplied options delegate.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="PostgreSqlCoordinationOptions"/>.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UsePostgreSql(Action<PostgreSqlCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Selects PostgreSQL as the coordination backing store using the supplied options delegate with
        /// access to the DI container.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="PostgreSqlCoordinationOptions"/> with service-provider access.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UsePostgreSql(
            Action<PostgreSqlCoordinationOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalCoordinationProvider<PostgreSqlCoordinationOptions> _Provider = new(
        PostgreSqlDialect.Instance,
        static options => options.CreateConnection(),
        PostgreSqlMembershipSchemaContribution.Create
    );
}
