// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Tests;

/// <summary>
/// In-memory conformance fixture: every session is a real <see cref="CommitScopeFactory" /> over a fresh
/// <see cref="CommitScopeStack" />, with the coordinator's logger captured per session.
/// </summary>
public sealed class InMemoryCommitCoordinationFixture : ICommitCoordinationFixture
{
    public CommitCoordinationSession CreateSession()
    {
        var logger = new CapturingLogger<CommitCoordinator>();
        var stack = new CommitScopeStack();

        return new CommitCoordinationSession(new CommitScopeFactory(stack, logger), stack, logger.Entries);
    }
}
