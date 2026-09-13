// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination;

/// <summary>
/// Opens ambient commit coordination scopes. Core-internal infrastructure: provider enlistment helpers reach it
/// through <see cref="ICommitScopeFactory" />; consumers interact only through <see cref="ICurrentCommitCoordinator" />.
/// </summary>
internal sealed class CommitScopeFactory(CommitScopeStack stack, ILogger<CommitCoordinator>? logger = null)
    : ICommitScopeFactory
{
    private readonly ILogger _logger = logger ?? NullLogger<CommitCoordinator>.Instance;

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the ambient pop handle is transferred to CommitScope."
    )]
    public ICommitScope Open(IRelationalCommitContext? relational)
    {
        var coordinator = new CommitCoordinator(relational, _logger);

        // The push is synchronous in this frame so the ambient coordinator is visible to the caller immediately and
        // flows into every awaited callee; an async helper would strand it in its own execution context.
        var ambientHandle = stack.Push(coordinator);

        return new CommitScope(coordinator, ambientHandle);
    }
}
