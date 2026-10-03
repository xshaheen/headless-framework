// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sequences.Sqlite;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>Chooses SQLite as the sequences provider.</summary>
[PublicAPI]
public static class SetupSequencesSqlite
{
    extension(HeadlessSequencesSetupBuilder setup)
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
        public HeadlessSequencesSetupBuilder UseSqlite()
        {
            return setup.UseSqlite(
                (options, services) => options.ConnectionString = services.GetSqliteConnectionString()
            );
        }

        /// <summary>Stores counters in SQLite, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SQLite connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessSequencesSetupBuilder UseSqlite(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlite(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores counters in SQLite, binding <see cref="SqliteSequencesOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlite(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqliteSequencesOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores counters in SQLite, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlite(Action<SqliteSequencesOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqliteSequencesOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores counters in SQLite, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SQLite connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlite(Action<SqliteSequencesOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqliteSequencesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqliteSequencesOptionsExtension : ISequencesProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqliteSequencesOptions>? _configure;
        private readonly Action<SqliteSequencesOptions, IServiceProvider>? _configureWithServices;

        public SqliteSequencesOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqliteSequencesOptionsExtension(Action<SqliteSequencesOptions> configure)
        {
            _configure = configure;
        }

        public SqliteSequencesOptionsExtension(Action<SqliteSequencesOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqliteSequencesOptions, SqliteSequencesOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqliteSequencesOptions, SqliteSequencesOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqliteSequencesOptions, SqliteSequencesOptionsValidator>(_configureWithServices);
            }

            // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its
            // contract: false means the runner never creates the table (a migration tool owns it), while the
            // initializer promise still completes for dependents.
            services.AddHeadlessSchemaContribution(sp =>
                SqliteSequencesSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<SqliteSequencesOptions>>().Value
                )
            );
            // The store waits between deadlock retries on this clock.
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISequenceStore>(sp => new RelationalSequenceStore(
                SqliteDialect.Instance,
                sp.GetRequiredService<IOptions<SqliteSequencesOptions>>().Value,
                "Headless.Sequences.Sqlite",
                sp.GetRequiredService<TimeProvider>()
            ));
        }
    }
}
