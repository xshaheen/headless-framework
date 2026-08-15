// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// In-memory <see cref="ITenantStore"/> for tests and small apps, seeded at construction from
/// <see cref="InMemoryTenantStoreOptions"/>. Normalizes and validates seed identifiers eagerly:
/// two seeds whose identifiers normalize to the same value throw immediately (R20), so a
/// misconfigured seed set fails fast rather than surfacing as a silent duplicate.
/// </summary>
internal sealed class InMemoryTenantStore : ITenantStore, ITenantDirectory
{
    private readonly IReadOnlyDictionary<string, TenantInfo> _byId;
    private readonly IReadOnlyDictionary<string, TenantInfo> _byNormalizedIdentifier;

    /// <summary>Builds the immutable id and normalized-identifier lookup tables from the seed options.</summary>
    /// <param name="options">The seed data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its value is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two or more seeds normalize to the same identifier, or two or more seeds share the same tenant id.
    /// </exception>
    public InMemoryTenantStore(IOptions<InMemoryTenantStoreOptions> options)
    {
        Argument.IsNotNull(options);

        var seeds = Argument.IsNotNull(options.Value).Tenants;

        var entries = new List<(string RawIdentifier, TenantInfo Tenant)>(seeds.Count);

        foreach (var seed in seeds)
        {
            var normalizedIdentifier = seed.Identifier.Trim().ToLowerInvariant();

            // The store contract hands back an already-normalized Identifier (TenantInfo.Identifier's
            // doc): reconstruct rather than reuse the seed instance so app-supplied mixed-case seeds
            // still round-trip normalized.
            var normalized = new TenantInfo(seed.Id, normalizedIdentifier, seed.Name, seed.IsEnabled)
            {
                ExtraProperties = new ExtraProperties(seed.ExtraProperties),
            };

            entries.Add((seed.Identifier, normalized));
        }

        (_byId, _byNormalizedIdentifier) = TenantSeedIndexBuilder.Build(entries, "in-memory");
    }

    /// <inheritdoc/>
    public Task<TenantInfo?> FindByIdentifierAsync(
        string normalizedIdentifier,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(normalizedIdentifier);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_byNormalizedIdentifier.GetValueOrDefault(normalizedIdentifier));
    }

    /// <inheritdoc/>
    public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_byId.GetValueOrDefault(id));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<TenantInfo>>([.. _byId.Values]);
    }
}
