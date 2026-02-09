// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ScheduledTriggerTests : TestBase
{
    [Fact]
    public void should_create_with_required_properties()
    {
        // given
        var scheduledTime = DateTimeOffset.UtcNow;
        var jobName = Faker.Lorem.Word();
        var attempt = Faker.Random.Int(1, 10);

        // when
        var trigger = new ScheduledTrigger
        {
            ScheduledTime = scheduledTime,
            JobName = jobName,
            Attempt = attempt,
        };

        // then
        trigger.ScheduledTime.Should().Be(scheduledTime);
        trigger.JobName.Should().Be(jobName);
        trigger.Attempt.Should().Be(attempt);
    }

    [Fact]
    public void should_default_optional_properties_to_null_when_not_set()
    {
        // when
        var trigger = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "test-job",
            Attempt = 1,
        };

        // then
        trigger.CronExpression.Should().BeNull();
        trigger.Payload.Should().BeNull();
    }

    [Fact]
    public void should_set_cron_expression_when_provided()
    {
        // given
        const string cron = "0 */5 * * *";

        // when
        var trigger = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "cron-job",
            Attempt = 1,
            CronExpression = cron,
        };

        // then
        trigger.CronExpression.Should().Be(cron);
    }

    [Fact]
    public void should_set_payload_when_provided()
    {
        // given
        const string payload = """{"orderId":"abc-123","amount":42.5}""";

        // when
        var trigger = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "payload-job",
            Attempt = 1,
            Payload = payload,
        };

        // then
        trigger.Payload.Should().Be(payload);
    }

    [Fact]
    public void should_set_all_properties_when_fully_initialized()
    {
        // given
        var scheduledTime = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        const string jobName = "full-job";
        const int attempt = 3;
        const string cron = "0 0 */6 * *";
        const string payload = """{"key":"value"}""";

        // when
        var trigger = new ScheduledTrigger
        {
            ScheduledTime = scheduledTime,
            JobName = jobName,
            Attempt = attempt,
            CronExpression = cron,
            Payload = payload,
        };

        // then
        trigger.ScheduledTime.Should().Be(scheduledTime);
        trigger.JobName.Should().Be(jobName);
        trigger.Attempt.Should().Be(attempt);
        trigger.CronExpression.Should().Be(cron);
        trigger.Payload.Should().Be(payload);
    }

    [Fact]
    public void should_be_sealed_record()
    {
        // then
        typeof(ScheduledTrigger).Should().BeSealed();
        typeof(ScheduledTrigger).BaseType.Should().Be(typeof(object));
        typeof(ScheduledTrigger).GetInterfaces().Should().Contain(typeof(IEquatable<ScheduledTrigger>));
    }

    [Fact]
    public void should_support_value_equality_when_same_properties()
    {
        // given
        var scheduledTime = DateTimeOffset.UtcNow;

        var trigger1 = new ScheduledTrigger
        {
            ScheduledTime = scheduledTime,
            JobName = "job",
            Attempt = 1,
        };

        var trigger2 = new ScheduledTrigger
        {
            ScheduledTime = scheduledTime,
            JobName = "job",
            Attempt = 1,
        };

        // then
        trigger1.Should().Be(trigger2);
    }

    [Fact]
    public void should_not_be_equal_when_properties_differ()
    {
        // given
        var trigger1 = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "job-a",
            Attempt = 1,
        };

        var trigger2 = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "job-b",
            Attempt = 1,
        };

        // then
        trigger1.Should().NotBe(trigger2);
    }

    [Fact]
    public void should_support_with_expression_when_copying()
    {
        // given
        var original = new ScheduledTrigger
        {
            ScheduledTime = DateTimeOffset.UtcNow,
            JobName = "original",
            Attempt = 1,
        };

        // when
        var copy = original with
        {
            Attempt = 2,
        };

        // then
        copy.Attempt.Should().Be(2);
        copy.JobName.Should().Be("original");
        copy.ScheduledTime.Should().Be(original.ScheduledTime);
    }
}
