// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConsumerClientTests : TestBase
{
    private readonly IOptions<MessagingKafkaOptions> _options = Options.Create(
        new MessagingKafkaOptions { Servers = "localhost:9092" }
    );
    private readonly IServiceProvider _serviceProvider;

    public KafkaConsumerClientTests()
    {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given, when
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("localhost:9092");
    }

    [Fact]
    public async Task should_sanitize_broker_address_when_credentials_are_present()
    {
        // given
        var credentialedOptions = Options.Create(new MessagingKafkaOptions { Servers = "user:secret@broker:9092" });

        // when
        await using var client = new KafkaConsumerClient("test-group", 1, credentialedOptions, _serviceProvider);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("broker:9092");
    }

    [Fact]
    public async Task should_allow_setting_OnMessageCallback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnMessageCallback = (_, _) => Task.CompletedTask;

        // then
        client.OnMessageCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_allow_setting_OnLogCallback()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        client.OnLogCallback = _ => { };

        // then
        client.OnLogCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_subscribing_with_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.SubscribeAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_throw_when_fetching_null_topics()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        var act = async () => await client.FetchTopicsAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FetchTopicsAsync_should_return_topics()
    {
        // given
        var options = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                MainConfig = { ["allow.auto.create.topics"] = "false" },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);
        client.OnLogCallback = _ => { }; // Set callback to avoid null ref

        // when - FetchTopicsAsync will return topics even if admin client fails
        // The topic names are returned regardless of whether topic creation succeeds
        var result = await client.FetchTopicsAsync(["topic1", "topic2"]);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain("topic1");
        result.Should().Contain("topic2");
    }

    [Fact]
    public async Task FetchTopicsAsync_should_use_kafka_override_when_called_via_interface()
    {
        // given
        var adminClient = Substitute.For<IAdminClient>();
        adminClient
            .CreateTopicsAsync(Arg.Any<IEnumerable<TopicSpecification>>(), Arg.Any<CreateTopicsOptions>())
            .Returns(Task.CompletedTask);

        IConsumerClient client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            adminClientFactory: _ => adminClient
        );

        // when
        var result = await client.FetchTopicsAsync(["orders.created", "orders.*"]);

        // then
        result.Should().Contain("orders.created");
        result.Should().Contain(x => x.StartsWith("^orders\\.", StringComparison.Ordinal));
        await adminClient
            .Received(1)
            .CreateTopicsAsync(
                Arg.Is<IEnumerable<TopicSpecification>>(specs =>
                    specs.Select(x => x.Name).SequenceEqual(new[] { "orders.created" })
                ),
                Arg.Any<CreateTopicsOptions>()
            );
    }

    [Fact]
    public async Task should_dispose_successfully()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 2, _options, _serviceProvider);

        // when
        await client.DisposeAsync();

        // then - no exception
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_allow_auto_create_topics_from_config()
    {
        // given
        var optionsWithAutoCreate = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                MainConfig = { ["allow.auto.create.topics"] = "false" },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithAutoCreate, _serviceProvider);

        // then - client created successfully with the config
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_support_custom_headers_builder()
    {
        // given
        var customHeaders = new List<KeyValuePair<string, string>> { new("custom-key", "custom-value") };
        var optionsWithCustomHeaders = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", CustomHeadersBuilder = (_, _) => customHeaders }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, optionsWithCustomHeaders, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_handle_retriable_error_codes()
    {
        // given
        var options = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", RetriableErrorCodes = [ErrorCode.Local_TimedOut] }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then - client created successfully
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_topic_options_for_auto_creation()
    {
        // given
        var options = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                TopicOptions = new KafkaTopicOptions { NumPartitions = 3, ReplicationFactor = 1 },
            }
        );
        await using var client = new KafkaConsumerClient("test-group", 1, options, _serviceProvider);

        // then
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task Connect_should_be_idempotent()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when - call Connect multiple times (it won't actually connect without Kafka)
        // This tests the idempotency of the Connect check
        client.Connect();
        client.Connect();

        // then - no exception
    }

    [Fact]
    public async Task RejectAsync_should_seek_failed_offset()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );
        client.Connect();
        var consumeResult = new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders.created", new Partition(2), new Offset(17)),
            Message = new Message<string, byte[]> { Value = [1], Headers = new Confluent.Kafka.Headers() },
        };

        // when
        await client.RejectAsync(consumeResult);

        // then
        consumer.Received(1).Seek(consumeResult.TopicPartitionOffset);
    }

    [Fact]
    public async Task ListeningAsync_should_process_messages_sequentially_when_concurrency_is_requested()
    {
        // given
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        LogMessageEventArgs? configurationWarning = null;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                var callIndex = Interlocked.Increment(ref consumeCallCount);
                return callIndex switch
                {
                    1 => new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(0)
                        ),
                        Message = new Message<string, byte[]> { Value = [1], Headers = new Confluent.Kafka.Headers() },
                    },
                    2 => new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(1)
                        ),
                        Message = new Message<string, byte[]> { Value = [2], Headers = new Confluent.Kafka.Headers() },
                    },
                    _ => null!,
                };
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            2,
            _options,
            _serviceProvider,
            consumerFactory: _ => consumer
        );

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;

        client.OnMessageCallback = async (_, _) =>
        {
            var current = Interlocked.Increment(ref callbackCount);
            if (current == 1)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.ConfigureAwait(false);
                return;
            }

            secondStarted.TrySetResult();
        };
        client.OnLogCallback = args =>
        {
            if (args.LogType == MqLogType.TransportConfigurationWarning)
            {
                configurationWarning = args;
            }
        };

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        await Task.Delay(100, AbortToken);

        // then
        secondStarted.Task.IsCompleted.Should().BeFalse();
        configurationWarning.Should().NotBeNull();
        configurationWarning!.Reason.Should().Contain("groupConcurrent=2");
        configurationWarning.Reason.Should().Contain("sequentially");

        releaseFirst.TrySetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);

        await cts.CancelAsync();
        await listeningTask.WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
    }

    // -------------------------------------------------------------------------
    // PauseAsync / ResumeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PauseAsync_is_idempotent_when_called_twice()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when — no consumer built yet, but should not throw
        await client.PauseAsync();
        await client.PauseAsync();

        // then — no exception
    }

    [Fact]
    public async Task ResumeAsync_is_noop_when_not_paused()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when — never paused, resume should be a no-op
        await client.ResumeAsync();

        // then — no exception
    }

    [Fact]
    public async Task PauseAsync_then_ResumeAsync_completes_full_cycle()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync();
        await client.ResumeAsync();

        // then — no exception, state restored
    }

    [Fact]
    public async Task PauseAsync_is_noop_after_disposal()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.PauseAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_noop_after_disposal()
    {
        // given
        var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);
        await client.DisposeAsync();

        // when — should not throw
        await client.ResumeAsync();
    }

    [Fact]
    public async Task ResumeAsync_is_idempotent_after_resume()
    {
        // given
        await using var client = new KafkaConsumerClient("test-group", 1, _options, _serviceProvider);

        // when
        await client.PauseAsync();
        await client.ResumeAsync();
        await client.ResumeAsync(); // second resume is no-op

        // then — no exception
    }

    [Fact]
    public async Task should_seek_back_when_custom_headers_builder_throws()
    {
        // given
        var throwingOptions = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "localhost:9092",
                CustomHeadersBuilder = (_, _) => throw new InvalidOperationException("bad header builder"),
            }
        );

        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var consumeCallCount = 0;
        consumer
            .Consume(Arg.Any<TimeSpan>())
            .Returns(_ =>
            {
                return Interlocked.Increment(ref consumeCallCount) == 1
                    ? new ConsumeResult<string, byte[]>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset(
                            "orders.created",
                            new Partition(0),
                            new Offset(5)
                        ),
                        Message = new Message<string, byte[]> { Value = [1], Headers = new Confluent.Kafka.Headers() },
                    }
                    : null!;
            });

        await using var client = new KafkaConsumerClient(
            "test-group",
            1,
            throwingOptions,
            _serviceProvider,
            consumerFactory: _ => consumer
        );

        var callbackInvoked = false;
        LogMessageEventArgs? loggedError = null;
        client.OnMessageCallback = (_, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        };
        client.OnLogCallback = args =>
        {
            if (args.LogType == MqLogType.ConsumeError)
                loggedError = args;
        };

        using var cts = new CancellationTokenSource();

        // when
        var listeningTask = client.ListeningAsync(TimeSpan.FromMilliseconds(10), cts.Token).AsTask();
        await Task.Delay(300, AbortToken);
        await cts.CancelAsync();
        await listeningTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        // then — callback should not be invoked, offset should be seeked back
        callbackInvoked.Should().BeFalse();
        consumer.Received(1).Seek(Arg.Is<TopicPartitionOffset>(tpo => tpo.Offset == 5));
        loggedError.Should().NotBeNull();
        loggedError!.Reason.Should().Contain("bad header builder");
    }
}
