// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination.Sqlite;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Microsoft.Extensions.Configuration;
using Extension = Headless.Coordination.RelationalCoordinationProviderExtension<
    Headless.Coordination.Sqlite.SqliteCoordinationOptions,
    Headless.Coordination.Sqlite.SqliteCoordinationOptionsValidator,
    Headless.Coordination.Sqlite.SqliteCoordinationStorageOptionsValidator
>;

namespace Headless.Coordination;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting SQLite as the
/// coordination backing store.
/// </summary>
[PublicAPI]
public static class SetupCoordinationSqlite
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        /// <summary>
        /// Configures SQLite storage with the connection registered by <c>AddSqliteSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqliteSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessCoordinationSetupBuilder UseSqlite()
        {
            return setup.UseSqlite(
                (options, services) => options.ConnectionString = services.GetSqliteConnectionString()
            );
        }

        /// <summary>
        /// Selects SQLite as the coordination backing store using the supplied connection string.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string. Must not be null, empty, or whitespace.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlite(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlite(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Selects SQLite as the coordination backing store, binding
        /// <see cref="SqliteCoordinationOptions"/> from the supplied <see cref="IConfiguration"/> section.
        /// </summary>
        /// <param name="configuration">The configuration section to bind provider options from.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlite(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>
        /// Selects SQLite as the coordination backing store using the supplied options delegate.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="SqliteCoordinationOptions"/>.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlite(Action<SqliteCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Selects SQLite as the coordination backing store using the supplied options delegate with
        /// access to the DI container.
        /// </summary>
        /// <param name="configure">Delegate that configures <see cref="SqliteCoordinationOptions"/> with service-provider access.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessCoordinationSetupBuilder UseSqlite(Action<SqliteCoordinationOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalCoordinationProvider<SqliteCoordinationOptions> _Provider = new(
        SqliteDialect.Instance,
        static options => SqliteDialect.Instance.CreateConnection(options.ConnectionString),
        SqliteMembershipSchemaContribution.Create
    );
}
