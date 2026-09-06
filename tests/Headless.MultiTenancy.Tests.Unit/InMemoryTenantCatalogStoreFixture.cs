// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Harness fixture over the internal <see cref="InMemoryTenantStore"/>, exercised through the
/// <see cref="TenantStoreConformanceTests{TFixture}"/> suite. Each <see cref="SeedAsync"/> call builds a
/// fresh, independent store instance — the in-memory store's entire state is its seed options, so there
/// is nothing to reset between calls.
/// </summary>
public sealed class InMemoryTenantCatalogStoreFixture : ITenantCatalogStoreFixture
{
    public Task<ITenantStore> SeedAsync(IReadOnlyList<TenantSeed> seeds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new InMemoryTenantStoreOptions { Tenants = [.. seeds.Select(_ToTenantInfo)] };

        // The store's own constructor validates uniqueness and throws InvalidOperationException on
        // duplicates (R20) — the same check the FluentValidation options validator performs at DI
        // startup, so bypassing DI here still exercises the duplicate-rejection contract.
        return Task.FromResult<ITenantStore>(new InMemoryTenantStore(Options.Create(options)));
    }

    private static TenantInfo _ToTenantInfo(TenantSeed seed)
    {
        var info = new TenantInfo(seed.Id, seed.Identifier, seed.Name, seed.IsEnabled);

        if (seed.ExtraProperties is not null)
        {
            foreach (var (key, value) in seed.ExtraProperties)
            {
                info.ExtraProperties[key] = value;
            }
        }

        return info;
    }
}
