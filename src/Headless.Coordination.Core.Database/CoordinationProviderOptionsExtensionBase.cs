// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

/// <summary>
/// Shared coordination provider options-extension shape: binds provider options (config / action /
/// action-with-services) with FluentValidation, then defers provider service registration to derived types.
/// </summary>
internal abstract class CoordinationProviderOptionsExtensionBase<TOptions, TValidator> : ICoordinationProviderOptionsExtension
    where TOptions : class
    where TValidator : class, IValidator<TOptions>
{
    private readonly IConfiguration? _configuration;
    private readonly Action<TOptions>? _configure;
    private readonly Action<TOptions, IServiceProvider>? _configureWithServices;

    protected CoordinationProviderOptionsExtensionBase(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected CoordinationProviderOptionsExtensionBase(Action<TOptions> configure)
    {
        _configure = configure;
    }

    protected CoordinationProviderOptionsExtensionBase(Action<TOptions, IServiceProvider> configure)
    {
        _configureWithServices = configure;
    }

    public void AddServices(IServiceCollection services)
    {
        if (_configuration is not null)
        {
            services.Configure<TOptions, TValidator>(_configuration);
        }
        else if (_configure is not null)
        {
            services.Configure<TOptions, TValidator>(_configure);
        }
        else
        {
            services.Configure<TOptions, TValidator>(_configureWithServices);
        }

        AddProviderServices(services);
    }

    /// <summary>Registers the provider-specific store, initializer, and supporting services.</summary>
    protected abstract void AddProviderServices(IServiceCollection services);
}
