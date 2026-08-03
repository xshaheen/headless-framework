// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Unit tests for the concurrent-mode commit watermark. The tracker is what keeps a group from
/// committing past a delivery that is still being handled, and what keeps it from freezing on an
/// offset the broker never hands to the application.
/// </summary>
public sealed class KafkaOffsetCommitTrackerTests : TestBase
{
    private const string _Topic = "orders.created";
    private static readonly TopicPartition _Partition = new(_Topic, new Partition(0));

    [Fact]
    public void should_commit_next_offset_when_only_delivery_completes()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var delivery = tracker.Track(_Record(10));

        // when
        var committable = tracker.MarkCommitted(delivery);

        // then
        _OffsetsOf(committable).Should().Equal([11]);
    }

    [Fact]
    public void should_hold_watermark_below_in_flight_delivery_when_higher_offset_completes_first()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var first = tracker.Track(_Record(100));
        var second = tracker.Track(_Record(101));

        // when
        var afterSecond = tracker.MarkCommitted(second);
        var afterFirst = tracker.MarkCommitted(first);

        // then
        afterSecond.Should().BeEmpty("offset 100 is still in flight");
        _OffsetsOf(afterFirst).Should().Equal([102]);
    }

    [Fact]
    public void should_advance_watermark_past_offsets_the_broker_never_delivered()
    {
        // given — 11..14 are transaction control records or compaction holes: the poll loop jumps
        // straight from 10 to 15, so they can never turn up later.
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var first = tracker.Track(_Record(10));
        var second = tracker.Track(_Record(15));

        // when
        var afterFirst = tracker.MarkCommitted(first);
        var afterSecond = tracker.MarkCommitted(second);

        // then
        _OffsetsOf(afterFirst).Should().Equal([15], "the undelivered range cannot be in flight");
        _OffsetsOf(afterSecond).Should().Equal([16]);
    }

    [Fact]
    public void should_advance_watermark_past_gap_when_completions_arrive_out_of_order()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var first = tracker.Track(_Record(100));
        var second = tracker.Track(_Record(103));

        // when — the higher offset finishes first, across the 101..102 hole
        var afterSecond = tracker.MarkCommitted(second);
        var afterFirst = tracker.MarkCommitted(first);

        // then
        afterSecond.Should().BeEmpty("offset 100 is still in flight");
        _OffsetsOf(afterFirst).Should().Equal([104]);
    }

    [Fact]
    public void should_advance_watermark_past_tombstone_offsets()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var first = tracker.Track(_Record(10));
        tracker.MarkCommitted(first);

        // when — 11 and 12 are tombstones the poll loop skips without dispatching
        tracker.MarkObserved(_Tombstone(11));
        tracker.MarkObserved(_Tombstone(12));
        var afterThird = tracker.MarkCommitted(tracker.Track(_Record(13)));

        // then
        _OffsetsOf(afterThird).Should().Equal([14]);
    }

    [Fact]
    public void should_commit_trailing_tombstone_when_no_delivery_remains()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        tracker.MarkCommitted(tracker.Track(_Record(10)));

        // when
        var committable = tracker.MarkObserved(_Tombstone(11));

        // then
        _OffsetsOf(committable).Should().Equal([12]);
    }

    [Fact]
    public void should_not_commit_trailing_tombstone_past_lower_in_flight_delivery()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var inFlight = tracker.Track(_Record(10));

        // when
        var whileInFlight = tracker.MarkObserved(_Tombstone(11));
        var afterCompletion = tracker.MarkCommitted(inFlight);

        // then
        whileInFlight.Should().BeEmpty();
        _OffsetsOf(afterCompletion).Should().Equal([12]);
    }

    [Fact]
    public void should_advance_watermark_to_log_end_offset_when_partition_eof_is_observed()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var delivery = tracker.Track(_Record(10));

        // when — 11..14 are trailing control records, so the log end offset is 15
        tracker.MarkObserved(_EndOfPartition(15));
        var committable = tracker.MarkCommitted(delivery);

        // then — the EOF result already carries the next offset to read, so it is not incremented
        _OffsetsOf(committable).Should().Equal([15]);
    }

    [Fact]
    public void should_commit_log_end_offset_when_eof_is_the_only_observation()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();

        // when
        var committable = tracker.MarkObserved(_EndOfPartition(15));

        // then
        _OffsetsOf(committable).Should().Equal([15]);
    }

    [Fact]
    public void should_not_track_offsets_below_zero()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();

        // when
        var delivery = tracker.Track(_Record(Offset.Unset.Value));

        // then
        delivery.IsTracked.Should().BeFalse();
        tracker.MarkCommitted(delivery).Should().BeEmpty();
    }

    [Fact]
    public void should_ignore_completion_from_before_reassignment_when_partition_is_reset()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var delivery = tracker.Track(_Record(10));

        // when
        tracker.Reset(_Partition);

        // then
        tracker.MarkCommitted(delivery).Should().BeEmpty();
    }

    [Fact]
    public void should_restart_watermark_from_first_delivery_after_reset()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        tracker.MarkCommitted(tracker.Track(_Record(10)));
        tracker.Reset(_Partition);

        // when — the replacement assignment resumes from the group's committed offset
        var committable = tracker.MarkCommitted(tracker.Track(_Record(5)));

        // then
        _OffsetsOf(committable).Should().Equal([6]);
    }

    [Fact]
    public void should_keep_watermark_at_rejected_offset_until_the_replay_arrives()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var rejected = tracker.Track(_Record(50));

        // when — the reject seeks back to 50, but the poll loop was already holding 55 and tracks it
        // after the seek. 51..54 still have to be replayed before the watermark may pass them.
        tracker.MarkRejected(rejected).Should().BeTrue();
        var afterStaleCompletion = tracker.MarkCommitted(tracker.Track(_Record(55)));
        var afterReplayedCompletion = tracker.MarkCommitted(tracker.Track(_Record(50)));

        // then
        afterStaleCompletion.Should().BeEmpty("the delivery was fetched before the seek");
        _OffsetsOf(afterReplayedCompletion).Should().Equal([51], "51..54 have not been replayed yet");
    }

    [Fact]
    public void should_ignore_trailing_observation_fetched_before_rejected_offset_is_replayed()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var rejected = tracker.Track(_Record(50));
        tracker.MarkRejected(rejected);

        // when
        var staleObservation = tracker.MarkObserved(_Tombstone(55));
        var replayedObservation = tracker.MarkObserved(_Tombstone(50));

        // then
        staleObservation.Should().BeEmpty();
        _OffsetsOf(replayedObservation).Should().Equal([51]);
    }

    [Fact]
    public void should_ignore_completion_from_before_reject_when_generation_changed()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var delivery = tracker.Track(_Record(10));

        // when
        tracker.MarkRejected(delivery);

        // then
        tracker.MarkCommitted(delivery).Should().BeEmpty();
        tracker.MarkRejected(delivery).Should().BeFalse();
    }

    [Fact]
    public void should_track_partitions_independently()
    {
        // given
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var onPartitionZero = tracker.Track(_Record(10));
        var onPartitionOne = tracker.Track(_Record(200, partition: 1));

        // when
        var zeroCommittable = tracker.MarkCommitted(onPartitionZero);
        var oneCommittable = tracker.MarkCommitted(onPartitionOne);

        // then
        zeroCommittable.Should().ContainSingle().Which.Partition.Value.Should().Be(0);
        _OffsetsOf(zeroCommittable).Should().Equal([11]);
        oneCommittable.Should().ContainSingle().Which.Partition.Value.Should().Be(1);
        _OffsetsOf(oneCommittable).Should().Equal([201]);
    }

    [Fact]
    public void should_not_grow_pending_state_when_offsets_complete_across_a_gap()
    {
        // given — the pre-fix tracker kept every completed offset above a hole forever
        var tracker = new KafkaConsumerClient.KafkaOffsetCommitTracker();
        var blocked = tracker.Track(_Record(0));

        // when — a long run of holes, each followed by a completed delivery
        for (var offset = 10; offset < 1000; offset += 10)
        {
            tracker.MarkCommitted(tracker.Track(_Record(offset)));
        }

        var committable = tracker.MarkCommitted(blocked);

        // then — releasing the blocking delivery commits straight to the observed frontier
        _OffsetsOf(committable).Should().Equal([991]);
    }

    private static List<long> _OffsetsOf(List<TopicPartitionOffset> committable)
    {
        return committable.ConvertAll(x => x.Offset.Value);
    }

    private static ConsumeResult<string, byte[]> _Record(long offset, int partition = 0)
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset(_Topic, new Partition(partition), new Offset(offset)),
            Message = new Message<string, byte[]> { Value = BitConverter.GetBytes(offset), Headers = [] },
        };
    }

    private static ConsumeResult<string, byte[]> _Tombstone(long offset)
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset(_Topic, new Partition(0), new Offset(offset)),
            Message = new Message<string, byte[]> { Value = null!, Headers = [] },
        };
    }

    private static ConsumeResult<string, byte[]> _EndOfPartition(long logEndOffset)
    {
        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset(_Topic, new Partition(0), new Offset(logEndOffset)),
            IsPartitionEOF = true,
        };
    }
}
