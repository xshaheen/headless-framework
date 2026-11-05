// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A sink for the dashboard's operator log: one entry for every command the shell accepts, from the dashboard's UI or
/// an API client, so an operator's interventions can be audited.
/// </summary>
/// <remarks>
/// <para>
/// The shell writes one entry per command call it runs, one when an Operation is confirmed, and one per Operation
/// chunk. A call the shell refuses (permission, schema, reason, confirmation, or a disabled command) writes none.
/// </para>
/// <para>
/// Several sinks may be registered; every entry goes to each, in registration order. The dashboard registers a default
/// sink that writes each entry as a structured <c>ILogger</c> event under the
/// <c>Headless.Dashboard.OperatorLog</c> category. Add a sink with
/// <c>services.AddSingleton&lt;IDashboardOperatorLog, MySink&gt;()</c>; sinks are resolved as singletons and called
/// concurrently, so they must be thread-safe. An entry is written after the change it records was made, so a sink that
/// throws fails the request but does not undo the change.
/// </para>
/// <para>
/// Implemented by hosts. New members arrive as default interface members; <see cref="WriteAsync"/> never gains a
/// parameter, and <see cref="OperatorLogEntry"/> grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardOperatorLog
{
    /// <summary>Records one operator-log entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">
    /// Not cancelled when the operator's request ends: the change the entry records was already made.
    /// </param>
    ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken);
}

/// <summary>Writes each operator-log entry to every registered <see cref="IDashboardOperatorLog"/>.</summary>
internal sealed class OperatorLogWriter(IEnumerable<IDashboardOperatorLog> sinks)
{
    private readonly IDashboardOperatorLog[] _sinks = [.. sinks];

    public async Task WriteAsync(OperatorLogEntry entry)
    {
        // The change the entry records has already been made; an operator closing the tab must not lose its record.
        foreach (var sink in _sinks)
        {
            await sink.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
