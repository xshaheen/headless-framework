// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Coordination.PostgreSql;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting PostgreSQL as the
/// coordination backing store.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        /// <summary>
        /// Selects PostgreSQL as the coordination backing store using the supplied connection string.
        /// </summary>
        /// <param name="connectionString">The Npgsql connection string. Must not be null, empty, or whitespace.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
        public HeadlessCoordinationSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
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

            setup.RegisterExtension(new PostgreSqlCoordinationOptionsExtension(configuration));

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

            setup.RegisterExtension(new PostgreSqlCoordinationOptionsExtension(configure));

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

            setup.RegisterExtension(new PostgreSqlCoordinationOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlCoordinationOptionsExtension
        : CoordinationProviderOptionsExtensionBase<
            PostgreSqlCoordinationOptions,
            PostgreSqlCoordinationOptionsValidator
        >
    {
        public PostgreSqlCoordinationOptionsExtension(IConfiguration configuration)
            : base(configuration) { }

        public PostgreSqlCoordinationOptionsExtension(Action<PostgreSqlCoordinationOptions> configure)
            : base(configure) { }

        public PostgreSqlCoordinationOptionsExtension(Action<PostgreSqlCoordinationOptions, IServiceProvider> configure)
            : base(configure) { }

        protected override void AddProviderServices(IServiceCollection services)
        {
            services.AddCoordinationCore<PostgreSqlMembershipStore>();
            _AddPostgreSqlCoordinationProviderCore(services);
        }
    }

    private static void _AddPostgreSqlCoordinationProviderCore(IServiceCollection services)
    {
        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<PostgreSqlMembershipStore>());
        services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
            sp.GetRequiredService<PostgreSqlMembershipStorageInitializer>()
        );
        services.AddInitializerHostedService<PostgreSqlMembershipStorageInitializer>();
    }
}
