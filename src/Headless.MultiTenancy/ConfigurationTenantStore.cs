// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// Configuration-backed <see cref="ITenantStore"/>, bound once at startup from
/// <see cref="ConfigurationTenantStoreOptions"/> via the options system (R16). The bound
/// <see cref="IOptions{TOptions}"/> snapshot is captured once and never re-read: a configuration
/// change after startup requires a process restart to take effect — there is no change-token refresh
/// (KTD7). Normalizes and validates seed identifiers eagerly, mirroring <see cref="InMemoryTenantStore"/>:
/// two seeds whose identifiers normalize to the same value throw immediately (R20).
/// </summary>
internal sealed class ConfigurationTenantStore : ITenantStore, ITenantDirectory
{
    private readonly IReadOnlyDictionary<string, TenantInfo> _byId;
    private readonly IReadOnlyDictionary<string, TenantInfo> _byNormalizedIdentifier;

    /// <summary>Builds the immutable id and normalized-identifier lookup tables from the bound seed options.</summary>
    /// <param name="options">The bound seed data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A seed's <c>Id</c> or <c>Identifier</c> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two or more seeds normalize to the same identifier, or two or more seeds share the same tenant id.
    /// </exception>
    public ConfigurationTenantStore(IOptions<ConfigurationTenantStoreOptions> options)
    {
        Argument.IsNotNull(options);

        var seeds = Argument.IsNotNull(options.Value).Tenants;

        var entries = new List<(string RawIdentifier, TenantInfo Tenant)>(seeds.Count);

        foreach (var seed in seeds)
        {
            var normalizedIdentifier = seed.Identifier.Trim().ToLowerInvariant();
            var extraProperties = new ExtraProperties();

            foreach (var (key, value) in seed.ExtraProperties)
            {
                extraProperties[key] = value;
            }

            // Normal construction through TenantInfo's own validating constructor (R16) — the options
            // binder only ever produces the plain ConfigurationTenantSeed shape; the domain type itself
            // is never constructed through reflection over an uninitialized instance.
            var tenant = new TenantInfo(seed.Id, normalizedIdentifier, seed.Name, seed.IsEnabled)
            {
                ExtraProperties = extraProperties,
            };

            entries.Add((seed.Identifier, tenant));
        }

        (_byId, _byNormalizedIdentifier) = TenantSeedIndexBuilder.Build(entries, "configuration");
    }

    /// <inheritdoc/>
    public Task<TenantInfo?> FindByIdentifierAsync(
        string normalizedIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(normalizedIdentifier);
        cancellationToken.ThrowIfCancellationRequested();

        // Copy out: the catalog service's cache-miss path hands this instance straight to app code, and
        // ExtraProperties is a mutable bag — returning the bound instance would let one request's mutation
        // corrupt the process-wide seed for every later request.
        var tenant = _byNormalizedIdentifier.GetValueOrDefault(normalizedIdentifier);

        return Task.FromResult(tenant is null ? null : _Copy(tenant));
    }

    /// <inheritdoc/>
    public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        // Copy out for the same reason as FindByIdentifierAsync: never alias the bound instance.
        var tenant = _byId.GetValueOrDefault(id);

        return Task.FromResult(tenant is null ? null : _Copy(tenant));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<TenantInfo>>([.. _byId.Values.Select(_Copy)]);
    }

    /// <summary>
    /// Materializes a fresh <see cref="TenantInfo"/> per lookup, mirroring what the Entity Framework Core
    /// store gets for free from its per-query materialization. The bound instances live in singleton
    /// dictionaries for the process lifetime, so handing one out would alias store state into app code.
    /// </summary>
    private static TenantInfo _Copy(TenantInfo tenant)
    {
        return new TenantInfo(tenant.Id, tenant.Identifier, tenant.Name, tenant.IsEnabled)
        {
            ExtraProperties = new ExtraProperties(tenant.ExtraProperties),
        };
    }
}
