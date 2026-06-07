// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Coordination.PostgreSql;

[PublicAPI]
public static class SetupPostgresCoordination
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPostgresCoordination(IConfiguration configuration)
        {
            services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(configuration);

            return services._AddPostgresCoordinationCore(configuration.GetSection("Headless:Coordination"));
        }

        public IServiceCollection AddPostgresCoordination(Action<PostgreSqlCoordinationOptions> setupAction)
        {
            services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(setupAction);

            return services._AddPostgresCoordinationCore(static _ => { });
        }

        public IServiceCollection AddPostgresCoordination(
            Action<PostgreSqlCoordinationOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(setupAction);

            return services._AddPostgresCoordinationCore(static _ => { });
        }

        private IServiceCollection _AddPostgresCoordinationCore(IConfiguration configuration)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<PostgresMembershipStore>(configuration);

            return services._AddPostgresCoordinationProviderCore();
        }

        private IServiceCollection _AddPostgresCoordinationCore(Action<CoordinationOptions> setupAction)
        {
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<PostgresMembershipStore>(setupAction);

            return services._AddPostgresCoordinationProviderCore();
        }

        private IServiceCollection _AddPostgresCoordinationProviderCore()
        {
            services.TryAddSingleton<PostgresMembershipStore>();
            services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<PostgresMembershipStore>());
            services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
                sp.GetRequiredService<PostgresMembershipStorageInitializer>()
            );
            services.AddInitializerHostedService<PostgresMembershipStorageInitializer>();

            return services;
        }
    }
}
