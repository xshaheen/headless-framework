// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sequences.SqlServer;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>Chooses SQL Server as the sequences provider.</summary>
[PublicAPI]
public static class SetupSequencesSqlServer
{
    extension(HeadlessSequencesSetupBuilder setup)
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
        public HeadlessSequencesSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>Stores counters in SQL Server, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SqlClient connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SqlClient connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessSequencesSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores counters in SQL Server, binding <see cref="SqlServerSequencesOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerSequencesOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores counters in SQL Server, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlServer(Action<SqlServerSequencesOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSequencesOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores counters in SQL Server, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="RelationalSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UseSqlServer(Action<SqlServerSequencesOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSequencesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerSequencesOptionsExtension : ISequencesProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerSequencesOptions>? _configure;
        private readonly Action<SqlServerSequencesOptions, IServiceProvider>? _configureWithServices;

        public SqlServerSequencesOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerSequencesOptionsExtension(Action<SqlServerSequencesOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerSequencesOptionsExtension(Action<SqlServerSequencesOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerSequencesOptions, SqlServerSequencesOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerSequencesOptions, SqlServerSequencesOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerSequencesOptions, SqlServerSequencesOptionsValidator>(
                    _configureWithServices
                );
            }

            // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its
            // contract: false means the runner never creates the table (a migration tool owns it), while the
            // initializer promise still completes for dependents.
            services.AddHeadlessSchemaContribution(sp =>
                SqlServerSequencesSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<SqlServerSequencesOptions>>().Value
                )
            );
            // The store waits between deadlock retries on this clock.
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISequenceStore>(sp => new RelationalSequenceStore(
                SqlServerDialect.Instance,
                sp.GetRequiredService<IOptions<SqlServerSequencesOptions>>().Value,
                "Headless.Sequences.SqlServer",
                sp.GetRequiredService<TimeProvider>()
            ));
        }
    }
}
