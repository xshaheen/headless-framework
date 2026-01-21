// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Framework.Testing.Tests;
using Headless.Messaging.AwsSqs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AmazonSqsConsumerClientTests : TestBase
{
    [Fact]
    public async Task should_log_error_and_release_semaphore_when_consumeAsync_throws_in_concurrent_mode()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var exceptionThrown = new InvalidOperationException("Test exception");

        // Configure callback to throw
        client.OnMessageCallback = (_, _) => throw exceptionThrown;

        // Create mock SQS client
        var sqsClient = Substitute.For<IAmazonSQS>();

        // Mock ReceiveMessage to return a valid message
        var receiveResponse = new ReceiveMessageResponse
        {
            Messages =
            [
                new Message
                {
                    Body = """
                        {
                            "Message": "test message",
                            "MessageAttributes": {
                                "test-key": {
                                    "Value": "test-value"
                                }
                            }
                        }
                        """,
                    ReceiptHandle = "test-receipt-handle",
                },
            ],
        };

        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(receiveResponse);

        // Mock queue creation
        sqsClient
            .CreateQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "http://test" });

        // Mock GetAttributes
        sqsClient
            .GetAttributesAsync(Arg.Any<string>())
            .Returns(
                Task.FromResult(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = "arn:aws:sqs:test" }
                )
            );

        // Use reflection to set the private _sqsClient field
        var sqsClientField = typeof(AmazonSqsConsumerClient).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        sqsClientField!.SetValue(client, sqsClient);

        var queueUrlField = typeof(AmazonSqsConsumerClient).GetField(
            "_queueUrl",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        queueUrlField!.SetValue(client, "http://test");

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

        // Verify RejectAsync was attempted
        await sqsClient
            .Received(1)
            .ChangeMessageVisibilityAsync(Arg.Any<string>(), "test-receipt-handle", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_log_error_when_reject_fails_after_consume_error()
    {
        // given
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var consumeException = new InvalidOperationException("Consume failed");
        var rejectException = new MessageNotInflightException("Reject failed");

        // Configure callback to throw
        client.OnMessageCallback = (_, _) => throw consumeException;

        // Create mock SQS client
        var sqsClient = Substitute.For<IAmazonSQS>();

        // Mock ReceiveMessage to return a valid message
        var receiveResponse = new ReceiveMessageResponse
        {
            Messages =
            [
                new Message
                {
                    Body = """
                        {
                            "Message": "test message",
                            "MessageAttributes": {
                                "test-key": {
                                    "Value": "test-value"
                                }
                            }
                        }
                        """,
                    ReceiptHandle = "test-receipt-handle",
                },
            ],
        };

        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(receiveResponse);

        // Mock ChangeMessageVisibility to throw
        sqsClient
            .ChangeMessageVisibilityAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(rejectException);

        // Mock queue creation
        sqsClient
            .CreateQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "http://test" });

        sqsClient
            .GetAttributesAsync(Arg.Any<string>())
            .Returns(
                Task.FromResult(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = "arn:aws:sqs:test" }
                )
            );

        // Use reflection to set the private fields
        var sqsClientField = typeof(AmazonSqsConsumerClient).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        sqsClientField!.SetValue(client, sqsClient);

        var queueUrlField = typeof(AmazonSqsConsumerClient).GetField(
            "_queueUrl",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        queueUrlField!.SetValue(client, "http://test");

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

        // Give time for fire-and-forget task
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
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 1, options, logger);

        var messageReceived = false;
        client.OnMessageCallback = (_, _) =>
        {
            messageReceived = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();

        // Return invalid message (null MessageAttributes)
        var receiveResponse = new ReceiveMessageResponse
        {
            Messages = [new Message { Body = "{}", ReceiptHandle = "test-receipt" }],
        };

        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(receiveResponse);

        sqsClient
            .CreateQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "http://test" });

        sqsClient
            .GetAttributesAsync(Arg.Any<string>())
            .Returns(
                Task.FromResult(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = "arn:aws:sqs:test" }
                )
            );

        var sqsClientField = typeof(AmazonSqsConsumerClient).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        sqsClientField!.SetValue(client, sqsClient);

        var queueUrlField = typeof(AmazonSqsConsumerClient).GetField(
            "_queueUrl",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        queueUrlField!.SetValue(client, "http://test");

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
        var options = Options.Create(
            new AmazonSqsOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SqsServiceUrl = "http://localhost:4566",
                SnsServiceUrl = "http://localhost:4566",
            }
        );

        var logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
        await using var client = new AmazonSqsConsumerClient("test-group", 0, options, logger); // groupConcurrent = 0

        var callbackExecuted = false;
        client.OnMessageCallback = (_, _) =>
        {
            callbackExecuted = true;
            return Task.CompletedTask;
        };

        var sqsClient = Substitute.For<IAmazonSQS>();

        var receiveResponse = new ReceiveMessageResponse
        {
            Messages =
            [
                new Message
                {
                    Body = """
                        {
                            "Message": "test",
                            "MessageAttributes": {
                                "key": {
                                    "Value": "value"
                                }
                            }
                        }
                        """,
                    ReceiptHandle = "receipt",
                },
            ],
        };

        sqsClient
            .ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(receiveResponse);

        sqsClient
            .CreateQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateQueueResponse { QueueUrl = "http://test" });

        sqsClient
            .GetAttributesAsync(Arg.Any<string>())
            .Returns(
                Task.FromResult(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["QueueArn"] = "arn:aws:sqs:test" }
                )
            );

        var sqsClientField = typeof(AmazonSqsConsumerClient).GetField(
            "_sqsClient",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        sqsClientField!.SetValue(client, sqsClient);

        var queueUrlField = typeof(AmazonSqsConsumerClient).GetField(
            "_queueUrl",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        queueUrlField!.SetValue(client, "http://test");

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
}
