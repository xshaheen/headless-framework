// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Sequences;

internal sealed class UnitOfWorkSequencesFeature(
    SequenceRequestResolver resolver,
    ISequenceStore store,
    TimeProvider timeProvider
) : IUnitOfWorkSequences
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
        _EnsureGapFree(name, policy);
        _Enlist(unitOfWork);

        return await store
            .IncrementEnlistedAsync(unitOfWork, key, policy.Start, policy.Step, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<SequenceNumber> NextNumberAsync(
        IUnitOfWork unitOfWork,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        var policy = resolver.Policy(name);
        _EnsureGapFree(name, policy);
        var issuedOn = SequenceNumberFormat.IssuedOn(policy, timeProvider.GetUtcNow());
        var partition = SequenceNumberFormat.Partition(policy, issuedOn);
        var (key, _) = resolver.Resolve(name, partition);
        _Enlist(unitOfWork);

        var value = await store
            .IncrementEnlistedAsync(unitOfWork, key, policy.Start, policy.Step, cancellationToken)
            .ConfigureAwait(false);

        return SequenceNumbers.Create(policy, value, partition, issuedOn);
    }

    public async ValueTask<SequenceAdvance> AdvanceToAsync(
        IUnitOfWork unitOfWork,
        string name,
        long value,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        var (key, policy) = resolver.Resolve(name, partition);

        if (policy.Mode != SequenceMode.Reported)
        {
            throw new InvalidOperationException(
                $"Sequence '{name}' is not registered as reported, so it issues its own numbers and cannot follow "
                    + "values a caller reports. Register it with SequenceMode.Reported."
            );
        }

        _Enlist(unitOfWork);

        // A new counter has accepted nothing yet; storing one step below the start makes the start the next value
        // expected, so the first report is classified like every later one.
        var baseline = checked(policy.Start - policy.Step);
        var previous = await store
            .AdvanceEnlistedAsync(unitOfWork, key, value, baseline, cancellationToken)
            .ConfigureAwait(false);

        if (value <= previous)
        {
            return new SequenceAdvance(SequenceAdvanceStatus.Stale, previous == baseline ? null : previous);
        }

        var status =
            value == checked(previous + policy.Step) ? SequenceAdvanceStatus.Next : SequenceAdvanceStatus.Skipped;

        return new SequenceAdvance(status, previous == baseline ? null : previous);
    }

    private static void _EnsureGapFree(string name, SequencePolicy policy)
    {
        switch (policy.Mode)
        {
            case SequenceMode.GapFree:
                return;
            case SequenceMode.Reported:
                throw new InvalidOperationException(
                    $"Sequence '{name}' is registered as reported: its values come from the caller through "
                        + "unit.Sequences.AdvanceToAsync, so it never issues numbers."
                );
            default:
                throw new InvalidOperationException(
                    $"Sequence '{name}' uses fast mode, so take it through the injected ISequenceGenerator. "
                        + "unit.Sequences serves only names registered as gap-free, because it holds the counter's "
                        + "row lock until the unit ends, and a fast counter taken from both entry points can wait on "
                        + "itself."
                );
        }
    }

    private void _Enlist(IUnitOfWork unitOfWork)
    {
        // Checks the unit's state, resource kind, and transaction liveness; the provider-specific transaction type
        // is the store's to judge, so any DbTransaction passes here.
        UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, _Operation);
        var resource = (IRelationalUnitOfWorkResource)unitOfWork.Resource!;

        store.ValidateEnlistment(unitOfWork);

        // The write is not tracked by the unit's change tracker, so a replay cannot restore it; it has to be re-run.
        // An owned unit replays the caller's block, which runs it again, so it stays replayable. An observed unit
        // belongs to someone else's commit edge (the EF save pipeline's own save), whose replay would restore a
        // tracked entity carrying a value the rolled-back counter no longer holds. Marked only after every check
        // passed, so a refused call never makes the unit non-retryable.
        if (!resource.IsOwned)
        {
            unitOfWork.PreventRetry();
        }
    }
}
