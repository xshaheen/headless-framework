// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Coordination.SqlServer;

[PublicAPI]
public static class SetupSqlServerCoordination
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlServerCoordination(IConfiguration configuration)
        {
            services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(configuration);

            return services._AddSqlServerCoordinationCore(configuration.GetSection("Headless:Coordination"));
        }

        public IServiceCollection AddSqlServerCoordination(Action<SqlServerCoordinationOptions> setupAction)
        {
            services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(setupAction);

            return services._AddSqlServerCoordinationCore(static _ => { });
        }

        public IServiceCollection AddSqlServerCoordination(
            Action<SqlServerCoordinationOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(setupAction);

            return services._AddSqlServerCoordinationCore(static _ => { });
        }

        private IServiceCollection _AddSqlServerCoordinationCore(IConfiguration configuration)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<SqlServerMembershipStore>(configuration);

            return services._AddSqlServerCoordinationProviderCore();
        }

        private IServiceCollection _AddSqlServerCoordinationCore(Action<CoordinationOptions> setupAction)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<SqlServerMembershipStore>(setupAction);

            return services._AddSqlServerCoordinationProviderCore();
        }

        private IServiceCollection _AddSqlServerCoordinationProviderCore()
        {
            services.TryAddSingleton<SqlServerMembershipStore>();
            services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<SqlServerMembershipStore>());
            services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
                sp.GetRequiredService<SqlServerMembershipStorageInitializer>()
            );
            services.AddInitializerHostedService<SqlServerMembershipStorageInitializer>();

            return services;
        }
    }
}
