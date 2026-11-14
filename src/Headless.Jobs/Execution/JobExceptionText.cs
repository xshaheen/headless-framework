// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Bounds the exception text a failed job writes to its rows and its spans. A handler can throw with a message of any
/// size (a whole response body, a serialized payload), and every failed attempt would otherwise copy all of it into the
/// run row, the attempt row, and the trace.
/// </summary>
internal static class JobExceptionText
{
    /// <summary>The most characters a stored failure (JSON or plain reason) may hold.</summary>
    internal const int StoredMaxLength = 4096;

    /// <summary>The most characters one span tag or span status description may hold.</summary>
    internal const int TagMaxLength = 1024;

    /// <summary>Ends text that was cut, so a reader knows the rest existed.</summary>
    internal const string TruncationMarker = "… [truncated]";

    /// <summary>Returns <paramref name="text"/> cut to <see cref="StoredMaxLength"/>, marked when cut.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    internal static string? ForStore(string? text) => text.TruncateEnd(StoredMaxLength, TruncationMarker);

    /// <summary>Returns <paramref name="text"/> cut to <see cref="TagMaxLength"/>, marked when cut.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    internal static string? ForTag(string? text) => text.TruncateEnd(TagMaxLength, TruncationMarker);
}
