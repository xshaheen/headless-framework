// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sequences.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sequences;

/// <summary>Chooses PostgreSQL as the sequences provider.</summary>
[PublicAPI]
public static class SetupSequencesPostgreSql
{
    extension(HeadlessSequencesSetupBuilder setup)
    {
        /// <summary>Stores counters in PostgreSQL, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="PostgreSqlSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose Npgsql connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessSequencesSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores counters in PostgreSQL, binding <see cref="PostgreSqlSequencesOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="PostgreSqlSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlSequencesOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores counters in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="PostgreSqlSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UsePostgreSql(Action<PostgreSqlSequencesOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSequencesOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores counters in PostgreSQL, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The counter table is created at host startup unless
        /// <see cref="PostgreSqlSequencesOptions.InitializeOnStartup" /> is <see langword="false" />. A gap-free call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSequencesSetupBuilder UsePostgreSql(
            Action<PostgreSqlSequencesOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSequencesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlSequencesOptionsExtension : ISequencesProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlSequencesOptions>? _configure;
        private readonly Action<PostgreSqlSequencesOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlSequencesOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlSequencesOptionsExtension(Action<PostgreSqlSequencesOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlSequencesOptionsExtension(Action<PostgreSqlSequencesOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlSequencesOptions, PostgreSqlSequencesOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlSequencesOptions, PostgreSqlSequencesOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlSequencesOptions, PostgreSqlSequencesOptionsValidator>(
                    _configureWithServices
                );
            }

            services.AddInitializerHostedService<PostgreSqlSequencesStorageInitializer>();
            services.TryAddSingleton<ISequenceStore, PostgreSqlSequenceStore>();
        }
    }
}
