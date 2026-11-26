// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Options;

namespace Headless.Logging.Internal;

/// <summary>
/// Adds the ambient tenant id to every entry written while a tenant is ambient, on every logging provider.
/// </summary>
/// <remarks>
/// <para>
/// Reading <see cref="ICurrentTenant"/> when the entry is written, rather than opening a scope where the tenant is set,
/// labels the entry with the tenant that is actually ambient: a nested <see cref="ICurrentTenant.Change"/> relabels it,
/// a system scope unlabels it, and a consumer, a job attempt, or a hand-written tenant loop is covered with no extra
/// call.
/// </para>
/// <para>
/// Microsoft's enrichment keeps a tag whose key the entry already has, so an entry whose template has a
/// <c>{TenantId}</c> hole, as the framework's own consume and job messages do, keeps that value and gets no second key.
/// </para>
/// </remarks>
internal sealed class TenantLogEnricher(IServiceProvider services, IOptions<TenantTelemetryOptions> options)
    : ILogEnricher
{
    private readonly bool _enabled = options.Value.EnrichLogs;
    private readonly string _propertyName = options.Value.LogAttributeName;

    // Resolved on first use, not with the logger factory: a tenant implementation that logs would otherwise need the
    // factory that is being built.
    private ICurrentTenant? _currentTenant;
    private bool _resolved;

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (!_enabled || _CurrentTenant()?.Id is not { } tenantId || LoggedEntry.HasProperty(_propertyName))
        {
            return;
        }

        collector.Add(_propertyName, tenantId);
    }

    private ICurrentTenant? _CurrentTenant()
    {
        if (!_resolved)
        {
            // A host with no tenancy registers no ICurrentTenant; its entries get no tenant.
            _currentTenant = services.GetService<ICurrentTenant>();
            Volatile.Write(ref _resolved, true);
        }

        return _currentTenant;
    }
}
