// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// Configuration-backed <see cref="ITenantDataPlacementResolver"/>, built once from
/// <see cref="ConfigurationTenantDataPlacementOptions"/>. Lookups are dictionary reads, so it is not cached.
/// </summary>
internal sealed class ConfigurationTenantDataPlacementResolver : ITenantDataPlacementResolver
{
    private readonly Dictionary<string, TenantDataPlacement> _placements;

    public ConfigurationTenantDataPlacementResolver(IOptions<ConfigurationTenantDataPlacementOptions> options)
    {
        Argument.IsNotNull(options);

        var entries = Argument.IsNotNull(options.Value).Tenants;
        _placements = new Dictionary<string, TenantDataPlacement>(entries.Count, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // Options validation already rejected duplicate ids and empty entries at startup; Add still throws
            // rather than silently keeping one of two placements if a caller bypassed that validation.
            _placements.Add(entry.TenantId, new TenantDataPlacement(entry.Schema, entry.ConnectionString));
        }
    }

    public Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        // TenantDataPlacement is immutable, so the shared instance can be handed out as-is.
        return Task.FromResult(_placements.GetValueOrDefault(tenantId));
    }
}
