// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Configuration;

public sealed class MessagingOptionsValidationTests : TestBase
{
    [Fact]
    public void should_validate_topic_name_length()
    {
        // given
        var options = _CreateOptions();
        var longTopic = new string('a', 256);

        // when
        var act = () => options.WithTopicMapping<TestMessage>(longTopic);

        // then
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*exceeds maximum length of 255*")
            .WithParameterName("topic");
    }

    [Fact]
    public void should_accept_max_length_topic_name()
    {
        // given
        var options = _CreateOptions();
        var maxLengthTopic = new string('a', 255);

        // when
        var result = options.WithTopicMapping<TestMessage>(maxLengthTopic);

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_validate_topic_name_characters()
    {
        // given
        var options = _CreateOptions();

        // when/then - invalid characters
        var act1 = () => options.WithTopicMapping<TestMessage>("topic@name");
        act1.Should().Throw<ArgumentException>().WithMessage("*invalid character*@*");

        var act2 = () => options.WithTopicMapping<TestMessage>("topic#name");
        act2.Should().Throw<ArgumentException>().WithMessage("*invalid character*#*");

        var act3 = () => options.WithTopicMapping<TestMessage>("topic name");
        act3.Should().Throw<ArgumentException>().WithMessage("*invalid character* *");

        var act4 = () => options.WithTopicMapping<TestMessage>("topic/name");
        act4.Should().Throw<ArgumentException>().WithMessage("*invalid character*/*");
    }

    [Fact]
    public void should_accept_valid_topic_name_characters()
    {
        // given
        var options = _CreateOptions();

        // when/then - valid characters: alphanumeric, dots, hyphens, underscores
        var result1 = options.WithTopicMapping<TestMessage>("valid.topic-name_123");
        result1.Should().BeSameAs(options);
    }

    [Fact]
    public void should_reject_leading_dots()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>(".leading.dot");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot start or end with a dot*");
    }

    [Fact]
    public void should_reject_trailing_dots()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("trailing.dot.");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot start or end with a dot*");
    }

    [Fact]
    public void should_reject_consecutive_dots()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("topic..name");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot contain consecutive dots*");
    }

    [Fact]
    public void should_set_default_retry_count()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.FailedRetryCount.Should().Be(50);
    }

    [Fact]
    public void should_set_default_parallel_settings()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.EnableSubscriberParallelExecute.Should().BeFalse();
        options.SubscriberParallelExecuteThreadCount.Should().Be(Environment.ProcessorCount);
        options.SubscriberParallelExecuteBufferFactor.Should().Be(1);
        options.EnablePublishParallelSend.Should().BeFalse();
        options.ConsumerThreadCount.Should().Be(1);
    }

    [Fact]
    public void should_set_default_message_expiration()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.SucceedMessageExpiredAfter.Should().Be(24 * 3600); // 24 hours
        options.FailedMessageExpiredAfter.Should().Be(15 * 24 * 3600); // 15 days
    }

    [Fact]
    public void should_set_default_retry_interval()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.FailedRetryInterval.Should().Be(60); // 60 seconds
    }

    [Fact]
    public void should_set_default_backoff_strategy()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.RetryBackoffStrategy.Should().NotBeNull();
        options.RetryBackoffStrategy.Should().BeOfType<ExponentialBackoffStrategy>();
    }

    [Fact]
    public void should_set_default_version()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.Version.Should().Be("v1");
    }

    [Fact]
    public void should_reject_duplicate_topic_mapping_with_different_topic()
    {
        // given
        var options = _CreateOptions();
        options.WithTopicMapping<TestMessage>("first.topic");

        // when
        var act = () => options.WithTopicMapping<TestMessage>("second.topic");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already mapped to topic*first.topic*Cannot map to*second.topic*");
    }

    [Fact]
    public void should_allow_same_topic_mapping()
    {
        // given
        var options = _CreateOptions();
        options.WithTopicMapping<TestMessage>("same.topic");

        // when
        var result = options.WithTopicMapping<TestMessage>("same.topic");

        // then - should not throw
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_reject_null_topic()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_empty_topic()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_whitespace_topic()
    {
        // given
        var options = _CreateOptions();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("   ");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_set_default_scheduler_batch_size()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.SchedulerBatchSize.Should().Be(1000);
    }

    [Fact]
    public void should_set_default_collector_cleaning_interval()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.CollectorCleaningInterval.Should().Be(300); // 5 minutes
    }

    [Fact]
    public void should_set_default_fallback_window()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.FallbackWindowLookbackSeconds.Should().Be(240); // 4 minutes
    }

    [Fact]
    public void should_have_default_json_serializer_options()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.JsonSerializerOptions.Should().NotBeNull();
    }

    [Fact]
    public void should_have_storage_lock_disabled_by_default()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.UseStorageLock.Should().BeFalse();
    }

    [Fact]
    public void should_have_null_publish_batch_size_by_default()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.PublishBatchSize.Should().BeNull();
    }

    private static MessagingOptions _CreateOptions()
    {
        var services = new ServiceCollection();
        var registry = new ConsumerRegistry();
        var options = new MessagingOptions { Services = services, Registry = registry };
        return options;
    }

    private sealed class TestMessage;
}
