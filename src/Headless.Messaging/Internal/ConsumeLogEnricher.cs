// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Messaging.Internal;

/// <summary>
/// Adds the trace id of the span that published the consumed message to every entry written while its consume
/// pipeline runs, so an operator can follow the publishing request into the consumer's lines.
/// </summary>
/// <remarks>
/// The consumer's own trace id is not enough: an in-process delivery runs on the consumer loop's flow, under an
/// activity that is not the publisher's, and a host that samples out the consume span has no consume trace at all.
/// The stored <c>traceparent</c> is the only record of the cause that survives every transport. The tenant is not
/// added here: Microsoft's enrichment keeps duplicate keys, so a <c>TenantId</c> tag would repeat the
/// <c>{TenantId}</c> hole of the framework's own consume-scope messages; the ambient-tenant log enrichment covers it.
/// </remarks>
internal sealed class ConsumeLogEnricher : ILogEnricher
{
    /// <summary>The log field holding the trace id of the span that published or scheduled the work.</summary>
    internal const string CausationTraceId = "CausationTraceId";

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (ConsumeLogScope.CausationTraceId is { } traceId)
        {
            collector.Add(CausationTraceId, traceId);
        }
    }
}
