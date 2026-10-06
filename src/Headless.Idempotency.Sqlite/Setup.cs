// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Idempotency.Sqlite;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Extension = Headless.Idempotency.RelationalIdempotencyProviderExtension<
    Headless.Idempotency.Sqlite.SqliteIdempotencyOptions,
    Headless.Idempotency.Sqlite.SqliteIdempotencyOptionsValidator,
    Headless.Idempotency.Sqlite.SqliteIdempotencyStorageOptionsValidator
>;

namespace Headless.Idempotency;

/// <summary>Chooses SQLite as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencySqlite
{
    extension(HeadlessIdempotencySetupBuilder setup)
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
        public HeadlessIdempotencySetupBuilder UseSqlite()
        {
            return setup.UseSqlite(
                (options, services) => options.ConnectionString = services.GetSqliteConnectionString()
            );
        }

        /// <summary>
        /// Stores idempotency records in SQLite, in the database named by <paramref name="connectionString" />.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="RelationalIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SQLite connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlite(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlite(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores idempotency records in SQLite, binding <see cref="SqliteIdempotencyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="RelationalIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlite(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new Extension(_Provider, configuration));

            return setup;
        }

        /// <summary>Stores idempotency records in SQLite, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="RelationalIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlite(Action<SqliteIdempotencyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }

        /// <summary>
        /// Stores idempotency records in SQLite, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="RelationalIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlite(Action<SqliteIdempotencyOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Extension(_Provider, configure));

            return setup;
        }
    }

    private static readonly RelationalIdempotencyProvider _Provider = new(
        SqliteDialect.Instance,
        "Headless.Idempotency.Sqlite",
        static (factory, connection, cancellationToken) =>
            factory.BeginAsync((SqliteConnection)connection, cancellationToken),
        static services => services.AddSqliteUnitOfWork(),
        SqliteIdempotencySchemaContribution.Create,
        // Deferred: a peek takes no write lock, so it reads the last committed record while a unit holds the database.
#pragma warning disable CA1849 // False positive: no async overload begins a deferred transaction, and the driver's async begin is synchronous anyway.
        static (connection, _) =>
            ValueTask.FromResult<DbTransaction>(((SqliteConnection)connection).BeginTransaction(deferred: true)),
#pragma warning restore CA1849
        EnlistedAdmissionRefusal
    );

    /// <summary>
    /// SQLite has no counter outside the transaction, unlike a PostgreSQL or SQL Server sequence: a generation drawn
    /// inside a caller's unit that then rolls back would be drawn again by the next admission, and an attempt still
    /// holding the rolled-back admission could complete the new attempt's record. Autonomous admissions commit before
    /// they return their generation, so they are safe.
    /// </summary>
    internal const string EnlistedAdmissionRefusal =
        "Headless.Idempotency.Sqlite cannot admit a key inside a caller's unit of work: SQLite has no counter that "
        + "survives the caller's rollback, so a rolled-back admission's generation could be issued again to the next "
        + "attempt. Admit through IIdempotentOperations.AdmitAsync, which commits the admission on its own; the unit "
        + "can still complete, release, fence, or set a recovery point of that admission.";
}
