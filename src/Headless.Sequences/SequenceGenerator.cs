// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sequences;

/// <summary>Fast-mode numbering: each call is one autonomous increment that the provider commits on its own connection.</summary>
internal sealed class SequenceGenerator(SequenceRequestResolver resolver, ISequenceStore store) : ISequenceGenerator
{
    public async ValueTask<long> NextAsync(
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    )
    {
        var (key, policy) = resolver.Resolve(name, partition);
        _EnsureFast(name, policy);

        return await store.IncrementAsync(key, policy.Start, policy.Step, cancellationToken).ConfigureAwait(false);
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

        // Checked so a block that cannot fit in a long is refused before it reaches the database. A new row stores
        // the block's last value; an existing row advances by the whole block.
        var span = checked((count - 1) * policy.Step);
        var delta = checked(count * policy.Step);
        var insertValue = checked(policy.Start + span);

        var last = await store.IncrementAsync(key, insertValue, delta, cancellationToken).ConfigureAwait(false);

        return new SequenceRange(last - span, count, policy.Step);
    }

    private static void _EnsureFast(string name, SequencePolicy policy)
    {
        if (policy.Mode != SequenceMode.Fast)
        {
            throw new InvalidOperationException(
                $"Sequence '{name}' is registered as gap-free, so take it through unit.Sequences on the unit of work "
                    + "that writes the number. The injected ISequenceGenerator never joins a transaction, and a "
                    + "number it hands to a caller that rolls back leaves a gap."
            );
        }
    }
}
