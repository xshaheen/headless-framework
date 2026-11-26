// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails.Dev;

/// <summary>
/// The in-memory <see cref="IEmailCaptureStore"/> that every development email sender in the process writes to.
/// </summary>
internal sealed class EmailCaptureStore(int capacity, TimeProvider timeProvider) : IEmailCaptureStore
{
    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    // Oldest first; capacity is small enough that RemoveAt(0) and linear lookups cost less than an index would.
    private readonly List<CapturedEmail> _items = [];
    private long _version;
    private TaskCompletionSource<long> _changed = _NewSignal();

    public int Capacity { get; } = Argument.IsPositive(capacity);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>Stamps <paramref name="draft"/> with its id, sequence, and capture time, and stores it.</summary>
    public CapturedEmail Add(CapturedEmail draft)
    {
        var now = _timeProvider.GetUtcNow();
        CapturedEmail item;
        TaskCompletionSource<long> signal;

        lock (_lock)
        {
            item = draft with { Id = Guid.CreateVersion7(now), Sequence = ++_version, CapturedAt = now };
            _items.Add(item);

            if (_items.Count > Capacity)
            {
                _items.RemoveAt(0);
            }

            signal = _SwapSignal();
        }

        signal.TrySetResult(item.Sequence);

        return item;
    }

    public IReadOnlyList<CapturedEmail> Query(EmailCaptureQuery? query = null)
    {
        if (query?.Limit is { } limit)
        {
            Argument.IsPositive(limit);
        }

        var result = new List<CapturedEmail>();

        lock (_lock)
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];

                if (query is not null && !_Matches(item, query))
                {
                    continue;
                }

                result.Add(item);

                if (result.Count == query?.Limit)
                {
                    break;
                }
            }
        }

        return result;
    }

    public CapturedEmail? Get(Guid id)
    {
        lock (_lock)
        {
            return _items.Find(x => x.Id == id);
        }
    }

    public bool Delete(Guid id)
    {
        TaskCompletionSource<long> signal;
        long version;

        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == id);

            if (index < 0)
            {
                return false;
            }

            _items.RemoveAt(index);
            version = ++_version;
            signal = _SwapSignal();
        }

        signal.TrySetResult(version);

        return true;
    }

    public int Clear()
    {
        TaskCompletionSource<long> signal;
        long version;
        int removed;

        lock (_lock)
        {
            removed = _items.Count;

            if (removed == 0)
            {
                return 0;
            }

            _items.Clear();
            version = ++_version;
            signal = _SwapSignal();
        }

        signal.TrySetResult(version);

        return removed;
    }

    public async ValueTask<long> WaitForChangeAsync(long knownVersion, CancellationToken cancellationToken = default)
    {
        Task<long> changed;

        lock (_lock)
        {
            if (_version != knownVersion)
            {
                return _version;
            }

            changed = _changed.Task;
        }

        return await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private TaskCompletionSource<long> _SwapSignal()
    {
        var previous = _changed;
        _changed = _NewSignal();

        return previous;
    }

    private static TaskCompletionSource<long> _NewSignal()
    {
        // Waiters resume off the thread that completes the signal, so a capture never runs a dashboard's continuation.
        return new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
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
