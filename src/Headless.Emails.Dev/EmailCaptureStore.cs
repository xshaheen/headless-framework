// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Collections;

namespace Headless.Emails.Dev;

/// <summary>
/// The in-memory <see cref="IEmailCaptureStore"/> that every development email sender in the process writes to.
/// </summary>
internal sealed class EmailCaptureStore(int capacity, TimeProvider timeProvider) : IEmailCaptureStore
{
    private readonly VersionedRingBuffer<CapturedEmail> _buffer = new(capacity);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public int Capacity => _buffer.Capacity;

    public int Count => _buffer.Count;

    public long Version => _buffer.Version;

    /// <summary>Stamps <paramref name="draft"/> with its id, sequence, and capture time, and stores it.</summary>
    /// <param name="draft">The captured values, without an id, sequence, or capture time.</param>
    /// <returns>The stored item.</returns>
    public CapturedEmail Add(CapturedEmail draft)
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

    public IReadOnlyList<CapturedEmail> Query(EmailCaptureQuery query)
    {
        Argument.IsNotNull(query);

        return _buffer.ToListNewestFirst(item => _Matches(item, query), query.Limit);
    }

    public CapturedEmail? Get(Guid id)
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

    private static bool _Matches(CapturedEmail item, EmailCaptureQuery query)
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

        return _Contains(item.Subject, search)
            || _Contains(item.TextBody, search)
            || _Contains(item.HtmlBody, search)
            || _ContainsAddress(item.From, search)
            || item.To.Any(a => _ContainsAddress(a, search))
            || item.Cc.Any(a => _ContainsAddress(a, search))
            || item.Bcc.Any(a => _ContainsAddress(a, search));
    }

    private static bool _ContainsAddress(EmailRequestAddress address, string search)
    {
        return _Contains(address.EmailAddress, search) || _Contains(address.DisplayName, search);
    }

    private static bool _Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }
}
