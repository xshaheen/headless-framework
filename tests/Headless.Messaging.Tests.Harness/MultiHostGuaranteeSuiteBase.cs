// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// The multi-host guarantees that only several hosts sharing one real database and broker can show. The scheduled chaos
/// suite implements every case once per database, with sizes and fault timings far past what a unit or integration run
/// can afford, so the bodies live there and stay out of the fast tier. Declaring the cases here lets the guarantee
/// catalogue see each binding, and the chaos project's evidence test proves every leaf runs them.
/// </summary>
[PublicAPI]
public abstract class MultiHostGuaranteeSuiteBase : TestBase
{
    /// <summary>
    /// Several hosts drain one backlog of due retry rows on one database; every row's retry runs exactly once.
    /// </summary>
    [Guarantee("MSG-COORD-05")]
    public abstract Task should_run_each_retry_row_once_when_several_hosts_drain_one_backlog();

    /// <summary>
    /// Delayed rows come due while the hosts restart one after another; each row reaches the broker exactly once, none
    /// stays leased after the last host stops, and lateness stays within twice the delayed-message poll interval.
    /// </summary>
    [Guarantee("MSG-COORD-06")]
    public abstract Task should_send_each_delayed_row_once_while_hosts_restart_in_turn();

    /// <summary>
    /// A host is cut off from the broker and the database with deliveries in flight, as a crash would leave it; the
    /// other hosts, on a real coordination provider, run each of its rows exactly once and lose none.
    /// </summary>
    [Guarantee("MSG-COORD-04")]
    public abstract Task should_run_a_killed_hosts_in_flight_rows_once_on_the_other_hosts();
}
