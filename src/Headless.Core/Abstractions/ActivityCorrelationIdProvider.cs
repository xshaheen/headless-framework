// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Abstractions;

/// <summary>
/// Reads the correlation ID from <see cref="Activity.Current"/>.
/// Works automatically for OpenTelemetry users. Returns <see langword="null"/> if no activity is active.
/// </summary>
public sealed class ActivityCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string? CorrelationId => Activity.Current?.Id;
}
