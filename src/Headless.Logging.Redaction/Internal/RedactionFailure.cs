// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging.Internal;

/// <summary>
/// What a log value becomes when redacting it threw: a log call never fails because of a value, and the raw value never
/// reaches a provider in its place.
/// </summary>
internal static class RedactionFailure
{
    /// <summary>
    /// Whether a failure is contained. Running out of memory is not a property of the value, so it is never swallowed.
    /// </summary>
    public static bool IsContained(Exception exception) => exception is not OutOfMemoryException;

    /// <summary>The value's type and the exception type, never the exception message, which can quote the value.</summary>
    public static string Text(object value, Exception exception) =>
        value.GetType() + " (redaction failed: " + exception.GetType().Name + ")";
}
