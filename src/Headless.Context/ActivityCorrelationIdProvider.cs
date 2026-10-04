// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Context;

/// <summary>
/// Reads the correlation ID from <see cref="Activity.Current"/>.
/// </summary>
/// <remarks>
/// Works automatically for OpenTelemetry users. Returns <see langword="null"/> when no activity is active.
/// </remarks>
public sealed class ActivityCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string? CorrelationId => Activity.Current?.Id;
}
