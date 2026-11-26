// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Logging.Internal;

/// <summary>
/// Adds the trace id of the span that caused the running message consume or job attempt to every entry written inside
/// it, so the entries of the request that published or scheduled the work and the work's own entries share one id.
/// </summary>
/// <remarks>
/// The entry's own <c>TraceId</c> is not enough: a job runs in a trace of its own that only links back to its
/// scheduler, and an in-process delivery runs under the consumer loop's activity. Messaging and Jobs put the stored
/// trace id on the <see cref="CausalScope"/> they open around that work. The tenant comes from
/// <see cref="TenantLogEnricher"/>, which reads the ambient tenant rather than the scope.
/// </remarks>
internal sealed class CausationLogEnricher : ILogEnricher
{
    internal const string CausationTraceId = "CausationTraceId";

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (CausalScope.CausationTraceId is { } traceId)
        {
            collector.Add(CausationTraceId, traceId);
        }
    }
}
