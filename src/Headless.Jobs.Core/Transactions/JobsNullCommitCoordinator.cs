// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.Jobs.Transactions;

/// <summary>
/// Jobs' own fallback <see cref="ICurrentCommitCoordinator" />: registered via <c>TryAddSingleton</c> so the
/// <c>JobsManager</c> degrades to the direct-insert path when no commit coordination provider is wired. This lives in
/// Jobs (not <c>CommitCoordination.Core</c>, whose <c>AddCommitCoordination</c> registers the real coordinator stack)
/// because the null fallback exists only for hosts that use Jobs without commit coordination — the unconditional
/// <c>AddCommitCoordination</c> registration wins when present. Mirrors Messaging's null-coordinator fallback.
/// </summary>
internal sealed class JobsNullCommitCoordinator : ICurrentCommitCoordinator
{
    public ICommitCoordinator? Current => null;
}
