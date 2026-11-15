// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sequences;

/// <summary>
/// Fast-mode numbering: each call is one autonomous increment that the provider commits on its own connection, or, for
/// a policy with a <see cref="SequencePolicy.BlockSize" /> above 1, a value from a block reserved by one such increment.
/// </summary>
internal sealed class SequenceGenerator(
    SequenceRequestResolver resolver,
    ISequenceStore store,
    SequenceBlocks blocks,
    TimeProvider timeProvider
) : ISequenceGenerator
{
    public async ValueTask<long> NextAsync(
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        var (key, policy) = resolver.Resolve(name, partition);
        _EnsureFast(name, policy);

        return await _NextValueAsync(key, policy, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SequenceRange> ReserveAsync(
        string name,
        int count,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(count);

        var (key, policy) = resolver.Resolve(name, partition);
        _EnsureFast(name, policy);

        return await SequenceBlocks.ReserveAsync(store, key, policy, count, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SequenceNumber> NextNumberAsync(string name, CancellationToken cancellationToken = default)
    {
        var policy = resolver.Policy(name);
        _EnsureFast(name, policy);
        var issuedOn = SequenceNumberFormat.IssuedOn(policy, timeProvider.GetUtcNow());
        var partition = SequenceNumberFormat.Partition(policy, issuedOn);
        var (key, _) = resolver.Resolve(name, partition);

        var value = await _NextValueAsync(key, policy, cancellationToken).ConfigureAwait(false);

        return SequenceNumbers.Create(policy, value, partition, issuedOn);
    }

    private ValueTask<long> _NextValueAsync(SequenceKey key, SequencePolicy policy, CancellationToken cancellationToken)
    {
        return policy.BlockSize > 1
            ? blocks.NextAsync(key, policy, cancellationToken)
            : store.IncrementAsync(key, policy.Start, policy.Step, cancellationToken);
    }

    private static void _EnsureFast(string name, SequencePolicy policy)
    {
        switch (policy.Mode)
        {
            case SequenceMode.Fast:
                return;
            case SequenceMode.Reported:
                throw new InvalidOperationException(
                    $"Sequence '{name}' is registered as reported: its values come from the caller through "
                        + "unit.Sequences.AdvanceToAsync, so it never issues numbers."
                );
            default:
                throw new InvalidOperationException(
                    $"Sequence '{name}' is registered as gap-free, so take it through unit.Sequences on the unit of "
                        + "work that writes the number. The injected ISequenceGenerator never joins a transaction, and "
                        + "a number it hands to a caller that rolls back leaves a gap."
                );
        }
    }
}
