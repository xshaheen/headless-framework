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

    private sealed class SqlServerCoordinationOptionsExtension : ICoordinationProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerCoordinationOptions>? _configure;
        private readonly Action<SqlServerCoordinationOptions, IServiceProvider>? _configureWithServices;

        public SqlServerCoordinationOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerCoordinationOptionsExtension(Action<SqlServerCoordinationOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerCoordinationOptionsExtension(
            Action<SqlServerCoordinationOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerCoordinationOptions, SqlServerCoordinationOptionsValidator>(
                    _configureWithServices
                );
            }

            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.AddCoordinationCore<SqlServerMembershipStore>(static _ => { });
            _AddSqlServerCoordinationProviderCore(services);
        }
    }

    private static void _AddSqlServerCoordinationProviderCore(IServiceCollection services)
    {
        services.TryAddSingleton<SqlServerMembershipStore>();
        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<SqlServerMembershipStore>());
        services.TryAddSingleton<IMembershipStorageInitializer>(static sp =>
            sp.GetRequiredService<SqlServerMembershipStorageInitializer>()
        );
        services.AddInitializerHostedService<SqlServerMembershipStorageInitializer>();
    }
}
