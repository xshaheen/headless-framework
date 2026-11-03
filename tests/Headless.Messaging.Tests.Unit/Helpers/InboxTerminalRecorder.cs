// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Helpers;

/// <summary>
/// Records the <c>headless.messaging.inbox.terminal</c> outcomes of one test only. The meter is process-wide and test
/// classes run in parallel, so the test sends its messages under a tenant of its own and registers
/// <see cref="UseRunTag"/>, which puts that tenant on a tag only this recorder listens for.
/// </summary>
internal sealed class InboxTerminalRecorder : IDisposable
{
    private const string _RunTagName = "tests.run_tenant";

    private readonly ConcurrentQueue<string?> _outcomes = new();
    private readonly MeterListener _listener;

    public InboxTerminalRecorder()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Name, MessagingMetrics.InboxTerminalName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) => _Record(tags));
        _listener.Start();
    }

    /// <summary>The tenant to send this test's messages under.</summary>
    public string RunTenant { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The outcome tag of every terminal this test's messages recorded, in order.</summary>
    public IReadOnlyList<string?> Outcomes => [.. _outcomes];

    /// <summary>Tags inbox measurements with the message's tenant, under the tag this recorder listens for.</summary>
    public static void UseRunTag(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton(new InboxMetricPolicy(_RunTagName)));

    public void Dispose() => _listener.Dispose();

    private void _Record(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? tenant = null;
        string? outcome = null;
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, _RunTagName, StringComparison.Ordinal))
            {
                tenant = tag.Value as string;
            }
            else if (string.Equals(tag.Key, MessagingTags.InboxOutcome, StringComparison.Ordinal))
            {
                outcome = tag.Value as string;
            }
        }

        if (string.Equals(tenant, RunTenant, StringComparison.Ordinal))
        {
            _outcomes.Enqueue(outcome);
        }
    }
}
