// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// One provider's <c>RunAsync</c> spelling under the replay conformance suite. The fixture wires the spelling's
/// replay loop so that <see cref="ReplayableFaultException" /> is a fault it would replay, which lets a scenario
/// prove that a refusal comes from the runner's policy and not from the classification.
/// </summary>
public interface IUnitOfWorkReplayFixture
{
    /// <summary>
    /// Whether the spelling replays an attempt that faulted before its commit started. The connection-shaped raw-ADO
    /// overloads never do: the caller owns the connection, so a replay would run on the connection that just failed.
    /// </summary>
    bool ReplaysBeforeCommit { get; }

    /// <summary>Runs <paramref name="operation" /> through the spelling on a fresh service scope.</summary>
    Task<TResult> RunAsync<TResult>(
        Func<IUnitOfWorkReplayContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    );

    /// <summary>Counts probe rows using a connection independent of any unit's transaction.</summary>
    Task<int> CountProbeRowsAsync(CancellationToken cancellationToken);

    /// <summary>Creates the probe schema when absent and deletes every probe row between scenarios.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>What a replay scenario can do inside one attempt of the spelling under test.</summary>
public interface IUnitOfWorkReplayContext : IUnitOfWorkRunContext
{
    /// <summary>
    /// Makes this attempt's commit fault before the transaction becomes durable, with a fault the fixture's replay
    /// loop would otherwise replay.
    /// </summary>
    Task ArmCommitFaultAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ends this attempt's database session from another connection, so the commit that follows is sent on a
    /// connection the server already dropped and gets no answer.
    /// </summary>
    Task BreakConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calls the same provider's <c>RunAsync</c> on this attempt's resource, which joins the attempt's unit instead
    /// of beginning another.
    /// </summary>
    Task RunJoinedAsync(Func<IUnitOfWork, CancellationToken, Task> operation, CancellationToken cancellationToken);
}

/// <summary>The fault every replay fixture's loop classifies as transient.</summary>
public sealed class ReplayableFaultException(string message = "Simulated replayable failure.") : Exception(message);
