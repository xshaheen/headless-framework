// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.Messaging.Transactions;

/// <summary>
/// Messaging's own fallback <see cref="ICurrentCommitCoordinator" />: registered via <c>TryAddSingleton</c> so the
/// outbox writer degrades to the non-transactional immediate-dispatch path when no commit coordination provider is
/// wired. This deliberately lives in Messaging (not <c>CommitCoordination.Core</c>, whose <c>AddCommitCoordination</c>
/// registers the real <c>CommitScopeStack</c>) because the null fallback is a Messaging-opts-in-independently concern:
/// it exists only for hosts that use Messaging without commit coordination.
/// </summary>
internal sealed class MessagingNullCommitCoordinator : ICurrentCommitCoordinator
{
    public ICommitCoordinator? Current => null;
}
