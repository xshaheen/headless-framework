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
        var options = _CreateBuilder();
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
        var options = _CreateBuilder();
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
        var options = _CreateBuilder();

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
        var options = _CreateBuilder();

        // when/then - valid characters: alphanumeric, dots, hyphens, underscores
        var result1 = options.WithTopicMapping<TestMessage>("valid.topic-name_123");
        result1.Should().BeSameAs(options);
    }

    [Fact]
    public void should_reject_leading_dots()
    {
        // given
        var options = _CreateBuilder();

        // when
        var act = () => options.WithTopicMapping<TestMessage>(".leading.dot");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot start or end with a dot*");
    }

    [Fact]
    public void should_reject_trailing_dots()
    {
        // given
        var options = _CreateBuilder();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("trailing.dot.");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot start or end with a dot*");
    }

    [Fact]
    public void should_reject_consecutive_dots()
    {
        // given
        var options = _CreateBuilder();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("topic..name");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*cannot contain consecutive dots*");
    }

    [Fact]
    public void should_set_default_retry_policy()
    {
        // given
        var options = new MessagingOptions();

        // then
        options.RetryPolicy.MaxInlineRetries.Should().Be(2);
        options.RetryPolicy.MaxPersistedRetries.Should().Be(15);
        options.RetryPolicy.InitialDispatchGrace.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryPolicy.BackoffStrategy.Should().BeOfType<ExponentialBackoffStrategy>();
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
    public void should_reject_retry_policy_with_negative_max_persisted_retries()
    {
        // given
        var options = new RetryPolicyOptions { MaxPersistedRetries = -1 };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.MaxPersistedRetries));
    }

    [Fact]
    public void should_accept_retry_policy_with_zero_persisted_retries_and_zero_inline_retries()
    {
        // No retries at all (single attempt only) is a valid configuration.
        var options = new RetryPolicyOptions { MaxPersistedRetries = 0, MaxInlineRetries = 0 };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_reject_retry_policy_with_negative_max_inline_retries()
    {
        // given
        var options = new RetryPolicyOptions { MaxInlineRetries = -1 };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.MaxInlineRetries));
    }

    [Fact]
    public void should_reject_retry_policy_with_non_positive_initial_dispatch_grace()
    {
        // given
        var options = new RetryPolicyOptions { InitialDispatchGrace = TimeSpan.Zero };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.InitialDispatchGrace));
    }

    [Fact]
    public void should_reject_retry_policy_when_initial_dispatch_grace_exceeds_one_hour()
    {
        // given
        var options = new RetryPolicyOptions { InitialDispatchGrace = TimeSpan.FromHours(2) };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.InitialDispatchGrace));
    }

    [Fact]
    public void should_reject_retry_policy_with_non_positive_on_exhausted_timeout()
    {
        // given
        var options = new RetryPolicyOptions { OnExhaustedTimeout = TimeSpan.Zero };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.OnExhaustedTimeout));
    }

    [Fact]
    public void should_reject_retry_policy_when_on_exhausted_timeout_exceeds_one_hour()
    {
        // given
        var options = new RetryPolicyOptions { OnExhaustedTimeout = TimeSpan.FromHours(2) };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.OnExhaustedTimeout));
    }

    [Fact]
    public void should_reject_retry_policy_with_null_backoff_strategy()
    {
        // given
        var options = new RetryPolicyOptions { BackoffStrategy = null! };

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(RetryPolicyOptions.BackoffStrategy));
    }

    [Fact]
    public void should_accept_default_retry_policy()
    {
        // given
        var options = new RetryPolicyOptions();

        // when
        var result = new RetryPolicyOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_reject_messaging_options_with_null_retry_policy()
    {
        // given — force the _retryPolicy backing field to null via reflection. RetryPolicy is a
        // get-only public property with a named backing field, so reflection is stable (no
        // compiler-generated naming). Reflection lives in the test rather than in a production
        // test seam.
        var options = new MessagingOptions();
        var field = typeof(MessagingOptions).GetField(
            "_retryPolicy",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        field.Should().NotBeNull("named backing field _retryPolicy must exist on MessagingOptions");
        field!.SetValue(options, null);

        // when
        var result = new MessagingOptionsValidator().Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(MessagingOptions.RetryPolicy));
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
        var options = _CreateBuilder();
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
        var options = _CreateBuilder();
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
        var options = _CreateBuilder();

        // when
        var act = () => options.WithTopicMapping<TestMessage>(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_empty_topic()
    {
        // given
        var options = _CreateBuilder();

        // when
        var act = () => options.WithTopicMapping<TestMessage>("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_whitespace_topic()
    {
        // given
        var options = _CreateBuilder();

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

    private static MessagingSetupBuilder _CreateBuilder()
    {
        var services = new ServiceCollection();
        var registry = new ConsumerRegistry();
        var options = new MessagingOptions();
        return new MessagingSetupBuilder(services, options, registry);
    }

    private sealed class TestMessage;
}
