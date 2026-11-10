// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Dashboard;

/// <summary>
/// The node's last stream events, in a bounded ring, so a stream that reconnects to this node resumes from its last
/// event id instead of refetching every view.
/// </summary>
/// <remarks>
/// <para>
/// Every event gets the next sequence, starting at 1. A stream reads the events after its position and waits for the
/// next append. A position the ring no longer holds, from before a reader restart, or ahead of the ring, cannot be
/// resumed, and the stream sends <c>reset</c> instead.
/// </para>
/// <para>
/// A reader restart is recorded as a gap: the change reads stopped while no stream was open, so the store changes made
/// meanwhile never reached the ring, and no position from before the restart can be resumed.
/// </para>
/// </remarks>
internal sealed class EventRingBuffer
{
    /// <summary>The number of events the ring holds by default.</summary>
    internal const int DefaultCapacity = 4096;

    private readonly Lock _lock = new();
    private readonly StreamEvent?[] _events;
    private long _head;
    private long _resumeFloor;
    private TaskCompletionSource _appended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates a ring of <see cref="DefaultCapacity"/> events.</summary>
    public EventRingBuffer()
        : this(DefaultCapacity) { }

    /// <summary>Creates a ring of <paramref name="capacity"/> events.</summary>
    internal EventRingBuffer(int capacity)
    {
        Argument.IsPositive(capacity);
        _events = new StreamEvent?[capacity];
    }

    /// <summary>Gets the sequence of the last appended event; 0 before the first.</summary>
    public long Head
    {
        get
        {
            lock (_lock)
            {
                return _head;
            }
        }
    }

    /// <summary>Appends one event and wakes every stream waiting for one.</summary>
    /// <returns>The event's sequence.</returns>
    public long Append(
        StreamEventType type,
        string moduleId,
        DashboardChange? change = null,
        bool stale = false,
        string? node = null,
        IReadOnlyList<string>? unavailable = null
    )
    {
        TaskCompletionSource appended;
        long sequence;

        lock (_lock)
        {
            sequence = ++_head;
            _events[_Slot(sequence)] = new StreamEvent
            {
                Sequence = sequence,
                Type = type,
                ModuleId = moduleId,
                Change = change,
                Stale = stale,
                Node = node,
                Unavailable = unavailable,
            };

            appended = _appended;
            _appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        appended.TrySetResult();

        return sequence;
    }

    /// <summary>
    /// Records that the change reads restart after a stop, so no stream resumes from a position before this point.
    /// </summary>
    /// <remarks>
    /// The gap is an event of its own, never sent, so a stream that opens after the restart holds a position at or
    /// after it and can still resume.
    /// </remarks>
    public void MarkGap()
    {
        var sequence = Append(StreamEventType.Gap, moduleId: string.Empty);

        lock (_lock)
        {
            _resumeFloor = sequence;
        }
    }

    /// <summary>
    /// Whether a stream whose last event was <paramref name="sequence"/> can resume: the ring still holds every event
    /// after it, it comes after the last gap, and it is not ahead of the ring.
    /// </summary>
    public bool CanResumeAfter(long sequence)
    {
        lock (_lock)
        {
            return sequence >= _resumeFloor && sequence <= _head && sequence >= _OldestRetained() - 1;
        }
    }

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> events after <paramref name="position"/>, in order, or reports that
    /// the ring has overwritten some of them.
    /// </summary>
    public StreamRead ReadAfter(long position, int maxCount)
    {
        Argument.IsPositive(maxCount);

        lock (_lock)
        {
            if (position < _OldestRetained() - 1 || position > _head)
            {
                return new StreamRead(Missed: true, [], _head);
            }

            var count = (int)Math.Min(maxCount, _head - position);
            var events = new StreamEvent[count];

            for (var i = 0; i < count; i++)
            {
                events[i] = _events[_Slot(position + 1 + i)]!;
            }

            return new StreamRead(Missed: false, events, _head);
        }
    }

    /// <summary>Returns a task that completes when an event after <paramref name="position"/> is appended.</summary>
    public Task WaitForAppendAsync(long position)
    {
        lock (_lock)
        {
            return _head > position ? Task.CompletedTask : _appended.Task;
        }
    }

    private long _OldestRetained() => Math.Max(1, _head - _events.Length + 1);

    private int _Slot(long sequence) => (int)((sequence - 1) % _events.Length);
}

/// <summary>What one read of the ring returned.</summary>
/// <param name="Missed">Whether the ring no longer holds every event after the position read from.</param>
/// <param name="Events">The events after the position, in order; empty when <paramref name="Missed"/>.</param>
/// <param name="Head">The sequence of the ring's last event when it was read.</param>
internal sealed record StreamRead(bool Missed, IReadOnlyList<StreamEvent> Events, long Head);

/// <summary>One event in the ring.</summary>
internal sealed record StreamEvent
{
    /// <summary>Gets the event's sequence on this node.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets what the event reports.</summary>
    public required StreamEventType Type { get; init; }

    /// <summary>Gets the module the event belongs to; empty for a gap.</summary>
    public required string ModuleId { get; init; }

    /// <summary>Gets the changed object for a <see cref="StreamEventType.Change"/> event.</summary>
    public DashboardChange? Change { get; init; }

    /// <summary>
    /// Gets, for a <see cref="StreamEventType.ModuleStale"/> or <see cref="StreamEventType.NodeStale"/> event, whether
    /// the module's or the node's reads are failing.
    /// </summary>
    public bool Stale { get; init; }

    /// <summary>Gets the node a <see cref="StreamEventType.NodeStale"/> or <see cref="StreamEventType.NodeReset"/> event names.</summary>
    public string? Node { get; init; }

    /// <summary>
    /// Gets, for a <see cref="StreamEventType.ModuleStale"/> event, the sources the module's reads could not reach, or
    /// <see langword="null"/> when every source answered.
    /// </summary>
    public IReadOnlyList<string>? Unavailable { get; init; }
}

/// <summary>What a ring event reports.</summary>
internal enum StreamEventType
{
    /// <summary>A module's object changed.</summary>
    Change,

    /// <summary>A module's Needs you items may have changed.</summary>
    AttentionChanged,

    /// <summary>A module's change reads started or stopped failing, or reach fewer or more of its sources.</summary>
    ModuleStale,

    /// <summary>A module reported that one node's data started or stopped being out of date.</summary>
    NodeStale,

    /// <summary>A module reported that one node's views must refetch.</summary>
    NodeReset,

    /// <summary>The change reads restarted; never sent.</summary>
    Gap,
}
