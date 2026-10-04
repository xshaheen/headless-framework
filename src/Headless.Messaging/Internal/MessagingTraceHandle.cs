// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

/// <summary>
/// Carries the started <see cref="Activity"/> (or <see langword="null"/> when no span listener is attached) plus
/// the operation start timestamp across an emission site's before/after/error calls. A default handle
/// (<see cref="StartTimestampMs"/> is <see langword="null"/>) means no listener was attached when the operation
/// began, so the after/error paths skip all telemetry work.
/// </summary>
internal readonly record struct MessagingTraceHandle(Activity? Activity, long? StartTimestampMs)
{
    /// <summary>Whether the operation began with a span or metric listener attached.</summary>
    public bool IsRecording => StartTimestampMs.HasValue;
}
