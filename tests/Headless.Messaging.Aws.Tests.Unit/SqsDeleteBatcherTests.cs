// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Deletes of one queue share <c>DeleteMessageBatch</c> calls when they overlap, and each caller still learns its own
/// outcome.
/// </summary>
public sealed class SqsDeleteBatcherTests : TestBase
{
    private const string _QueueUrl = "http://test/queue";

    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly List<string> _invalidReceipts = [];

    [Fact]
    public async Task should_delete_a_lone_message_at_once_without_a_batch()
    {
        // given
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-1", Arg.Any<CancellationToken>())
            .Returns(new DeleteMessageResponse());
        var batcher = _CreateBatcher();

        // when
        await batcher.DeleteAsync("receipt-1", AbortToken);

        // then
        await _sqs.Received(1).DeleteMessageAsync(_QueueUrl, "receipt-1", CancellationToken.None);
        await _sqs.DidNotReceiveWithAnyArgs().DeleteMessageBatchAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_send_the_deletes_that_arrive_during_a_call_together_in_the_next_one()
    {
        // given: the first delete is in flight
        var firstCall = new TaskCompletionSource<DeleteMessageResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-0", Arg.Any<CancellationToken>()).Returns(firstCall.Task);
        List<DeleteMessageBatchRequestEntry>? batch = null;
        _sqs.DeleteMessageBatchAsync(
                _QueueUrl,
                Arg.Any<List<DeleteMessageBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                batch = call.Arg<List<DeleteMessageBatchRequestEntry>>();
                return new DeleteMessageBatchResponse
                {
                    Successful = [.. batch.Select(entry => new DeleteMessageBatchResultEntry { Id = entry.Id })],
                };
            });
        var batcher = _CreateBatcher();
        var first = batcher.DeleteAsync("receipt-0", AbortToken);

        // when: three more arrive before it returns
        var waiting = Enumerable.Range(1, 3).Select(i => batcher.DeleteAsync($"receipt-{i}", AbortToken)).ToArray();
        firstCall.SetResult(new DeleteMessageResponse());
        await first;
        await Task.WhenAll(waiting);

        // then: they leave together in one batch
        batch.Should().NotBeNull();
        batch!.Select(entry => entry.ReceiptHandle).Should().Equal("receipt-1", "receipt-2", "receipt-3");
        await _sqs.ReceivedWithAnyArgs(1).DeleteMessageBatchAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_report_each_entry_outcome_to_its_own_caller()
    {
        // given: one batch with a success, a stale receipt, a refusal, and an entry SQS does not mention
        var firstCall = new TaskCompletionSource<DeleteMessageResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-0", Arg.Any<CancellationToken>()).Returns(firstCall.Task);
        _sqs.DeleteMessageBatchAsync(
                _QueueUrl,
                Arg.Any<List<DeleteMessageBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new DeleteMessageBatchResponse
                {
                    Successful = [new DeleteMessageBatchResultEntry { Id = "0" }],
                    Failed =
                    [
                        new BatchResultErrorEntry
                        {
                            Id = "1",
                            Code = "ReceiptHandleIsInvalid",
                            Message = "stale",
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
        var first = batcher.DeleteAsync("receipt-0", AbortToken);
        var deleted = batcher.DeleteAsync("receipt-deleted", AbortToken);
        var stale = batcher.DeleteAsync("receipt-stale", AbortToken);
        var refused = batcher.DeleteAsync("receipt-refused", AbortToken);
        var unreported = batcher.DeleteAsync("receipt-unreported", AbortToken);

        // when
        firstCall.SetResult(new DeleteMessageResponse());
        await first;

        // then
        await deleted;
        await stale;
        _invalidReceipts.Should().Equal("stale");
        (await refused.Awaiting(t => t).Should().ThrowAsync<AmazonSQSException>())
            .Which.ErrorCode.Should()
            .Be("InternalError");
        await unreported.Awaiting(t => t).Should().ThrowAsync<AmazonSQSException>();
    }

    [Fact]
    public async Task should_fail_every_caller_of_a_batch_the_call_failed_for_and_keep_serving_later_deletes()
    {
        // given
        var firstCall = new TaskCompletionSource<DeleteMessageResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-0", Arg.Any<CancellationToken>()).Returns(firstCall.Task);
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-later", Arg.Any<CancellationToken>())
            .Returns(new DeleteMessageResponse());
        var outage = new AmazonSQSException("throttled");
        _sqs.DeleteMessageBatchAsync(
                _QueueUrl,
                Arg.Any<List<DeleteMessageBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException<DeleteMessageBatchResponse>(outage));
        var batcher = _CreateBatcher();
        var first = batcher.DeleteAsync("receipt-0", AbortToken);
        var second = batcher.DeleteAsync("receipt-1", AbortToken);
        var third = batcher.DeleteAsync("receipt-2", AbortToken);

        // when
        firstCall.SetResult(new DeleteMessageResponse());
        await first;

        // then
        (await second.Awaiting(t => t).Should().ThrowAsync<AmazonSQSException>())
            .Which.Should()
            .BeSameAs(outage);
        (await third.Awaiting(t => t).Should().ThrowAsync<AmazonSQSException>()).Which.Should().BeSameAs(outage);
        await batcher.DeleteAsync("receipt-later", AbortToken);
    }

    [Fact]
    public async Task should_treat_a_stale_receipt_on_a_lone_delete_as_settled()
    {
        // given
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DeleteMessageResponse>(new ReceiptHandleIsInvalidException("stale")));
        var batcher = _CreateBatcher();

        // when
        await batcher.DeleteAsync("receipt-1", AbortToken);

        // then
        _invalidReceipts.Should().Equal("stale");
    }

    [Fact]
    public async Task should_keep_serving_deletes_when_the_stale_receipt_callback_throws()
    {
        // given
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-stale", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DeleteMessageResponse>(new ReceiptHandleIsInvalidException("stale")));
        _sqs.DeleteMessageAsync(_QueueUrl, "receipt-later", Arg.Any<CancellationToken>())
            .Returns(new DeleteMessageResponse());
        var failure = new InvalidOperationException("log sink failed");
        var batcher = new SqsDeleteBatcher(_sqs, _QueueUrl, _ => throw failure);

        // when
        var stale = batcher.DeleteAsync("receipt-stale", AbortToken);

        // then: the caller sees the fault instead of waiting forever, and the queue keeps deleting
        (await stale.Awaiting(t => t).Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(failure);
        await batcher.DeleteAsync("receipt-later", AbortToken).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    private SqsDeleteBatcher _CreateBatcher() => new(_sqs, _QueueUrl, _invalidReceipts.Add);
}
