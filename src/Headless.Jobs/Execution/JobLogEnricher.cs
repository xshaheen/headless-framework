// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Jobs;

/// <summary>
/// Adds the trace id of the span that scheduled the running job to every entry written during its attempt, so an
/// operator can follow the scheduling request into the job's lines.
/// </summary>
/// <remarks>
/// The run's own span starts a trace of its own that only links back to its scheduler, so the entry's own trace id
/// never matches the request. The tenant is not added here: Microsoft's enrichment keeps duplicate keys, so a
/// <c>TenantId</c> tag would repeat the <c>{TenantId}</c> hole of the framework's own job-scope messages; the
/// ambient-tenant log enrichment covers it.
/// </remarks>
internal sealed class JobLogEnricher : ILogEnricher
{
    /// <summary>The log field holding the trace id of the span that published or scheduled the work.</summary>
    internal const string CausationTraceId = "CausationTraceId";

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (JobCausalContext.Current?.CausationTraceId is { } traceId)
        {
            collector.Add(CausationTraceId, traceId);
        }
    }
}
