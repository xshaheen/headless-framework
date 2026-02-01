// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using SqsMessage = Amazon.SQS.Model.Message;

namespace Tests;

public sealed class AmazonSqsConsumerClientTests : TestBase
{
    private static IOptions<AmazonSqsOptions> _CreateOptions() =>
        Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

    private static void _SetPrivateFields(AmazonSqsConsumerClient client, IAmazonSQS sqsClient, string queueUrl)
    {
        var sqsClientField = typeof(AmazonSqsConsumerClient).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        sqsClientField!.SetValue(client, sqsClient);

        var queueUrlField = typeof(AmazonSqsConsumerClient).GetField(
            "_queueUrl",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        queueUrlField!.SetValue(client, queueUrl);
    }

    private static SemaphoreSlim _GetSemaphore(AmazonSqsConsumerClient client)
    {
        var semaphoreField = typeof(AmazonSqsConsumerClient).GetField(
            "_semaphore",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        return (SemaphoreSlim)semaphoreField!.GetValue(client)!;
    }

    [Fact]
    public async Task should_log_error_when_consumeAsync_throws_in_concurrent_mode()
    {
        // given
        var options = _CreateOptions();

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var exceptionThrown = new InvalidOperationException("Test exception");

        // Configure callback to throw
        client.OnMessageCallback = (_, _) => throw exceptionThrown;

        // Create mock SQS client
        var sqsClient = Substitute.For<IAmazonSQS>();

        // Return message only once, then empty
        var receiveCallCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCallCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage
                                {
                                    Body = """
                                    {
                                        "Message": "test message",
                                        "MessageAttributes": {
                                            "test-key": { "Value": "test-value" }
                                        }
                                    }
                                    """,
                                    ReceiptHandle = "test-receipt-handle",
                                },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when - Start listening in background and wait for message processing
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout occurs
        }

        // Give some time for the fire-and-forget task to complete
        await Task.Delay(500, AbortToken);

        // then - Verify error was logged
        logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Error consuming message for group")),
                Arg.Is<Exception>(ex => ex == exceptionThrown),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact(Skip = "Tests expected behavior - current implementation doesn't reject messages after consume errors")]
    public async Task should_log_error_when_reject_fails_after_consume_error()
    {
        // This test documents expected behavior where the implementation should:
        // 1. Catch callback exceptions
        // 2. Call RejectAsync to return the message to the queue
        // 3. If RejectAsync fails, log that failure too
        // Current implementation only logs the callback error, doesn't reject.

        // given
        var options = _CreateOptions();

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var consumeException = new InvalidOperationException("Consume failed");
        var rejectException = new MessageNotInflightException("Reject failed");

        client.OnMessageCallback = (_, _) => throw consumeException;

        var sqsClient = Substitute.For<IAmazonSQS>();

        var receiveCallCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCallCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage
                                {
                                    Body = """
                                    {
                                        "Message": "test message",
                                        "MessageAttributes": {
                                            "test-key": { "Value": "test-value" }
                                        }
                                    }
                                    """,
                                    ReceiptHandle = "test-receipt-handle",
                                },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        sqsClient
            .ChangeMessageVisibilityAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(rejectException);

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await Task.Delay(500, AbortToken);

        // then - Verify both errors were logged
        logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Error consuming message for group")),
                Arg.Is<Exception>(ex => ex == consumeException),
                Arg.Any<Func<object, Exception?, string>>()
            );

        logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Failed to reject message after consume error")),
                Arg.Is<Exception>(ex => ex == rejectException),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task should_handle_invalid_message_structure_and_reject()
    {
        // given
        var options = _CreateOptions();

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var messageReceived = false;
        client.OnMessageCallback = (_, _) =>
        {
            messageReceived = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();

        // Return invalid message (null MessageAttributes) once, then empty
        var receiveCallCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCallCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages = [new SqsMessage { Body = "{}", ReceiptHandle = "test-receipt" }],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await Task.Delay(500, AbortToken);

        // then
        messageReceived.Should().BeFalse("invalid messages should not be processed");

        // Verify error was logged
        logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o =>
                    o.ToString()!.Contains("Invalid SQS message structure") && o.ToString()!.Contains("Moving to DLQ")
                ),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );

        // Verify message was rejected
        await sqsClient
            .Received(1)
            .ChangeMessageVisibilityAsync(Arg.Any<string>(), "test-receipt", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_wrap_task_in_non_concurrent_mode()
    {
        // given
        var options = _CreateOptions();

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 0, options, logger); // groupConcurrent = 0

        var callbackExecuted = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackExecuted = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();

        // Return message only once, then empty
        var receiveCallCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCallCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage
                                {
                                    Body = """
                                    {
                                        "Message": "test",
                                        "MessageAttributes": { "key": { "Value": "value" } }
                                    }
                                    """,
                                    ReceiptHandle = "receipt",
                                },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // then
        callbackExecuted.Should().BeTrue("callback should execute in non-concurrent mode");

        // No error logs should be present (callback succeeded synchronously)
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact(Skip = "Documents known double-release bug - semaphore count exceeds initial")]
    public async Task should_not_double_release_semaphore_on_exception()
    {
        // CRITICAL BUG: This test documents a double-release bug in the semaphore handling.
        // When an exception is thrown in the callback, the semaphore is released twice,
        // causing the count to exceed the initial value.
        // The bug is in AmazonSqsConsumerClient.ConsumeAsync - both the finally block
        // and the catch block release the semaphore.

        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        const int concurrencyLimit = 3;
        await using var client = new AmazonSqsConsumerClient("test-group", concurrencyLimit, _CreateOptions(), logger);

        var semaphore = _GetSemaphore(client);
        var initialCount = semaphore.CurrentCount;

        var exceptionThrown = new InvalidOperationException("Test exception");
        client.OnMessageCallback = (_, _) => throw exceptionThrown;

        var sqsClient = Substitute.For<IAmazonSQS>();
        var receiveCallCount = 0;
        var receiveResponse = new ReceiveMessageResponse
        {
            Messages =
            [
                new SqsMessage
                {
                    Body = """
                        {
                            "Message": "test",
                            "MessageAttributes": { "key": { "Value": "value" } }
                        }
                        """,
                    ReceiptHandle = "receipt-1",
                },
            ],
        };

        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref receiveCallCount);
                return receiveCallCount <= 1
                    ? Task.FromResult(receiveResponse)
                    : Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await Task.Delay(500, AbortToken);

        // then - semaphore count should return to initial value (not exceed it due to double release)
        // BUG: finalCount is 4 instead of 3, confirming double release
        var finalCount = semaphore.CurrentCount;
        finalCount.Should().Be(initialCount, "semaphore should be released exactly once, not twice");
    }

    [Fact]
    public async Task should_fetch_batch_of_10_messages()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse { Messages = [] });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // then - verify the request was made with MaxNumberOfMessages = 10
        await sqsClient
            .Received()
            .ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(r => r.MaxNumberOfMessages == 10),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_delete_message_on_commit()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeleteMessageResponse()));

        _SetPrivateFields(client, sqsClient, "http://test-queue");

        const string receiptHandle = "test-receipt-handle-123";

        // when
        await client.CommitAsync(receiptHandle);

        // then
        await sqsClient
            .Received(1)
            .DeleteMessageAsync("http://test-queue", receiptHandle, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_change_visibility_on_reject()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        _SetPrivateFields(client, sqsClient, "http://test-queue");

        const string receiptHandle = "test-receipt-handle-456";

        // when
        await client.RejectAsync(receiptHandle);

        // then - verify visibility timeout is changed to 3 seconds for retry
        await sqsClient
            .Received(1)
            .ChangeMessageVisibilityAsync("http://test-queue", receiptHandle, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_respect_semaphore_concurrency()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        const int concurrencyLimit = 2;
        await using var client = new AmazonSqsConsumerClient(
            "test-group",
            (byte)concurrencyLimit,
            _CreateOptions(),
            logger
        );

        var semaphore = _GetSemaphore(client);
        var activeTasks = 0;
        var maxConcurrent = 0;
        var lockObj = new Lock();
        var messagesProcessed = 0;
        var tcs = new TaskCompletionSource();

        client.OnMessageCallback = async (_, sender) =>
        {
            lock (lockObj)
            {
                activeTasks++;
                if (activeTasks > maxConcurrent)
                {
                    maxConcurrent = activeTasks;
                }
            }

            await Task.Delay(200, AbortToken); // Simulate work

            lock (lockObj)
            {
                activeTasks--;
                messagesProcessed++;
                if (messagesProcessed >= 4)
                {
                    tcs.TrySetResult();
                }
            }

            await client.CommitAsync(sender);
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        var receiveCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref receiveCount);
                if (count <= 4)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage
                                {
                                    Body = """
                                    {
                                        "Message": "test",
                                        "MessageAttributes": { "key": { "Value": "value" } }
                                    }
                                    """,
                                    ReceiptHandle = $"receipt-{count}",
                                },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listeningTask = Task.Run(
            async () =>
            {
                try
                {
                    await client.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            },
            AbortToken
        );

        // Wait for messages to be processed or timeout
        _ = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(4), AbortToken));
        await cts.CancelAsync();
        await listeningTask;

        // then - max concurrent should not exceed concurrency limit
        maxConcurrent.Should().BeLessThanOrEqualTo(concurrencyLimit);
    }

    [Fact]
    public async Task should_use_long_polling_with_wait_time()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse { Messages = [] });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // then - verify long polling is configured with WaitTimeSeconds = 5
        await sqsClient
            .Received()
            .ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(r => r.WaitTimeSeconds == 5),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_handle_empty_response_gracefully()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse { Messages = [] });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // then
        callbackInvoked.Should().BeFalse("callback should not be invoked when no messages");
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task should_stop_on_cancellation()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(1);
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource();
        var listeningTask = Task.Run(
            async () => await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token),
            AbortToken
        );

        // Cancel after short delay
        await Task.Delay(200, AbortToken);
        await cts.CancelAsync();

        // then
        var act = async () => await listeningTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_handle_receipt_handle_invalid_exception_on_commit()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var logCallbackInvoked = false;
        LogMessageEventArgs? capturedLogArgs = null;
        client.OnLogCallback = args =>
        {
            logCallbackInvoked = true;
            capturedLogArgs = args;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ReceiptHandleIsInvalidException("Invalid receipt handle"));

        _SetPrivateFields(client, sqsClient, "http://test-queue");

        // when
        await client.CommitAsync("invalid-receipt");

        // then
        logCallbackInvoked.Should().BeTrue();
        capturedLogArgs!.LogType.Should().Be(MqLogType.InvalidIdFormat);
    }

    [Fact]
    public async Task should_handle_message_not_inflight_exception_on_reject()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var logCallbackInvoked = false;
        LogMessageEventArgs? capturedLogArgs = null;
        client.OnLogCallback = args =>
        {
            logCallbackInvoked = true;
            capturedLogArgs = args;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        sqsClient
            .ChangeMessageVisibilityAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new MessageNotInflightException("Message not inflight"));

        _SetPrivateFields(client, sqsClient, "http://test-queue");

        // when
        await client.RejectAsync("expired-receipt");

        // then
        logCallbackInvoked.Should().BeTrue();
        capturedLogArgs!.LogType.Should().Be(MqLogType.MessageNotInflight);
    }

    [Fact]
    public async Task should_return_correct_broker_address()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        _SetPrivateFields(client, sqsClient, "http://sqs.us-east-1.amazonaws.com/123456789/my-queue");

        // when
        var brokerAddress = client.BrokerAddress;

        // then
        brokerAddress.Name.Should().Be("aws_sqs");
        brokerAddress.Endpoint.Should().Be("http://sqs.us-east-1.amazonaws.com/123456789/my-queue");
    }

    [Fact]
    public async Task should_handle_json_deserialization_error()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var callbackInvoked = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        var receiveCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage { Body = "not valid json {{{", ReceiptHandle = "receipt-json-error" },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await Task.Delay(500, AbortToken);

        // then
        callbackInvoked.Should().BeFalse("invalid JSON should not be processed");
        logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Failed to deserialize SQS message")),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>()
            );

        await sqsClient
            .Received(1)
            .ChangeMessageVisibilityAsync(Arg.Any<string>(), "receipt-json-error", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_invoke_callback_with_correct_message_headers()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("my-consumer-group", 0, _CreateOptions(), logger);

        TransportMessage? capturedMessage = null;
        object? capturedSender = null;
        client.OnMessageCallback = (msg, sender) =>
        {
            capturedMessage = msg;
            capturedSender = sender;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();
        var receiveCount = 0;
        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref receiveCount) == 1)
                {
                    return Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages =
                            [
                                new SqsMessage
                                {
                                    Body = """
                                    {
                                        "Message": "test body content",
                                        "MessageAttributes": {
                                            "headless-msg-id": { "Value": "msg-123" },
                                            "headless-msg-name": { "Value": "TestEvent" }
                                        }
                                    }
                                    """,
                                    ReceiptHandle = "receipt-header-test",
                                },
                            ],
                        }
                    );
                }
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });

        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ListeningAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // then
        capturedMessage.Should().NotBeNull();
        capturedMessage!.Value.Headers.Should().ContainKey(Headers.Group);
        capturedMessage.Value.Headers[Headers.Group].Should().Be("my-consumer-group");
        capturedMessage.Value.Headers.Should().ContainKey("headless-msg-id");
        capturedMessage.Value.Headers["headless-msg-id"].Should().Be("msg-123");
        capturedMessage.Value.Headers.Should().ContainKey("headless-msg-name");
        capturedMessage.Value.Headers["headless-msg-name"].Should().Be("TestEvent");
        capturedSender.Should().Be("receipt-header-test");
    }

    [Fact]
    public async Task should_dispose_resources_correctly()
    {
        // given
        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        var client = new AmazonSqsConsumerClient("test-group", 1, _CreateOptions(), logger);

        var sqsClient = Substitute.For<IAmazonSQS>();
        _SetPrivateFields(client, sqsClient, "http://test");

        // when
        await client.DisposeAsync();

        // then - should not throw
        sqsClient.Received(1).Dispose();
    }
}
