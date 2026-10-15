// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Jobs.Internal;

/// <summary>
/// The W3C <c>traceparent</c> a job row stores to tie its execution back to the span that scheduled it.
/// </summary>
/// <remarks>
/// The value is written in W3C form directly rather than through the host's configured propagator: the column is
/// sized for exactly one W3C header, and a host configured for another format (B3, Jaeger) would otherwise store a
/// value the execution side could not read back. Only the trace and span ids matter for the link, so
/// <c>tracestate</c> is not kept.
/// </remarks>
internal static class JobTraceParent
{
    /// <summary>Length of a version-00 W3C <c>traceparent</c>: <c>00-{32 hex}-{16 hex}-{2 hex}</c>.</summary>
    internal const int MaxLength = 55;

    /// <summary>
    /// Returns the <c>traceparent</c> of <paramref name="activity"/>, or of <see cref="Activity.Current"/> when no
    /// span was created, or <see langword="null"/> when neither carries a W3C context. A context is never invented.
    /// </summary>
    internal static string? Capture(Activity? activity)
    {
        var source = activity ?? Activity.Current;

        // A hierarchical id has no trace or span id to link to, so it is treated the same as no context.
        return source is { IdFormat: ActivityIdFormat.W3C } ? source.Id : null;
    }

    /// <summary>
    /// Reads a stored <c>traceparent</c> as a link. Returns <see langword="false"/> for a null or unreadable value, so
    /// a row written by hand or by an older format starts an unlinked trace instead of failing its execution.
    /// </summary>
    internal static bool TryGetLink(string? traceParent, out ActivityLink link)
    {
        if (
            traceParent is not null
            && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var context)
        )
        {
            link = new ActivityLink(context);
            return true;
        }

        link = default;
        return false;
    }
}
