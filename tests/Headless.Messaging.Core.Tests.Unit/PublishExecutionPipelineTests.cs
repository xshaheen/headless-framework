// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Tests covering <see cref="IPublishExecutionPipeline"/> — filter chain ordering,
/// options/delay mutation propagation, exception phase scoping, and silent-swallow semantics.
/// </summary>
public sealed class PublishExecutionPipelineTests : TestBase
{
    [Fact]
    public async Task should_invoke_inner_publish_with_caller_options_when_no_filters_registered()
    {
        // given
        var services = new ServiceCollection();
        var pipeline = _BuildPipeline(services);
        PublishOptions? observed = null;

        // when
        await pipeline.ExecuteAsync(
            content: new SimplePayload("hi"),
            options: new PublishOptions { TenantId = "acme" },
            delayTime: null,
            innerPublish: (opts, _, _) =>
            {
                observed = opts;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        observed.Should().NotBeNull();
        observed!.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task should_invoke_filters_forward_in_executing_then_inner_then_reverse_in_executed()
    {
        // given
        var recorder = new PublishCallRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        new MessagingBuilder(services).AddPublishFilter<RecordingPublishFilterA>().AddPublishFilter<RecordingPublishFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync<SimplePayload>(
            content: new SimplePayload("hi"),
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner");
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then
        recorder.Calls.Should().Equal(
            "A.publishing",
            "B.publishing",
            "inner",
            "B.published",
            "A.published"
        );
    }

    [Fact]
    public async Task should_thread_filter_mutated_options_through_to_inner_publish()
    {
        // given
        var recorder = new PublishCallRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        new MessagingBuilder(services).AddPublishFilter<TenantStampingFilter>();
        var pipeline = _BuildPipeline(services);
        PublishOptions? observed = null;

        // when
        await pipeline.ExecuteAsync<SimplePayload>(
            content: new SimplePayload("hi"),
            options: null,
            delayTime: null,
            innerPublish: (opts, _, _) =>
            {
                observed = opts;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then — filter set TenantId via record `with` expression; inner sees it
        observed.Should().NotBeNull();
        observed!.TenantId.Should().Be("acme-from-filter");
    }

    [Fact]
    public async Task should_thread_filter_mutated_delay_through_to_inner_publish()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(new PublishCallRecorder());
        new MessagingBuilder(services).AddPublishFilter<DelayMultiplyingFilter>();
        var pipeline = _BuildPipeline(services);
        TimeSpan? observedDelay = null;

        // when
        await pipeline.ExecuteAsync<SimplePayload>(
            content: new SimplePayload("hi"),
            options: null,
            delayTime: TimeSpan.FromSeconds(5),
            innerPublish: (_, delay, _) =>
            {
                observedDelay = delay;
                return Task.CompletedTask;
            },
            cancellationToken: AbortToken
        );

        // then — filter doubled the delay
        observedDelay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task should_invoke_exception_phase_in_reverse_when_inner_throws()
    {
        // given
        var recorder = new PublishCallRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        new MessagingBuilder(services).AddPublishFilter<RecordingPublishFilterA>().AddPublishFilter<RecordingPublishFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync<SimplePayload>(
                content: new SimplePayload("hi"),
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) =>
                {
                    recorder.Record("inner.throw");
                    return Task.FromException(new InvalidOperationException("inner boom"));
                },
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner boom");
        recorder.Calls.Should().Equal(
            "A.publishing",
            "B.publishing",
            "inner.throw",
            "B.exception",
            "A.exception"
        );
    }

    [Fact]
    public async Task should_swallow_exception_when_filter_sets_exception_handled_silent_swallow()
    {
        // given — caller's PublishAsync await must complete successfully even though inner threw
        var recorder = new PublishCallRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        new MessagingBuilder(services).AddPublishFilter<HandlingPublishFilter>();
        var pipeline = _BuildPipeline(services);

        // when
        await pipeline.ExecuteAsync<SimplePayload>(
            content: new SimplePayload("hi"),
            options: null,
            delayTime: null,
            innerPublish: (_, _, _) =>
            {
                recorder.Record("inner.throw");
                return Task.FromException(new InvalidOperationException("inner boom"));
            },
            cancellationToken: AbortToken
        );

        // then — no throw; recorder shows exception phase ran and filter handled
        recorder.Calls.Should().Contain("inner.throw");
        recorder.Calls.Should().Contain("Handling.exception(handled=true)");
    }

    [Fact]
    public async Task should_only_invoke_exception_phase_for_filters_whose_publishing_phase_completed()
    {
        // given — A enters publishing fine, ExecutingThrow throws, B never enters
        var recorder = new PublishCallRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        new MessagingBuilder(services)
            .AddPublishFilter<RecordingPublishFilterA>()
            .AddPublishFilter<PublishingThrowingFilter>()
            .AddPublishFilter<RecordingPublishFilterB>();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync<SimplePayload>(
                content: new SimplePayload("hi"),
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) =>
                {
                    recorder.Record("inner");
                    return Task.CompletedTask;
                },
                cancellationToken: AbortToken
            );

        // then — A and ExecutingThrow ran; B's publishing AND exception phases must NOT run.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("filter publishing boom");
        recorder.Calls.Should().Equal(
            "A.publishing",
            "ExecutingThrow.publishing",
            "ExecutingThrow.exception",
            "A.exception"
        );
        recorder.Calls.Should().NotContain("B.publishing");
        recorder.Calls.Should().NotContain("B.exception");
        recorder.Calls.Should().NotContain("inner");
    }

    [Fact]
    public async Task should_rethrow_when_no_filters_registered_and_inner_throws()
    {
        // given
        var services = new ServiceCollection();
        var pipeline = _BuildPipeline(services);

        // when
        var act = async () =>
            await pipeline.ExecuteAsync<SimplePayload>(
                content: new SimplePayload("hi"),
                options: null,
                delayTime: null,
                innerPublish: (_, _, _) => Task.FromException(new InvalidOperationException("inner boom")),
                cancellationToken: AbortToken
            );

        // then — zero filters, exception propagates verbatim
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner boom");
    }

    [Fact]
    public async Task should_resolve_separate_filter_instances_per_publish_for_singleton_pipeline()
    {
        // given — pipeline is Singleton; filters are Scoped; per-publish scope guarantees fresh instances
        var services = new ServiceCollection();
        services.AddSingleton<PublishCallRecorder>();
        new MessagingBuilder(services).AddPublishFilter<InstanceTrackingFilter>();
        var pipeline = _BuildPipeline(services);
        var seen = new HashSet<int>();
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerCapturing = (opts, _, _) =>
        {
            // The instance hash leaks via tenant id; capture it
            if (opts?.TenantId is { } id && int.TryParse(id, out var hash))
            {
                seen.Add(hash);
            }

            return Task.CompletedTask;
        };

        // when — two publishes through the same singleton pipeline
        await pipeline.ExecuteAsync<SimplePayload>(new SimplePayload("a"), null, null, innerCapturing, AbortToken);
        await pipeline.ExecuteAsync<SimplePayload>(new SimplePayload("b"), null, null, innerCapturing, AbortToken);

        // then — two different filter instances ran (per-publish scope isolation)
        seen.Count.Should().Be(2);
    }

    private static IPublishExecutionPipeline _BuildPipeline(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new PublishExecutionPipeline(provider);
    }
}

internal sealed record SimplePayload(string Value);

internal sealed class PublishCallRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> Calls => _calls.ToArray();

    public void Record(string call) => _calls.Enqueue(call);
}

internal sealed class RecordingPublishFilterA(PublishCallRecorder recorder) : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        recorder.Record("A.publishing");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnPublishExecutedAsync(PublishedContext context)
    {
        recorder.Record("A.published");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnPublishExceptionAsync(PublishExceptionContext context)
    {
        recorder.Record("A.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingPublishFilterB(PublishCallRecorder recorder) : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        recorder.Record("B.publishing");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnPublishExecutedAsync(PublishedContext context)
    {
        recorder.Record("B.published");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnPublishExceptionAsync(PublishExceptionContext context)
    {
        recorder.Record("B.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class TenantStampingFilter : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        context.Options = (context.Options ?? new PublishOptions()) with { TenantId = "acme-from-filter" };
        return ValueTask.CompletedTask;
    }
}

internal sealed class DelayMultiplyingFilter : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        if (context.DelayTime.HasValue)
        {
            context.DelayTime = context.DelayTime.Value * 2;
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class HandlingPublishFilter(PublishCallRecorder recorder) : PublishFilter
{
    public override ValueTask OnPublishExceptionAsync(PublishExceptionContext context)
    {
        context.ExceptionHandled = true;
        recorder.Record($"Handling.exception(handled={context.ExceptionHandled.ToString().ToLowerInvariant()})");
        return ValueTask.CompletedTask;
    }
}

internal sealed class PublishingThrowingFilter(PublishCallRecorder recorder) : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        recorder.Record("ExecutingThrow.publishing");
        throw new InvalidOperationException("filter publishing boom");
    }

    public override ValueTask OnPublishExceptionAsync(PublishExceptionContext context)
    {
        recorder.Record("ExecutingThrow.exception");
        return ValueTask.CompletedTask;
    }
}

internal sealed class InstanceTrackingFilter : PublishFilter
{
    public override ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        // Stash this instance's hash in TenantId so the test can observe per-publish instance isolation.
        context.Options = (context.Options ?? new PublishOptions()) with
        {
            TenantId = GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return ValueTask.CompletedTask;
    }
}
