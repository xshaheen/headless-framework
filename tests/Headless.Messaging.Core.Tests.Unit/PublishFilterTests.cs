// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PublishFilterTests : TestBase
{
    [Fact]
    public async Task should_return_completed_task_for_default_executing()
    {
        // given
        var filter = new TestPublishFilter();
        var context = _CreatePublishingContext();

        // when
        var task = filter.OnPublishExecutingAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_return_completed_task_for_default_executed()
    {
        // given
        var filter = new TestPublishFilter();
        var context = _CreatePublishedContext();

        // when
        var task = filter.OnPublishExecutedAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_return_completed_task_for_default_exception()
    {
        // given
        var filter = new TestPublishFilter();
        var context = _CreatePublishExceptionContext();

        // when
        var task = filter.OnPublishExceptionAsync(context);

        // then
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task should_allow_override_of_executing_only_leaving_other_methods_at_default()
    {
        // given
        var filter = new ExecutingOnlyPublishFilter();

        // when
        await filter.OnPublishExecutingAsync(_CreatePublishingContext());
        await filter.OnPublishExecutedAsync(_CreatePublishedContext());
        await filter.OnPublishExceptionAsync(_CreatePublishExceptionContext());

        // then
        filter.ExecutingCalled.Should().BeTrue();
        filter.ExecutedCalled.Should().BeFalse();
        filter.ExceptionCalled.Should().BeFalse();
    }

    [Fact]
    public void should_create_publishing_context_with_content_and_options()
    {
        // given
        var content = new FilterTestMessage("hello");
        var options = new PublishOptions { TenantId = "acme" };

        // when
        var context = new PublishingContext(content, typeof(FilterTestMessage), options, delayTime: null);

        // then
        context.Content.Should().Be(content);
        context.MessageType.Should().Be<FilterTestMessage>();
        context.Options.Should().Be(options);
        context.DelayTime.Should().BeNull();
    }

    [Fact]
    public void should_allow_filters_to_mutate_options_via_with_expression()
    {
        // given
        var initial = new PublishOptions { CorrelationId = "corr-1" };
        var context = new PublishingContext(content: null, typeof(FilterTestMessage), initial, delayTime: null);

        // when
        context.Options = (context.Options ?? new PublishOptions()) with
        {
            TenantId = "acme",
        };

        // then
        context.Options.Should().NotBeNull();
        context.Options!.TenantId.Should().Be("acme");
        context.Options.CorrelationId.Should().Be("corr-1"); // preserved across the with expression
        initial.TenantId.Should().BeNull(); // original instance untouched
    }

    [Fact]
    public void should_allow_filters_to_mutate_delay_time()
    {
        // given
        var context = new PublishingContext(
            content: null,
            typeof(FilterTestMessage),
            options: null,
            delayTime: TimeSpan.FromSeconds(5)
        )
        {
            // when
            DelayTime = TimeSpan.FromMinutes(10),
        };

        // then
        context.DelayTime.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_default_delay_time_to_null_for_immediate_publishes()
    {
        // when
        var context = new PublishingContext(content: null, typeof(FilterTestMessage), options: null, delayTime: null);

        // then
        context.DelayTime.Should().BeNull();
    }

    [Fact]
    public void should_create_published_context_with_options()
    {
        // given
        var options = new PublishOptions { TenantId = "acme" };

        // when
        var context = new PublishedContext(content: null, typeof(FilterTestMessage), options, delayTime: null);

        // then
        context.Options.Should().Be(options);
    }

    [Fact]
    public void should_create_publish_exception_context_with_exception_and_default_handled_false()
    {
        // given
        var exception = new InvalidOperationException("transport failed");

        // when
        var context = new PublishExceptionContext(
            content: null,
            typeof(FilterTestMessage),
            options: null,
            delayTime: null,
            exception
        );

        // then
        context.Exception.Should().BeSameAs(exception);
        context.ExceptionHandled.Should().BeFalse();
    }

    [Fact]
    public void should_allow_filters_to_set_exception_handled_silent_swallow()
    {
        // given
        var context = new PublishExceptionContext(
            content: null,
            typeof(FilterTestMessage),
            options: null,
            delayTime: null,
            new InvalidOperationException("transport failed")
        )
        {
            // when
            ExceptionHandled = true,
        };

        // then
        context.ExceptionHandled.Should().BeTrue();
    }

    [Fact]
    public void should_reject_null_message_type_in_publishing_context()
    {
        // when
        var act = () => new PublishingContext(content: null, messageType: null!, options: null, delayTime: null);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_reject_null_exception_in_publish_exception_context()
    {
        // when
        var act = () =>
            new PublishExceptionContext(
                content: null,
                typeof(FilterTestMessage),
                options: null,
                delayTime: null,
                exception: null!
            );

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void publish_options_should_have_value_equality_after_record_conversion()
    {
        // given
        var a = new PublishOptions { TenantId = "acme", MessageId = "1" };
        var b = new PublishOptions { TenantId = "acme", MessageId = "1" };
        var c = new PublishOptions { TenantId = "globex", MessageId = "1" };

        // then
        a.Should().Be(b);
        a.Should().NotBe(c);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    private static PublishingContext _CreatePublishingContext()
    {
        return new PublishingContext(
            content: new FilterTestMessage("test"),
            typeof(FilterTestMessage),
            options: null,
            delayTime: null
        );
    }

    private static PublishedContext _CreatePublishedContext()
    {
        return new PublishedContext(
            content: new FilterTestMessage("test"),
            typeof(FilterTestMessage),
            options: null,
            delayTime: null
        );
    }

    private static PublishExceptionContext _CreatePublishExceptionContext()
    {
        return new PublishExceptionContext(
            content: new FilterTestMessage("test"),
            typeof(FilterTestMessage),
            options: null,
            delayTime: null,
            exception: new InvalidOperationException("test error")
        );
    }
}

// Default-behavior implementation
internal sealed class TestPublishFilter : PublishFilter;

// Override only OnPublishExecutingAsync; verify the other two stay at base no-op
internal sealed class ExecutingOnlyPublishFilter : PublishFilter
{
    public bool ExecutingCalled { get; private set; }
    public bool ExecutedCalled { get; private set; }
    public bool ExceptionCalled { get; private set; }

    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        ExecutingCalled = true;
        return ValueTask.CompletedTask;
    }
}
