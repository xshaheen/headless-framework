// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Tests;

/// <summary>
/// Provider fixture for the coordinator conformance scenarios. A fixture supplies isolated
/// <see cref="CommitCoordinationSession" />s; the harness opens every scope through the session's
/// <see cref="ICommitScopeFactory.Open" /> so the scenarios exercise exactly the public contract.
/// </summary>
public interface ICommitCoordinationFixture
{
    /// <summary>
    /// Creates an isolated coordination session: a fresh ambient stack, a factory over it whose every
    /// <see cref="ICommitScopeFactory.Open" /> starts a new root, and a per-session capture of the
    /// coordinator's log output (so a scenario can assert on an ignored conflicting signal).
    /// </summary>
    CommitCoordinationSession CreateSession();
}

/// <summary>
/// One isolated coordination environment handed to a scenario.
/// </summary>
public sealed class CommitCoordinationSession(
    ICommitScopeFactory factory,
    ICurrentCommitCoordinator ambient,
    IReadOnlyCollection<LogEntry> logs
)
{
    /// <summary>The factory that opens scopes on this session's ambient stack.</summary>
    public ICommitScopeFactory Factory { get; } = factory;

    /// <summary>The ambient view over the same stack.</summary>
    public ICurrentCommitCoordinator Ambient { get; } = ambient;

    /// <summary>Everything the coordinator logged in this session, in emission order.</summary>
    public IReadOnlyCollection<LogEntry> Logs { get; } = logs;

    /// <summary>Opens a new root scope; <paramref name="relational" /> is attached as the scope's relational handle.</summary>
    public ICommitScope Open(IRelationalCommitContext? relational = null)
    {
        return Factory.Open(relational);
    }
}
