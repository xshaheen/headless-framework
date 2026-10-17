// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Tests.Ledger;

/// <summary>
/// Records every publish, broker send, and handler run of one chaos scenario, across every host it starts. The scenario
/// owns one ledger and registers it in each host, so the counts survive restarts and span the whole cluster; the
/// verdict then compares what was accepted with what ran.
/// </summary>
/// <remarks>
/// Entries are keyed by the business key a message carries, not by its transport message ID, so a copy re-sent under a
/// fresh ID still lands on its original entry and shows up as a duplicate run.
/// </remarks>
public sealed class MessageLedger(TimeProvider clock)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<DateTimeOffset> _completions = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LedgerProgress _progress;

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Gets the running totals, which cost nothing to read while a scenario waits on them.</summary>
    public LedgerProgress Progress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    /// <summary>Records a publish the stack accepted, with the time it is due when it was published delayed.</summary>
    public void RecordPublished(string key, string host, DateTimeOffset? dueAt = null) =>
        _Update(
            key,
            entry =>
            {
                entry.Accepted = true;
                entry.PublishedBy ??= host;
                entry.DueAt ??= dueAt;
            }
        );

    /// <summary>Records a publish the stack refused, such as one made while storage was down.</summary>
    public void RecordPublishRejected(string key) => _Update(key, entry => entry.Rejections++);

    /// <summary>Records one transport send of the message, whether it reached the broker or not.</summary>
    public void RecordSend(string key, string host, bool succeeded)
    {
        var at = clock.GetUtcNow();
        _Update(key, entry => entry.Sends.Add(new LedgerSend(host, at, succeeded)));
    }

    /// <summary>Records a handler run starting and returns its attempt number across every host, from 1.</summary>
    public int RecordStarted(string key, string host)
    {
        var attempt = 0;
        _Update(
            key,
            entry =>
            {
                attempt = ++entry.Started;
                entry.StartedOn.Add(host);
            }
        );

        return attempt;
    }

    /// <summary>Records a handler run completing its side effect.</summary>
    public void RecordCompleted(string key, string host)
    {
        var at = clock.GetUtcNow();
        _Update(
            key,
            entry =>
            {
                entry.Completed++;
                entry.CompletedOn.Add(host);
                entry.FirstCompletedAt ??= at;
                _completions.Add(at);
            }
        );
    }

    /// <summary>Records a reply to the message arriving at its reply consumer.</summary>
    public void RecordReply(string key) => _Update(key, entry => entry.Replies++);

    /// <summary>Records the framework giving the message up as terminally failed.</summary>
    public void RecordExhausted(string key) => _Update(key, entry => entry.Exhausted++);

    /// <summary>Returns a consistent copy of everything recorded so far.</summary>
    public LedgerSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new LedgerSnapshot(
                _entries.ToDictionary(pair => pair.Key, pair => pair.Value.ToRecord(pair.Key), StringComparer.Ordinal),
                [.. _completions]
            );
        }
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds over the running totals, re-checking on every recorded event.
    /// Returns <see langword="false"/> when <paramref name="timeout"/> passes first, so a caller can also assert that
    /// nothing more happened within a window.
    /// </summary>
    public async Task<bool> WaitUntilAsync(
        Func<LedgerProgress, bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                Task changed;
                LedgerProgress progress;

                lock (_lock)
                {
                    changed = _changed.Task;
                    progress = _progress;
                }

                if (condition(progress))
                {
                    return true;
                }

                await changed.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void _Update(string key, Action<Entry> update)
    {
        TaskCompletionSource changed;

        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            var before = entry.Totals();
            update(entry);
            _progress = LedgerProgress.Add(_progress, LedgerProgress.Subtract(entry.Totals(), before));
            changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    private sealed class Entry
    {
        public bool Accepted { get; set; }

        public string? PublishedBy { get; set; }

        public DateTimeOffset? DueAt { get; set; }

        public int Rejections { get; set; }

        public List<LedgerSend> Sends { get; } = [];

        public int Started { get; set; }

        public List<string> StartedOn { get; } = [];

        public int Completed { get; set; }

        public List<string> CompletedOn { get; } = [];

        public DateTimeOffset? FirstCompletedAt { get; set; }

        public int Replies { get; set; }

        public int Exhausted { get; set; }

        public LedgerProgress Totals() =>
            new(
                Accepted: Accepted ? 1 : 0,
                AcceptedCompleted: Accepted && Completed > 0 ? 1 : 0,
                Rejected: Rejections,
                Starts: Started,
                Completions: Completed,
                Replies: Replies,
                Exhausted: Exhausted
            );

        public LedgerEntry ToRecord(string key) =>
            new(
                key,
                Accepted,
                PublishedBy,
                DueAt,
                Rejections,
                [.. Sends],
                Started,
                [.. StartedOn],
                Completed,
                [.. CompletedOn],
                FirstCompletedAt,
                Replies,
                Exhausted
            );
    }
}

/// <summary>Running totals across every entry of a ledger.</summary>
/// <param name="Accepted">Messages the stack accepted.</param>
/// <param name="AcceptedCompleted">Accepted messages whose handler completed at least once.</param>
/// <param name="Rejected">Publishes the stack refused.</param>
/// <param name="Starts">Handler runs started.</param>
/// <param name="Completions">Handler runs completed.</param>
/// <param name="Replies">Replies received.</param>
/// <param name="Exhausted">Messages the framework gave up as terminally failed.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LedgerProgress(
    int Accepted,
    int AcceptedCompleted,
    int Rejected,
    int Starts,
    int Completions,
    int Replies,
    int Exhausted
)
{
    /// <summary>Whether every accepted message has completed at least once.</summary>
    public bool AllAcceptedCompleted => AcceptedCompleted == Accepted;

    public static LedgerProgress Add(LedgerProgress left, LedgerProgress right) =>
        new(
            left.Accepted + right.Accepted,
            left.AcceptedCompleted + right.AcceptedCompleted,
            left.Rejected + right.Rejected,
            left.Starts + right.Starts,
            left.Completions + right.Completions,
            left.Replies + right.Replies,
            left.Exhausted + right.Exhausted
        );

    public static LedgerProgress Subtract(LedgerProgress left, LedgerProgress right) =>
        new(
            left.Accepted - right.Accepted,
            left.AcceptedCompleted - right.AcceptedCompleted,
            left.Rejected - right.Rejected,
            left.Starts - right.Starts,
            left.Completions - right.Completions,
            left.Replies - right.Replies,
            left.Exhausted - right.Exhausted
        );
}

/// <summary>One transport send: the host that made it, when it returned, and whether the broker took the message.</summary>
public sealed record LedgerSend(string Host, DateTimeOffset At, bool Succeeded);

/// <summary>Everything the ledger recorded for one business key.</summary>
public sealed record LedgerEntry(
    string Key,
    bool Accepted,
    string? PublishedBy,
    DateTimeOffset? DueAt,
    int Rejections,
    IReadOnlyList<LedgerSend> Sends,
    int Started,
    IReadOnlyList<string> StartedOn,
    int Completed,
    IReadOnlyList<string> CompletedOn,
    DateTimeOffset? FirstCompletedAt,
    int Replies,
    int Exhausted
)
{
    public int SuccessfulSends => Sends.Count(send => send.Succeeded);

    public DateTimeOffset? FirstSuccessfulSendAt =>
        Sends.Where(send => send.Succeeded).Min(send => (DateTimeOffset?)send.At);
}

/// <summary>A consistent copy of a ledger: every entry, and every completion time in the order it was recorded.</summary>
public sealed record LedgerSnapshot(
    IReadOnlyDictionary<string, LedgerEntry> Entries,
    IReadOnlyList<DateTimeOffset> Completions
)
{
    /// <summary>The entries of messages the stack accepted.</summary>
    public IEnumerable<LedgerEntry> Accepted => Entries.Values.Where(entry => entry.Accepted);

    public LedgerEntry Get(string key) =>
        Entries.TryGetValue(key, out var entry)
            ? entry
            : new LedgerEntry(key, false, null, null, 0, [], 0, [], 0, [], null, 0, 0);
}
