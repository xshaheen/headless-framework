// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

/// <summary>
/// One transactional inbox attempt as a provider began it: the observed-mode unit of work the handler receives, and
/// the provider transaction the inbox row completes in. Disposing it disposes the unit, then the transaction, then
/// anything the provider opened for the attempt.
/// </summary>
internal abstract class InboxAttemptTransaction : IAsyncDisposable
{
    /// <summary>The unit of work enlisted in observed mode over <see cref="Transaction" />.</summary>
    public abstract IUnitOfWork UnitOfWork { get; }

    /// <summary>The live provider transaction the inbox completion joins.</summary>
    public abstract DbTransaction Transaction { get; }

    /// <summary>Writes work the handler buffered (an EF change tracker) into the transaction before completion.</summary>
    public virtual ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public abstract Task CommitAsync(CancellationToken cancellationToken);

    public abstract Task RollbackAsync(CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();
}
