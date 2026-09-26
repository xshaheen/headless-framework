// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Headless.Fencing;

/// <summary>
/// Validates a call's arguments and turns them into the lease key every entry point uses, so the autonomous and
/// enlisted surfaces cannot drift apart on what a valid lease is.
/// </summary>
internal sealed class LeaseRequestResolver(ICurrentTenant currentTenant, IOptionsMonitor<FencingOptions> options)
{
    /// <summary>Validates a grant's arguments and the current tenant, and returns the lease key.</summary>
    /// <exception cref="ArgumentException">The kind, resource, or current tenant id is invalid.</exception>
    public LeaseKey Resolve(string kind, string resource)
    {
        _ValidateKind(kind, nameof(kind));
        _ValidateResource(resource, nameof(resource));

        // Read on every call, never cached: the same singleton serves every tenant, and a caller may change tenant
        // between two calls.
        var tenantId = _NormalizeTenantId(currentTenant.Id, "The current tenant id", "tenantId");

        return new LeaseKey(tenantId, kind, resource);
    }

    /// <summary>
    /// Validates a lease handed back by the caller and returns its key. The lease's own tenant is used, not the
    /// current one, because the generation belongs to the identity it was granted under.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    public static LeaseKey ResolveLease(FencedLease lease)
    {
        Argument.IsNotNull(lease);
        _ValidateKind(lease.Kind, nameof(lease));
        _ValidateResource(lease.Resource, nameof(lease));
        Argument.IsPositive(lease.Generation, "A lease generation must be positive.", nameof(lease));

        var tenantId = _NormalizeTenantId(lease.TenantId, "The lease's tenant id", nameof(lease));

        return new LeaseKey(tenantId, lease.Kind, lease.Resource);
    }

    /// <summary>Validates the kind of a sweep or purge, which spans every tenant.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind" /> is invalid.</exception>
    public static string ResolveKind(string kind)
    {
        _ValidateKind(kind, nameof(kind));

        return kind;
    }

    /// <summary>Checks a grant or renewal duration against the configured bounds.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the bounds.</exception>
    public TimeSpan ValidateDuration(TimeSpan duration)
    {
        // Read on every call so a bound changed through options reload applies to the next grant.
        var current = options.CurrentValue;

        if (duration < current.MinimumLeaseDuration || duration > current.MaximumLeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                $"A lease duration must be between {current.MinimumLeaseDuration} and "
                    + $"{current.MaximumLeaseDuration}; configure the bounds through FencingOptions."
            );
        }

        return duration;
    }

    private static void _ValidateKind(string kind, string paramName)
    {
        Argument.IsNotNullOrWhiteSpace(kind, paramName: paramName);
        Argument.HasMaxLength(kind, FencingFieldLimits.KindMaxLength, paramName: paramName);
        LeaseKeyText.EnsureNoSurroundingWhitespace(kind, "kind", paramName);
    }

    private static void _ValidateResource(string resource, string paramName)
    {
        Argument.IsNotNullOrWhiteSpace(resource, paramName: paramName);
        Argument.HasMaxLength(resource, FencingFieldLimits.ResourceMaxLength, paramName: paramName);
        LeaseKeyText.EnsureNoSurroundingWhitespace(resource, "resource", paramName);
    }

    private static string _NormalizeTenantId(string? tenantId, string what, string paramName)
    {
        if (tenantId is null)
        {
            // The host scope is its own lease namespace; single-tenant applications run here.
            return string.Empty;
        }

        // Empty is refused along with whitespace because the empty string is the stored host-scope key: accepting it
        // would merge a tenant's leases into the host's.
        Argument.IsNotNullOrWhiteSpace(
            tenantId,
            $"{what} is empty or whitespace-only, so no lease can be keyed by it. Use a real tenant id, or null "
                + "for the host scope.",
            paramName
        );

        if (tenantId.Length > FencingFieldLimits.TenantIdMaxLength)
        {
            throw new ArgumentException(
                $"{what} is {tenantId.Length} characters long; a lease key allows at most "
                    + $"{FencingFieldLimits.TenantIdMaxLength}.",
                paramName
            );
        }

        LeaseKeyText.EnsureNoSurroundingWhitespace(tenantId, "tenant id", paramName);

        return tenantId;
    }
}
