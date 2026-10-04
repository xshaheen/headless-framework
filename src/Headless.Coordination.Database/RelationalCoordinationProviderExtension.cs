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

/// <summary>
/// Registers relational provider options, validators, core membership services, and schema contributions.
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

/// <summary>Encapsulates provider-specific relational configurations: dialect, connection creation, and schema DDL.</summary>
/// <param name="Dialect">The provider SQL dialect.</param>
/// <param name="CreateConnection">Creates an unopened connection from provider options.</param>
/// <param name="SchemaContribution">Builds the schema contribution in the provider dialect.</param>
internal sealed record RelationalCoordinationProvider<TOptions>(
    ISqlDialect Dialect,
    Func<TOptions, DbConnection> CreateConnection,
    Func<TOptions, CoordinationStorageOptions, SchemaContribution> SchemaContribution
)
    where TOptions : RelationalCoordinationOptions;

/// <summary>Encapsulates runtime resources for the relational membership store.</summary>
/// <param name="Dialect">The SQL dialect.</param>
/// <param name="CreateConnection">Creates an unopened connection to the database.</param>
/// <param name="CommandTimeoutSeconds">The execution timeout for database commands in seconds.</param>
/// <param name="Tables">The membership table and column names formatted for the schema.</param>
internal sealed record RelationalCoordinationStorage(
    ISqlDialect Dialect,
    Func<DbConnection> CreateConnection,
    int CommandTimeoutSeconds,
    CoordinationTables Tables
);
