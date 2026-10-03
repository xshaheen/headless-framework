// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using FluentValidation;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Fencing;

/// <summary>What differs between relational providers: the dialect and the unit-of-work entry for its connections.</summary>
/// <param name="Dialect">The provider's dialect.</param>
/// <param name="PackageName">The provider package's name, for messages.</param>
/// <param name="BeginOwnedUnit">Begins an owned unit at READ COMMITTED on a connection of the dialect.</param>
/// <param name="AddUnitOfWork">The provider's unit-of-work registration.</param>
/// <param name="SchemaContribution">
/// Builds the provider's schema contribution (its DDL, in its dialect) for the schema runner from the bound options.
/// </param>
/// <param name="EnlistedGrantRefusal">
/// Why the provider refuses a grant inside a caller's unit, or <see langword="null" /> when it accepts one.
/// </param>
internal sealed record RelationalFencingProvider(
    ISqlDialect Dialect,
    string PackageName,
    Func<IUnitOfWorkFactory, DbConnection, CancellationToken, ValueTask<IUnitOfWork>> BeginOwnedUnit,
    Action<IServiceCollection> AddUnitOfWork,
    Func<RelationalFencingOptions, FencingStorageOptions, SchemaContribution> SchemaContribution,
    string? EnlistedGrantRefusal = null
);

/// <summary>
/// Registers a relational provider: its options and validators, the one relational lease store over its dialect, and
/// the one storage initializer.
/// </summary>
internal sealed class RelationalFencingProviderExtension<TOptions, TOptionsValidator, TStorageValidator>
    : IFencingProviderOptionsExtension
    where TOptions : RelationalFencingOptions
    where TOptionsValidator : class, IValidator<TOptions>
    where TStorageValidator : class, IValidator<FencingStorageOptions>
{
    private readonly RelationalFencingProvider _provider;
    private readonly IConfiguration? _configuration;
    private readonly Action<TOptions>? _configure;
    private readonly Action<TOptions, IServiceProvider>? _configureWithServices;

    public RelationalFencingProviderExtension(RelationalFencingProvider provider, IConfiguration configuration)
    {
        _provider = provider;
        _configuration = configuration;
    }

    public RelationalFencingProviderExtension(RelationalFencingProvider provider, Action<TOptions> configure)
    {
        _provider = provider;
        _configure = configure;
    }

    public RelationalFencingProviderExtension(
        RelationalFencingProvider provider,
        Action<TOptions, IServiceProvider> configure
    )
    {
        _provider = provider;
        _configureWithServices = configure;
    }

    public void AddServices(IServiceCollection services)
    {
        if (_configuration is not null)
        {
            services.Configure<TOptions, TOptionsValidator>(_configuration);
        }
        else if (_configure is not null)
        {
            services.Configure<TOptions, TOptionsValidator>(_configure);
        }
        else
        {
            services.Configure<TOptions, TOptionsValidator>(_configureWithServices);
        }

        services.AddOptions<FencingStorageOptions, TStorageValidator>();

        // Sweeps begin owned units through the unit-of-work factory, and enlisted calls reach the store through
        // unit.Leases, so the factory must exist whether or not the host registered it.
        _provider.AddUnitOfWork(services);

        var provider = _provider;

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TOptions>>().Value;
            var schema = sp.GetRequiredService<IOptions<FencingStorageOptions>>().Value.Schema;

            return new RelationalFencingStorage(
                provider.Dialect,
                provider.PackageName,
                options.ConnectionString,
                options.CommandTimeoutSeconds,
                new FencingTable(provider.Dialect, schema),
                provider.BeginOwnedUnit,
                provider.EnlistedGrantRefusal
            );
        });

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false means the runner never creates the lease table (a migration tool owns it), while the initializer
        // promise still completes for dependents.
        services.AddHeadlessSchemaContribution(sp =>
            provider.SchemaContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<IOptions<FencingStorageOptions>>().Value
            )
        );
        // The store waits between deadlock retries on this clock.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILeaseStore, RelationalLeaseStore>();
    }
}
