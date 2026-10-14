// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Rejects of one queue share <c>ChangeMessageVisibilityBatch</c> calls when they overlap, every call waits for the
/// visibility gate the heartbeat holds, and each caller still learns its own outcome.
/// </summary>
public sealed class SqsRejectBatcherTests : TestBase
{
    private const string _QueueUrl = "http://test/queue";

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _notInflight = [];

    [Fact]
    public async Task should_reject_a_lone_message_at_once_without_a_batch()
    {
        // given
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-1", 3, Arg.Any<CancellationToken>())
            .Returns(new ChangeMessageVisibilityResponse());
        var batcher = _CreateBatcher();

        // when
        await batcher.RejectAsync("receipt-1", AbortToken);

        // then
        await _sqs.Received(1).ChangeMessageVisibilityAsync(_QueueUrl, "receipt-1", 3, CancellationToken.None);
        _sqs.ReceivedCalls()
            .Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IAmazonSQS.ChangeMessageVisibilityBatchAsync));
        _gate.CurrentCount.Should().Be(1, "the gate is released after the call");
    }

    [Fact]
    public async Task should_send_the_rejects_that_arrive_during_a_call_together_in_the_next_one()
    {
        // given: the first reject is in flight
        var firstCall = new TaskCompletionSource<ChangeMessageVisibilityResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-0", 3, Arg.Any<CancellationToken>())
            .Returns(firstCall.Task);
        List<ChangeMessageVisibilityBatchRequestEntry>? batch = null;
        _sqs.ChangeMessageVisibilityBatchAsync(
                _QueueUrl,
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                batch = call.Arg<List<ChangeMessageVisibilityBatchRequestEntry>>();
                return new ChangeMessageVisibilityBatchResponse
                {
                    Successful =
                    [
                        .. batch.Select(entry => new ChangeMessageVisibilityBatchResultEntry { Id = entry.Id }),
                    ],
                };
            });
        var batcher = _CreateBatcher();
        var first = batcher.RejectAsync("receipt-0", AbortToken);

        // when: three more arrive before it returns
        var waiting = Enumerable.Range(1, 3).Select(i => batcher.RejectAsync($"receipt-{i}", AbortToken)).ToArray();
        firstCall.SetResult(new ChangeMessageVisibilityResponse());
        await first;
        await Task.WhenAll(waiting);

        // then: they leave together in one batch with the reject visibility
        batch.Should().NotBeNull();
        batch!.Select(entry => entry.ReceiptHandle).Should().Equal("receipt-1", "receipt-2", "receipt-3");
        batch.Should().OnlyContain(entry => entry.VisibilityTimeout == 3);
    }

    [Fact]
    public async Task should_not_send_a_reject_while_the_visibility_gate_is_held_and_send_the_waiting_ones_together()
    {
        // given: a heartbeat extension holds the gate
        await _gate.WaitAsync(AbortToken);
        List<ChangeMessageVisibilityBatchRequestEntry>? batch = null;
        _sqs.ChangeMessageVisibilityBatchAsync(
                _QueueUrl,
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                batch = call.Arg<List<ChangeMessageVisibilityBatchRequestEntry>>();
                return new ChangeMessageVisibilityBatchResponse
                {
                    Successful =
                    [
                        .. batch.Select(entry => new ChangeMessageVisibilityBatchResultEntry { Id = entry.Id }),
                    ],
                };
            });
        var batcher = _CreateBatcher();

        // when: rejects arrive meanwhile
        var rejects = Enumerable.Range(1, 3).Select(i => batcher.RejectAsync($"receipt-{i}", AbortToken)).ToArray();
        await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);

        // then: nothing is sent until the extension is done
        _sqs.ReceivedCalls().Should().BeEmpty();

        // when: the extension returns
        _gate.Release();
        await Task.WhenAll(rejects);

        // then: the rejects that waited share one call
        batch!.Select(entry => entry.ReceiptHandle).Should().Equal("receipt-1", "receipt-2", "receipt-3");
    }

    [Fact]
    public async Task should_report_each_entry_outcome_to_its_own_caller()
    {
        // given: one batch with a success, a message that is no longer in flight, a refusal, and an unmentioned entry
        var firstCall = new TaskCompletionSource<ChangeMessageVisibilityResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-0", 3, Arg.Any<CancellationToken>())
            .Returns(firstCall.Task);
        _sqs.ChangeMessageVisibilityBatchAsync(
                _QueueUrl,
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ChangeMessageVisibilityBatchResponse
                {
                    Successful = [new ChangeMessageVisibilityBatchResultEntry { Id = "0" }],
                    Failed =
                    [
                        new BatchResultErrorEntry
                        {
                            Id = "1",
                            Code = "MessageNotInflight",
                            Message = "gone",
                        },
                        new BatchResultErrorEntry
                        {
                            Id = "2",
                            Code = "InternalError",
                            Message = "boom",
                        },
                    ],
                }
            );
        var batcher = _CreateBatcher();
        var first = batcher.RejectAsync("receipt-0", AbortToken);
        var rejected = batcher.RejectAsync("receipt-rejected", AbortToken);
        var gone = batcher.RejectAsync("receipt-gone", AbortToken);
        var refused = batcher.RejectAsync("receipt-refused", AbortToken);
        var unreported = batcher.RejectAsync("receipt-unreported", AbortToken);

        // when
        firstCall.SetResult(new ChangeMessageVisibilityResponse());
        await first;

        // then
        await rejected;
        await gone;
        _notInflight.Should().Equal("gone");
        (await refused.Awaiting(task => task).Should().ThrowAsync<AmazonSQSException>())
            .Which.ErrorCode.Should()
            .Be("InternalError");
        await unreported.Awaiting(task => task).Should().ThrowAsync<AmazonSQSException>();
    }

    [Fact]
    public async Task should_treat_a_lone_message_that_is_no_longer_in_flight_as_rejected()
    {
        // given
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-1", 3, Arg.Any<CancellationToken>())
            .Returns<Task<ChangeMessageVisibilityResponse>>(_ => throw new MessageNotInflightException("gone"));
        var batcher = _CreateBatcher();

        // when
        await batcher.RejectAsync("receipt-1", AbortToken);

        // then
        _notInflight.Should().ContainSingle();
    }

    [Fact]
    public async Task should_fail_the_callers_of_a_failed_call_and_keep_serving_later_rejects()
    {
        // given
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-bad", 3, Arg.Any<CancellationToken>())
            .Returns<Task<ChangeMessageVisibilityResponse>>(_ => throw new AmazonSQSException("down"));
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-good", 3, Arg.Any<CancellationToken>())
            .Returns(new ChangeMessageVisibilityResponse());
        var batcher = _CreateBatcher();

        // when
        var bad = batcher.RejectAsync("receipt-bad", AbortToken);
        await bad.Awaiting(task => task).Should().ThrowAsync<AmazonSQSException>();
        await batcher.RejectAsync("receipt-good", AbortToken);

        // then
        await _sqs.Received(1).ChangeMessageVisibilityAsync(_QueueUrl, "receipt-good", 3, CancellationToken.None);
    }

    private SqsRejectBatcher _CreateBatcher()
    {
        return new SqsRejectBatcher(_sqs, _QueueUrl, _gate, visibilitySeconds: 3, _notInflight.Add);
    }
}
