// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Logging.Internal;

/// <summary>
/// Passes every tag an enricher adds through the classified property names before the tag reaches a provider.
/// </summary>
/// <remarks>
/// Microsoft's logger adds enrichment tags after the redacting logger has already handled the entry, so an enricher's
/// own values would otherwise reach providers raw, whichever library wrote the enricher.
/// </remarks>
internal sealed class NameClassifyingLogEnricher(ILogEnricher inner, LogPropertyRedactors redactors) : ILogEnricher
{
    public void Enrich(IEnrichmentTagCollector collector)
    {
        var classifying = NameClassifyingTagCollector.Rent(collector, redactors);

        try
        {
            inner.Enrich(classifying);
        }
        finally
        {
            // The collector outlives the call on its thread, so it lets go of this host's objects; otherwise a
            // disposed host's provider collector and redactors stay reachable until the thread enriches again.
            classifying.Inner = null!;
            classifying.Redactors = null!;
        }
    }
}

/// <inheritdoc cref="NameClassifyingLogEnricher"/>
internal sealed class NameClassifyingStaticLogEnricher(IStaticLogEnricher inner, LogPropertyRedactors redactors)
    : IStaticLogEnricher
{
    // Static enrichers run once per logger, so a fresh collector costs nothing per entry.
    public void Enrich(IEnrichmentTagCollector collector) =>
        inner.Enrich(new NameClassifyingTagCollector { Inner = collector, Redactors = redactors });
}

internal sealed class NameClassifyingTagCollector : IEnrichmentTagCollector
{
    // Dynamic enrichers run on every entry; one collector per thread keeps that path free of allocations. Enrichment
    // is synchronous and does not nest, so the instance is never in use twice on one thread.
    [ThreadStatic]
    private static NameClassifyingTagCollector? _cached;

    public IEnrichmentTagCollector Inner { get; set; } = null!;

    public LogPropertyRedactors Redactors { get; set; } = null!;

    public static NameClassifyingTagCollector Rent(IEnrichmentTagCollector inner, LogPropertyRedactors redactors)
    {
        var collector = _cached ??= new NameClassifyingTagCollector();
        collector.Inner = inner;
        collector.Redactors = redactors;

        return collector;
    }

    public void Add(string tagName, object tagValue)
    {
        if (!Redactors.TryGetRedactor(tagName, out var redactor))
        {
            Inner.Add(tagName, tagValue);

            return;
        }

        Inner.Add(tagName, LogPropertyRedactors.Redact(redactor, tagValue));
    }
}
