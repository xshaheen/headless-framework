// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Coordination.SqlServer;

[PublicAPI]
public static class SetupSqlServerCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        public HeadlessCoordinationSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessCoordinationSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerCoordinationOptionsExtension(configuration));

            return setup;
        }

        public HeadlessCoordinationSetupBuilder UseSqlServer(Action<SqlServerCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerCoordinationOptionsExtension(configure));

            return setup;
        }

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
