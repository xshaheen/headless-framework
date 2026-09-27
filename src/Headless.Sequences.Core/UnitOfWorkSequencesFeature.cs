// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Gap-free numbering: the increment runs on the unit's own connection and transaction, so the unit's outcome
/// decides whether the number is kept. Every refusal happens before the store runs a command.
/// </summary>
internal sealed class UnitOfWorkSequencesFeature(SequenceRequestResolver resolver, ISequenceStore store)
    : IUnitOfWorkSequences
{
    private const string _Operation = "gap-free sequence";

    public async ValueTask<long> NextAsync(
        IUnitOfWork unitOfWork,
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        var (key, policy) = resolver.Resolve(name, partition);

        if (policy.Mode != SequenceMode.GapFree)
        {
            throw new InvalidOperationException(
                $"Sequence '{name}' uses fast mode, so take it through the injected ISequenceGenerator. "
                    + "unit.Sequences serves only names registered as gap-free, because it holds the counter's row "
                    + "lock until the unit ends, and a fast counter taken from both entry points can wait on itself."
            );
        }

        // Checks the unit's state, resource kind, and transaction liveness; the provider-specific transaction type
        // is the store's to judge, so any DbTransaction passes here.
        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, _Operation);
        var resource = (IRelationalUnitOfWorkResource)unitOfWork.Resource!;

        store.ValidateEnlistment(resource);

        // The increment is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be
        // re-run. An owned unit replays the caller's block, which takes the number again, so it stays replayable.
        // An observed unit belongs to someone else's commit edge (the EF save pipeline's own save), whose replay
        // would restore a tracked entity carrying a number the rolled-back counter hands out again. Marked only
        // after every check passed, so a refused call never makes the unit non-retryable.
        if (!resource.IsOwned)
        {
            unitOfWork.PreventRetry();
        }

        return await store
            .IncrementEnlistedAsync(resource, key, policy.Start, policy.Step, cancellationToken)
            .ConfigureAwait(false);
    }
}
