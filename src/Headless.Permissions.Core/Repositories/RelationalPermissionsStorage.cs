// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Headless.Hosting.Initialization.Schema;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

/// <summary>Registers the relational repositories and a provider's schema contribution over one dialect.</summary>
internal static class RelationalPermissionsStorage
{
    public static void AddServices<TOptions>(
        IServiceCollection services,
        ISqlDialect dialect,
        StorageProvider provider,
        Func<TOptions, RelationalPermissionsTables, bool, SchemaContribution> createContribution
    )
        where TOptions : RelationalPermissionsOptions
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidator<PermissionsStorageOptions>>(
                new RelationalPermissionsStorageOptionsValidator(provider)
            )
        );
        services.AddOptions<PermissionsStorageOptions>().ValidateFluentValidation().ValidateOnStart();
        services.TryAddSingleton(sp => new RelationalPermissionsTables(
            dialect,
            sp.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value
        ));

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false keeps the steps out of the runner's apply pass while verify and export still see them.
        services.AddHeadlessSchemaContribution(sp =>
            createContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<RelationalPermissionsTables>(),
                sp.GetRequiredService<IOptions<PermissionsStorageOptions>>().Value.InitializeOnStartup
            )
        );

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
        services.TryAddSingleton<IPermissionGrantRepository>(sp => new RelationalPermissionGrantRepository(
            sp.GetRequiredService<RelationalPermissionsTables>(),
            sp.GetRequiredService<IOptions<TOptions>>().Value,
            sp,
            sp.GetRequiredService<TimeProvider>()
        ));
        services.TryAddSingleton<IPermissionDefinitionRecordRepository>(
            sp => new RelationalPermissionDefinitionRecordRepository(
                sp.GetRequiredService<RelationalPermissionsTables>(),
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<IJsonSerializer>()
            )
        );
    }
}
