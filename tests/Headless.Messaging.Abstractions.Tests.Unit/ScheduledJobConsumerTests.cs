// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ScheduledJobConsumerTests : TestBase
{
    [Fact]
    public async Task should_map_scheduled_trigger_into_base_job_context()
    {
        // given
        var consumer = new TestJobConsumer();
        var trigger = new ScheduledTrigger
        {
            JobName = "daily-job",
            Attempt = 3,
            ScheduledTime = DateTimeOffset.UtcNow,
            CronExpression = "0 0 0 * * *",
            Payload = null,
        };

        // when
        await consumer.Consume(_CreateContext(trigger), AbortToken);

        // then
        consumer.LastContext.Should().NotBeNull();
        consumer.LastContext!.JobName.Should().Be("daily-job");
        consumer.LastContext.Attempt.Should().Be(3);
        consumer.LastContext.CronExpression.Should().Be("0 0 0 * * *");
    }

    [Fact]
    public async Task should_deserialize_typed_payload_for_generic_job_consumer()
    {
        // given
        var consumer = new TestTypedJobConsumer();
        var trigger = new ScheduledTrigger
        {
            JobName = "typed-job",
            Attempt = 1,
            ScheduledTime = DateTimeOffset.UtcNow,
            Payload = """{"name":"report","count":4}""",
        };

        // when
        await consumer.Consume(_CreateContext(trigger), AbortToken);

        // then
        consumer.LastContext.Should().NotBeNull();
        consumer.LastContext!.Payload.Should().NotBeNull();
        consumer.LastContext.Payload!.Name.Should().Be("report");
        consumer.LastContext.Payload.Count.Should().Be(4);
    }

    [Fact]
    public async Task should_pass_raw_string_for_string_payload_consumer()
    {
        // given
        var consumer = new TestStringPayloadConsumer();
        var trigger = new ScheduledTrigger
        {
            JobName = "string-job",
            Attempt = 1,
            ScheduledTime = DateTimeOffset.UtcNow,
            Payload = "raw-payload",
        };

        // when
        await consumer.Consume(_CreateContext(trigger), AbortToken);

        // then
        consumer.LastContext.Should().NotBeNull();
        consumer.LastContext!.Payload.Should().Be("raw-payload");
    }

    private static ConsumeContext<ScheduledTrigger> _CreateContext(ScheduledTrigger trigger)
    {
        return new ConsumeContext<ScheduledTrigger>
        {
            Message = trigger,
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = Guid.NewGuid().ToString("N"),
            Topic = trigger.JobName,
            Timestamp = DateTimeOffset.UtcNow,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };
    }

    private sealed class TestJobConsumer : ScheduledJobConsumer
    {
        public ScheduledJobExecutionContext? LastContext { get; private set; }

        protected override ValueTask ExecuteAsync(
            ScheduledJobExecutionContext context,
            CancellationToken cancellationToken
        )
        {
            LastContext = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTypedJobConsumer : ScheduledJobConsumer<TestPayload>
    {
        public ScheduledJobExecutionContext<TestPayload>? LastContext { get; private set; }

        protected override ValueTask ExecuteAsync(
            ScheduledJobExecutionContext<TestPayload> context,
            CancellationToken cancellationToken
        )
        {
            LastContext = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestStringPayloadConsumer : ScheduledJobConsumer<string>
    {
        public ScheduledJobExecutionContext<string>? LastContext { get; private set; }

        protected override ValueTask ExecuteAsync(
            ScheduledJobExecutionContext<string> context,
            CancellationToken cancellationToken
        )
        {
            LastContext = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record TestPayload(string Name, int Count);
}
