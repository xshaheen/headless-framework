// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing.Sqlite;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Extension = Headless.Fencing.RelationalFencingProviderExtension<
    Headless.Fencing.Sqlite.SqliteFencingOptions,
    Headless.Fencing.Sqlite.SqliteFencingOptionsValidator,
    Headless.Fencing.Sqlite.SqliteFencingStorageOptionsValidator
>;

namespace Headless.Fencing;

/// <summary>Chooses SQLite as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingSqlite
{
    extension(HeadlessFencingSetupBuilder setup)
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
        public HeadlessFencingSetupBuilder UseSqlite()
        {
            return setup.UseSqlite(
                (options, services) => options.ConnectionString = services.GetSqliteConnectionString()
            );
        }

        /// <summary>Stores leases in SQLite, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SQLite connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessFencingSetupBuilder UseSqlite(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlite(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores leases in SQLite, binding <see cref="SqliteFencingOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlite(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>Stores leases in SQLite, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlite(Action<SqliteFencingOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Stores leases in SQLite, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="RelationalFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlite(Action<SqliteFencingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalFencingProvider _Provider = new(
        SqliteDialect.Instance,
        "Headless.Fencing.Sqlite",
        static (factory, connection, cancellationToken) =>
            factory.BeginAsync((SqliteConnection)connection, cancellationToken),
        static services => services.AddUnitOfWork(),
        SqliteFencingSchemaContribution.Create,
        EnlistedGrantRefusal
    );

    /// <summary>
    /// SQLite has no counter outside the transaction, unlike a PostgreSQL or SQL Server sequence: a generation drawn
    /// inside a caller's unit that then rolls back would be drawn again by the next grant, so two holders could carry
    /// the same fencing token. Autonomous grants commit before they return their generation, so they are safe.
    /// </summary>
    internal const string EnlistedGrantRefusal =
        "Headless.Fencing.Sqlite cannot grant a lease inside a unit of work, including the unit an expired-lease "
        + "sweep hands its handler: SQLite has no counter that survives the unit's rollback, so a rolled-back "
        + "grant's generation could be issued again as another holder's fencing token. Grant through "
        + "IFencedLeases.GrantAsync, which commits the grant on its own; the unit can still renew, settle, release, "
        + "or fence that lease.";
}
