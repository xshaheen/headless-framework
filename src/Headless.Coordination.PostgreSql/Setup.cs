// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Coordination.PostgreSql;

[PublicAPI]
public static class SetupPostgreSqlCoordination
{
    extension(HeadlessCoordinationSetupBuilder setup)
    {
        public HeadlessCoordinationSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessCoordinationSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlCoordinationOptionsExtension(configuration));

            return setup;
        }

        public HeadlessCoordinationSetupBuilder UsePostgreSql(Action<PostgreSqlCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlCoordinationOptionsExtension(configure));

            return setup;
        }

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
