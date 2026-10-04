// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.MultiTenancy;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Headless.Api.ServiceDefaults;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>
/// Adds the ambient tenant id to every log record. Reading <see cref="ICurrentTenant"/> at emission, rather than
/// opening a logging scope where the tenant is set, labels records with the tenant that is actually ambient: a nested
/// <see cref="ICurrentTenant.Change"/> relabels them, a system scope unlabels them, and a hand-written tenant loop is
/// covered with no extra call.
/// </summary>
/// <remarks>
/// Must run before the export processors. <c>OnEnd</c> runs synchronously inside the <c>ILogger.Log</c> call, on the
/// caller's async flow, which is what makes the ambient tenant readable here.
/// </remarks>
internal sealed class TenantLogRecordProcessor(ICurrentTenant currentTenant, TenantTelemetryOptions options)
    : BaseProcessor<LogRecord>
{
    private readonly bool _enabled = options.EnrichLogs;
    private readonly string _attributeName = options.LogAttributeName;

    public override void OnEnd(LogRecord data)
    {
        if (!_enabled || currentTenant.Id is not { } tenantId)
        {
            return;
        }

        var attributes = data.Attributes;

        if (attributes is null)
        {
            data.Attributes = new AppendedAttributes([], new(_attributeName, tenantId));
            return;
        }

        // Framework log templates already carry {TenantId}; a second key of the same name is invalid OTLP.
        for (var i = 0; i < attributes.Count; i++)
        {
            if (string.Equals(attributes[i].Key, _attributeName, StringComparison.Ordinal))
            {
                return;
            }
        }

        data.Attributes = new AppendedAttributes(attributes, new(_attributeName, tenantId));
    }

    /// <summary>The record's attributes plus one, without copying them.</summary>
    private sealed class AppendedAttributes(
        IReadOnlyList<KeyValuePair<string, object?>> inner,
        KeyValuePair<string, object?> appended
    ) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => inner.Count + 1;

        public KeyValuePair<string, object?> this[int index] => index == inner.Count ? appended : inner[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < inner.Count; i++)
            {
                yield return inner[i];
            }

            yield return appended;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

/// <summary>
/// Tags every span that starts while a tenant is ambient, such as database and outgoing HTTP spans. Spans that start
/// before their tenant is known (the request, consume, and job spans) are tagged where the tenant is resolved.
/// </summary>
internal sealed class TenantActivityProcessor(ICurrentTenant currentTenant, TenantTelemetryOptions options)
    : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        if (currentTenant.Id is { } tenantId)
        {
            TenantTelemetry.TagActivity(data, options, tenantId);
        }
    }
}
