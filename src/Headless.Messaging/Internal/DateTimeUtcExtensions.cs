// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Binds nullable instants to SQL parameters.
/// </summary>
/// <remarks>
/// This used to carry per-<c>DateTimeKind</c> normalization rules, because a <c>DateTime</c> could arrive with
/// any kind and each one had to be reinterpreted differently before it was safe to bind — including the subtle
/// case where <c>Unspecified</c> had to be STAMPED as UTC rather than converted, since converting would shift it
/// by the host's offset. <see cref="DateTimeOffset"/> carries its offset on the value itself, so a persisted
/// instant can no longer be ambiguous and there is nothing left to normalize: only <see langword="null"/> needs
/// handling.
/// </remarks>
internal static class DateTimeUtcExtensions
{
    /// <summary>
    /// Converts a nullable instant to a SQL parameter value, mapping <see langword="null"/> to
    /// <see cref="DBNull.Value"/>.
    /// </summary>
    internal static object ToUtcParameterValue(this DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToUniversalTime() : DBNull.Value;
    }
}
