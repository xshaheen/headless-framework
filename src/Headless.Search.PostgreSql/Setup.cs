// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Schema;
using Headless.Search.PostgreSql;
using Headless.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Search;

/// <summary>Chooses PostgreSQL as the search provider.</summary>
[PublicAPI]
public static class SetupSearchPostgreSql
{
    extension(HeadlessSearchSetupBuilder setup)
    {
        /// <summary>
        /// Creates the search functions in the database of the connection registered by <c>AddPostgreSqlSql</c>, so
        /// one connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessSearchSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>Creates the search functions in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The functions are created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessSearchSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Creates the search functions in PostgreSQL, binding <see cref="PostgreSqlSearchOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The functions are created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessSearchSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlSearchOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Creates the search functions in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The functions are created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSearchSetupBuilder UsePostgreSql(Action<PostgreSqlSearchOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSearchOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Creates the search functions in PostgreSQL, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The functions are created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSearchSetupBuilder UsePostgreSql(Action<PostgreSqlSearchOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSearchOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlSearchOptionsExtension : ISearchProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlSearchOptions>? _configure;
        private readonly Action<PostgreSqlSearchOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlSearchOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlSearchOptionsExtension(Action<PostgreSqlSearchOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlSearchOptionsExtension(Action<PostgreSqlSearchOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlSearchOptions, PostgreSqlSearchOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlSearchOptions, PostgreSqlSearchOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlSearchOptions, PostgreSqlSearchOptionsValidator>(_configureWithServices);
            }

            // The contribution factory reads options at first resolution, so a configured schema reaches the step.
            services.AddHeadlessSchemaContribution(sp =>
                PostgreSqlArabicSearchSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<PostgreSqlSearchOptions>>().Value
                )
            );
        }
    }
}
