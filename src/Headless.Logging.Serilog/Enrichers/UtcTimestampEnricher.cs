// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Serilog.Core;
using Serilog.Events;

#pragma warning disable IDE0130 // The project intentionally shares the Headless.Logging package namespace.
namespace Headless.Logging;

/// <summary>
/// Adds a <c>UtcTimestamp</c> property carrying the log event's instant expressed in UTC.
/// </summary>
/// <remarks>
/// Serilog captures <see cref="LogEvent.Timestamp"/> from <c>DateTimeOffset.Now</c> — the host's LOCAL
/// wall clock. Rendering <c>{Timestamp}</c> therefore prints local time, so the same instant reads differently
/// on two hosts in different zones and log lines will not sort correctly when merged.
/// <para>
/// <c>{Timestamp:u}</c> would convert to UTC but drops sub-second precision, which matters when reconstructing
/// an ordering. This enricher exposes the UTC instant as its own property so the output template can render it
/// at full millisecond precision.
/// </para>
/// </remarks>
public sealed class UtcTimestampEnricher : ILogEventEnricher
{
    /// <summary>The property name added to every log event.</summary>
    public const string PropertyName = "UtcTimestamp";

    /// <inheritdoc/>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        Argument.IsNotNull(logEvent);

        logEvent.AddPropertyIfAbsent(
            new LogEventProperty(PropertyName, new ScalarValue(logEvent.Timestamp.UtcDateTime))
        );
    }
}
