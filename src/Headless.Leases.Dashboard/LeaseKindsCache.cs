// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing;

namespace Headless.Leases;

/// <summary>
/// The kinds in use, kept for <see cref="Lifetime"/> per process. The kind filter loads them on every view open, and
/// the read is a distinct scan over the whole lease table, so repeating it per open tab would cost more than a list
/// that is a few seconds old.
/// </summary>
/// <remarks>A failed read is not kept, so the next request reads the store again.</remarks>
internal sealed class LeaseKindsCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private volatile Entry? _entry;

    public async ValueTask<IReadOnlyList<string>> GetAsync(
        IFencedLeaseInspector inspector,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(inspector);

        var now = _timeProvider.GetUtcNow();

        if (_entry is { } entry && now - entry.ReadAt < Lifetime)
        {
            return entry.Kinds;
        }

        var kinds = await inspector.ListKindsAsync(cancellationToken).ConfigureAwait(false);
        _entry = new Entry(kinds, now);

        return kinds;
    }

    private sealed record Entry(IReadOnlyList<string> Kinds, DateTimeOffset ReadAt);
}
