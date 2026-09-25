// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>Validates a call's arguments and turns them into the counter key and policy both entry points use.</summary>
internal sealed class SequenceRequestResolver(ICurrentTenant currentTenant, IOptionsMonitor<SequencesOptions> options)
{
    /// <summary>Validates the arguments and the current tenant, and returns the counter's key and policy.</summary>
    /// <exception cref="ArgumentException">The name, partition, or current tenant id is invalid.</exception>
    public (SequenceKey Key, SequencePolicy Policy) Resolve(string name, string? partition)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.HasMaxLength(name, SequenceFieldLimits.NameMaxLength);
        SequenceKeyText.EnsureNoSurroundingWhitespace(name, "name", nameof(name));

        var storedPartition = _NormalizePartition(partition);

        // Read on every call, never cached: the same singleton serves every tenant, and a caller may change tenant
        // between two calls.
        var tenantId = _NormalizeTenantId(currentTenant.Id);

        return (new SequenceKey(tenantId, name, storedPartition), options.CurrentValue.GetPolicy(name));
    }

    private static string _NormalizePartition(string? partition)
    {
        if (string.IsNullOrEmpty(partition))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(partition))
        {
            throw new ArgumentException(
                "A sequence partition must not be whitespace-only. Pass null or an empty string for no partition.",
                nameof(partition)
            );
        }

        Argument.HasMaxLength(partition, SequenceFieldLimits.PartitionMaxLength);
        SequenceKeyText.EnsureNoSurroundingWhitespace(partition, "partition", nameof(partition));

        return partition;
    }

    private static string _NormalizeTenantId(string? tenantId)
    {
        if (tenantId is null)
        {
            // The host scope is its own counter; single-tenant applications run here.
            return string.Empty;
        }

        // Empty is refused along with whitespace because the empty string is the stored host-scope key: accepting
        // it would merge a tenant's counter into the host's.
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "The current tenant id is empty or whitespace-only, so no sequence counter can be keyed by it. Set "
                    + "a real tenant id, or null for the host scope.",
                nameof(tenantId)
            );
        }

        if (tenantId.Length > SequenceFieldLimits.TenantIdMaxLength)
        {
            throw new ArgumentException(
                $"The current tenant id is {tenantId.Length} characters long; a sequence counter key allows at most "
                    + $"{SequenceFieldLimits.TenantIdMaxLength}.",
                nameof(tenantId)
            );
        }

        SequenceKeyText.EnsureNoSurroundingWhitespace(tenantId, "tenant id", nameof(tenantId));

        return tenantId;
    }
}
