// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Harness fixture over the internal <see cref="ConfigurationTenantStore"/>, exercised through the
/// <see cref="TenantStoreConformanceTests{TFixture}"/> suite. Each <see cref="SeedAsync"/> call builds a
/// fresh, independent store instance from a bound options snapshot (KTD7) — there is nothing to reset
/// between calls.
/// </summary>
public sealed class ConfigurationTenantCatalogStoreFixture : ITenantCatalogStoreFixture
{
    public Task<ITenantStore> SeedAsync(IReadOnlyList<TenantSeed> seeds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new ConfigurationTenantStoreOptions { Tenants = [.. seeds.Select(_ToSeed)] };

        // The store's own constructor validates uniqueness and throws InvalidOperationException on
        // duplicates (R20) — the same check the FluentValidation options validator performs at DI
        // startup, so bypassing DI here still exercises the duplicate-rejection contract.
        return Task.FromResult<ITenantStore>(new ConfigurationTenantStore(Options.Create(options)));
    }

    private static ConfigurationTenantSeed _ToSeed(TenantSeed seed)
    {
        var configSeed = new ConfigurationTenantSeed
        {
            Id = seed.Id,
            Identifier = seed.Identifier,
            Name = seed.Name,
            IsEnabled = seed.IsEnabled,
        };

        if (seed.ExtraProperties is not null)
        {
            foreach (var (key, value) in seed.ExtraProperties)
            {
                configSeed.ExtraProperties[key] = value;
            }
        }

        return configSeed;
    }
}
