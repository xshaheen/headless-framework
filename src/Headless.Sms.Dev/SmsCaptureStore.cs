// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Collections;

namespace Headless.Sms.Dev;

/// <summary>
/// The in-memory <see cref="ISmsCaptureStore"/> that every development SMS sender in the process writes to.
/// </summary>
internal sealed class SmsCaptureStore(int capacity, TimeProvider timeProvider) : ISmsCaptureStore
{
    private readonly VersionedRingBuffer<CapturedSms> _buffer = new(capacity);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public int Capacity => _buffer.Capacity;

    public int Count => _buffer.Count;

    public long Version => _buffer.Version;

    /// <summary>Stamps <paramref name="draft"/> with its id, sequence, and capture time, and stores it.</summary>
    /// <param name="draft">The captured values, without an id, sequence, or capture time.</param>
    /// <returns>The stored item.</returns>
    public CapturedSms Add(CapturedSms draft)
    {
        // The clock is read under the buffer's lock, so capture times never run backwards against the sequence.
        return _buffer.Add(sequence =>
        {
            var now = _timeProvider.GetUtcNow();

            return draft with
            {
                Id = Guid.CreateVersion7(now),
                Sequence = sequence,
                CapturedAt = now,
            };
        });
    }

    public IReadOnlyList<CapturedSms> Query(SmsCaptureQuery query)
    {
        Argument.IsNotNull(query);

        return _buffer.ToListNewestFirst(item => _Matches(item, query), query.Limit);
    }

    public CapturedSms? Get(Guid id)
    {
        return _buffer.Find(item => item.Id == id);
    }

    public bool Delete(Guid id)
    {
        return _buffer.Remove(item => item.Id == id);
    }

    public int Clear()
    {
        return _buffer.Clear();
    }

    public ValueTask<long> WaitForChangeAsync(long knownVersion, CancellationToken cancellationToken = default)
    {
        return _buffer.WaitForChangeAsync(knownVersion, cancellationToken);
    }

    private static bool _Matches(CapturedSms item, SmsCaptureQuery query)
    {
        if (query.AfterSequence is { } after && item.Sequence <= after)
        {
            return false;
        }

        if (query.SenderName is not null && !string.Equals(item.SenderName, query.SenderName, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(query.Search))
        {
            return true;
        }

        var search = query.Search;

        return item.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.MessageId?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
            || item.Destinations.Any(d => d.ToString(hasPlusPrefix: true).Contains(search, StringComparison.Ordinal));
    }
}
