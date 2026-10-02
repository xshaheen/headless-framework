// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using FluentValidation;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Coordination;

/// <summary>What differs between relational providers: the dialect, how a connection is made, and the DDL.</summary>
/// <param name="Dialect">The provider's dialect.</param>
/// <param name="CreateConnection">Creates an unopened connection from the bound provider options.</param>
/// <param name="SchemaContribution">Builds the provider's schema contribution (its DDL, in its dialect).</param>
internal sealed record RelationalCoordinationProvider<TOptions>(
    ISqlDialect Dialect,
    Func<TOptions, DbConnection> CreateConnection,
    Func<TOptions, CoordinationStorageOptions, SchemaContribution> SchemaContribution
)
    where TOptions : RelationalCoordinationOptions;

/// <summary>What the one relational membership store runs against.</summary>
/// <param name="Dialect">The engine's dialect.</param>
/// <param name="CreateConnection">Creates an unopened connection to the database that holds the tables.</param>
/// <param name="CommandTimeoutSeconds">The timeout of every command.</param>
/// <param name="Tables">The membership tables, named by the dialect in the configured schema.</param>
internal sealed record RelationalCoordinationStorage(
    ISqlDialect Dialect,
    Func<DbConnection> CreateConnection,
    int CommandTimeoutSeconds,
    CoordinationTables Tables
);

/// <summary>
/// Registers a relational provider: its options and validators, the core membership services over the one relational
/// store, and the provider's schema contribution.
/// </summary>
internal sealed class RelationalCoordinationProviderExtension<TOptions, TOptionsValidator, TStorageValidator>
    : ICoordinationProviderOptionsExtension
    where TOptions : RelationalCoordinationOptions
    where TOptionsValidator : class, IValidator<TOptions>
    where TStorageValidator : class, IValidator<CoordinationStorageOptions>
{
    private readonly RelationalCoordinationProvider<TOptions> _provider;
    private readonly IConfiguration? _configuration;
    private readonly Action<TOptions>? _configure;
    private readonly Action<TOptions, IServiceProvider>? _configureWithServices;

    public RelationalCoordinationProviderExtension(
        RelationalCoordinationProvider<TOptions> provider,
        IConfiguration configuration
    )
    {
        _provider = provider;
        _configuration = configuration;
    }

    public RelationalCoordinationProviderExtension(
        RelationalCoordinationProvider<TOptions> provider,
        Action<TOptions> configure
    )
    {
        _provider = provider;
        _configure = configure;
    }

    public RelationalCoordinationProviderExtension(
        RelationalCoordinationProvider<TOptions> provider,
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

        services.AddOptions<CoordinationStorageOptions, TStorageValidator>();
        services.AddCoordinationCore<RelationalMembershipStore>();

        var provider = _provider;

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TOptions>>().Value;
            var schema = sp.GetRequiredService<IOptions<CoordinationStorageOptions>>().Value.Schema;

            return new RelationalCoordinationStorage(
                provider.Dialect,
                () => provider.CreateConnection(options),
                options.CommandTimeoutSeconds,
                new CoordinationTables(provider.Dialect, schema)
            );
        });
        services.TryAddSingleton<IMembershipStore>(static sp => sp.GetRequiredService<RelationalMembershipStore>());

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false means the runner never creates the tables (a migration tool owns them), while the initializer
        // promise still completes for dependents.
        services.AddHeadlessSchemaContribution(sp =>
            provider.SchemaContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<IOptions<CoordinationStorageOptions>>().Value
            )
        );
    }
}
