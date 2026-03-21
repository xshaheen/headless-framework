// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless;

[PublicAPI]
public static class SecuritySetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddStringEncryptionService(IConfiguration config)
        {
            Argument.IsNotNull(config);

            services.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(config);
            return _AddEncryptionCore(services);
        }

        public IServiceCollection AddStringEncryptionService(Action<StringEncryptionOptions> configure)
        {
            Argument.IsNotNull(configure);

            services.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure);
            return _AddEncryptionCore(services);
        }

        public IServiceCollection AddStringEncryptionService(
            Action<StringEncryptionOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            services.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure);
            return _AddEncryptionCore(services);
        }

        public IServiceCollection AddStringHashService(IConfiguration config)
        {
            Argument.IsNotNull(config);

            services.Configure<StringHashOptions, StringHashOptionsValidator>(config);
            return _AddHashCore(services);
        }

        public IServiceCollection AddStringHashService(Action<StringHashOptions>? configure)
        {
            Argument.IsNotNull(configure);

            services.Configure<StringHashOptions, StringHashOptionsValidator>(configure);
            return _AddHashCore(services);
        }

        public IServiceCollection AddStringHashService(Action<StringHashOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            services.Configure<StringHashOptions, StringHashOptionsValidator>(configure);
            return _AddHashCore(services);
        }
    }

    private static IServiceCollection _AddEncryptionCore(IServiceCollection services)
    {
        services.AddSingletonOptionValue<StringEncryptionOptions>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();

        return services;
    }

    private static IServiceCollection _AddHashCore(IServiceCollection services)
    {
        services.AddSingletonOptionValue<StringHashOptions>();
        services.TryAddSingleton<IStringHashService, StringHashService>();

        return services;
    }
}
