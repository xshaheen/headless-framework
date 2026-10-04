// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Context;

/// <summary>
/// Retrieves the correlation identifier from <see cref="Activity.Current"/>.
/// </summary>
/// <remarks>
/// Integrates with OpenTelemetry tracing activities. Returns <see langword="null"/> when no activity is active.
/// </remarks>
public sealed class ActivityCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string? CorrelationId => Activity.Current?.Id;
}
