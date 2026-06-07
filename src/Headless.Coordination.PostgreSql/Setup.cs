// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Coordination.PostgreSql;

[PublicAPI]
public static class SetupPostgresCoordination
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

    private sealed class PostgreSqlCoordinationOptionsExtension : ICoordinationProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlCoordinationOptions>? _configure;
        private readonly Action<PostgreSqlCoordinationOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlCoordinationOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlCoordinationOptionsExtension(Action<PostgreSqlCoordinationOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlCoordinationOptionsExtension(
            Action<PostgreSqlCoordinationOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlCoordinationOptions, PostgreSqlCoordinationOptionsValidator>(
                    _configureWithServices
                );
            }

            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<PostgresMembershipStore>(static _ => { });
            _AddPostgresCoordinationProviderCore(services);
        }
    }

    private static void _AddPostgresCoordinationProviderCore(IServiceCollection services)
    {
        services.TryAddSingleton<PostgresMembershipStore>();
        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<PostgresMembershipStore>());
        services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
            sp.GetRequiredService<PostgresMembershipStorageInitializer>()
        );
        services.AddInitializerHostedService<PostgresMembershipStorageInitializer>();
    }
}
