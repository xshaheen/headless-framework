// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Streaming;

/// <summary>The node's event ring: ordered reads, overwriting, gaps, and waking waiting streams.</summary>
public sealed class EventRingBufferTests : TestBase
{
    [Fact]
    public void should_return_the_events_after_a_position_in_order()
    {
        var buffer = new EventRingBuffer(capacity: 8);

        var first = _AppendChange(buffer, "r-1");
        var second = _AppendChange(buffer, "r-2");
        var third = _AppendChange(buffer, "r-3");

        var read = buffer.ReadAfter(first, maxCount: 10);

        read.Missed.Should().BeFalse();
        read.Head.Should().Be(third);
        read.Events.Select(e => e.Sequence).Should().Equal(second, third);
        read.Events.Select(e => e.Change!.Target.Id).Should().Equal("r-2", "r-3");
    }

    [Fact]
    public void should_return_at_most_the_requested_number_of_events()
    {
        var buffer = new EventRingBuffer(capacity: 8);
        _AppendChange(buffer, "r-1");
        _AppendChange(buffer, "r-2");
        _AppendChange(buffer, "r-3");

        var read = buffer.ReadAfter(0, maxCount: 2);

        read.Events.Select(e => e.Change!.Target.Id).Should().Equal("r-1", "r-2");
    }

    [Fact]
    public void should_report_missed_events_when_the_ring_overwrote_the_events_after_a_position()
    {
        var buffer = new EventRingBuffer(capacity: 4);
        var early = _AppendChange(buffer, "r-1");

        for (var i = 2; i <= 6; i++)
        {
            _AppendChange(buffer, $"r-{i}");
        }

        buffer.ReadAfter(early, maxCount: 10).Missed.Should().BeTrue();
        buffer.CanResumeAfter(early).Should().BeFalse();

        // The ring holds 3 to 6, so a stream that had seen 2 can still resume.
        buffer.CanResumeAfter(2).Should().BeTrue();
        buffer.ReadAfter(2, maxCount: 10).Events.Select(e => e.Sequence).Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void should_not_resume_a_position_ahead_of_the_ring()
    {
        var buffer = new EventRingBuffer(capacity: 4);
        _AppendChange(buffer, "r-1");

        buffer.CanResumeAfter(5).Should().BeFalse();
        buffer.ReadAfter(5, maxCount: 10).Missed.Should().BeTrue();
    }

    [Fact]
    public void should_not_resume_a_position_from_before_a_gap_but_resume_one_after_it()
    {
        var buffer = new EventRingBuffer(capacity: 16);
        var beforeStop = _AppendChange(buffer, "r-1");

        buffer.MarkGap();
        var afterRestart = buffer.Head;

        buffer.CanResumeAfter(beforeStop).Should().BeFalse();
        buffer.CanResumeAfter(afterRestart).Should().BeTrue();
    }

    [Fact]
    public async Task should_wake_a_waiting_stream_when_an_event_is_appended()
    {
        var buffer = new EventRingBuffer(capacity: 4);
        var position = _AppendChange(buffer, "r-1");

        var waiting = buffer.WaitForAppendAsync(position);
        waiting.IsCompleted.Should().BeFalse();

        _AppendChange(buffer, "r-2");

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        buffer.WaitForAppendAsync(position).IsCompleted.Should().BeTrue();
    }

    private static long _AppendChange(EventRingBuffer buffer, string id)
    {
        return buffer.Append(StreamEventType.Change, "diag", FakeChangeStore.Change("diag", "run", id, "1"));
    }
}
