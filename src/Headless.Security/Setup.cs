// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless;

/// <summary>Registration helpers for the string encryption and hashing services.</summary>
/// <remarks>
/// All <c>Add*</c> members are idempotent: the first registration for a given service wins, and a later call with
/// different options is silently ignored. Configure each service once.
/// </remarks>
[PublicAPI]
public static class SetupSecurity
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddStringEncryptionService(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(config)
            );
        }

        public IServiceCollection AddStringEncryptionService(Action<StringEncryptionOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure)
            );
        }

        public IServiceCollection AddStringEncryptionService(
            Action<StringEncryptionOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure)
            );
        }

        public IServiceCollection AddStringHashService(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _AddHashCore(services, s => s.Configure<StringHashOptions, StringHashOptionsValidator>(config));
        }

        public IServiceCollection AddStringHashService(Action<StringHashOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _AddHashCore(services, s => s.Configure<StringHashOptions, StringHashOptionsValidator>(configure));
        }

        public IServiceCollection AddStringHashService(Action<StringHashOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _AddHashCore(services, s => s.Configure<StringHashOptions, StringHashOptionsValidator>(configure));
        }
    }

    private static IServiceCollection _AddEncryptionCore(IServiceCollection services, Action<IServiceCollection> bind)
    {
        if (_IsRegistered<IStringEncryptionService>(services))
        {
            return services;
        }

        bind(services);
        services.AddSingletonOptionValue<StringEncryptionOptions>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();

        return services;
    }

    private static IServiceCollection _AddHashCore(IServiceCollection services, Action<IServiceCollection> bind)
    {
        if (_IsRegistered<IStringHashService>(services))
        {
            return services;
        }

        bind(services);
        services.AddSingletonOptionValue<StringHashOptions>();
        services.TryAddSingleton<IStringHashService, StringHashService>();

        return services;
    }

    private static bool _IsRegistered<TService>(IServiceCollection services)
    {
        return services.Any(service => service.ServiceType == typeof(TService));
    }
}
