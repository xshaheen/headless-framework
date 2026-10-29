// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Context;

/// <summary>
/// Reads the correlation ID from the W3C trace ID of <see cref="Activity.Current"/>.
/// </summary>
/// <remarks>
/// Returns the 32-character lowercase hex trace ID, so every span in one trace, and every row or log written under
/// it, shares one correlation ID. The span part of <see cref="Activity.Id"/> differs per span, which would split one
/// request across several IDs. Works automatically for OpenTelemetry users. Returns <see langword="null"/> when no
/// activity is active, or when the activity uses the legacy hierarchical ID format, which carries no trace ID.
/// </remarks>
public sealed class ActivityCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string? CorrelationId =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.TraceId.ToHexString() : null;
}
