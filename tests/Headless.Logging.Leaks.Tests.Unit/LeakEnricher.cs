// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Tests;

/// <summary>
/// A consumer's own enricher: it adds the tag a scenario sets around its log call, and nothing otherwise, so the other
/// scenarios log exactly what they did before.
/// </summary>
internal sealed class LeakEnricher : ILogEnricher
{
    private static readonly AsyncLocal<KeyValuePair<string, string>?> _Current = new();

    public static IDisposable Add(string name, string value)
    {
        _Current.Value = new(name, value);

        return new Reset();
    }

    public void Enrich(IEnrichmentTagCollector collector)
    {
        if (_Current.Value is { } tag)
        {
            collector.Add(tag.Key, tag.Value);
        }
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => _Current.Value = null;
    }
}
