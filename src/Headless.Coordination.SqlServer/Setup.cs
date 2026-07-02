// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Coordination.SqlServer;

/// <summary>
/// Extension members on <see cref="HeadlessCoordinationSetupBuilder"/> for selecting SQL Server as the
/// coordination backing store.
/// </summary>
[PublicAPI]
public static class SetupSqlServerCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
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

            setup.RegisterExtension(new SqlServerCoordinationOptionsExtension(configuration));

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

            setup.RegisterExtension(new SqlServerCoordinationOptionsExtension(configure));

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

            setup.RegisterExtension(new SqlServerCoordinationOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerCoordinationOptionsExtension
        : CoordinationProviderOptionsExtensionBase<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>
    {
        public SqlServerCoordinationOptionsExtension(IConfiguration configuration)
            : base(configuration) { }

        public SqlServerCoordinationOptionsExtension(Action<SqlServerCoordinationOptions> configure)
            : base(configure) { }

        public SqlServerCoordinationOptionsExtension(Action<SqlServerCoordinationOptions, IServiceProvider> configure)
            : base(configure) { }

        protected override void AddProviderServices(IServiceCollection services)
        {
            services.AddCoordinationCore<SqlServerMembershipStore>();
            _AddSqlServerCoordinationProviderCore(services);
        }
    }

    private static void _AddSqlServerCoordinationProviderCore(IServiceCollection services)
    {
        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<SqlServerMembershipStore>());
        services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
            sp.GetRequiredService<SqlServerMembershipStorageInitializer>()
        );
        services.AddInitializerHostedService<SqlServerMembershipStorageInitializer>();
    }
}
