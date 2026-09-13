// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.ContextTypes;

public sealed class PublishContextTests : TestBase
{
    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Queue, true)]
    public void should_resolve_absolute_schedule_during_public_construction(MessageLane lane, bool isTransactional)
    {
        var scheduledAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(3)).AddTicks(17);
        MessageOptions options =
            lane == MessageLane.Bus
                ? new PublishOptions { ScheduledAt = scheduledAt }
                : new QueueOptions { ScheduledAt = scheduledAt };

        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            lane,
            options,
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            isTransactional: isTransactional,
            cancellationToken: AbortToken
        );

        context.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        context.ScheduledAt.Should().Be(scheduledAt.ToUniversalTime());
        context.PublishAt.Should().Be(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero).AddTicks(10));
        context.DelayTime.Should().BeNull();
        context.IsTransactional.Should().Be(isTransactional);
    }

    [Fact]
    public void should_reject_absolute_schedule_for_direct_delivery_during_public_construction()
    {
        var act = () =>
            new PublishContext<OrderPlaced>(
                new OrderPlaced("order-1"),
                MessageLane.Bus,
                new PublishOptions { ScheduledAt = DateTimeOffset.UnixEpoch.AddHours(1) },
                defaultDeliveryMode: DeliveryMode.Direct,
                now: DateTimeOffset.UnixEpoch,
                cancellationToken: AbortToken
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Direct*schedule*");
    }

    [Fact]
    public void should_reject_conflicting_schedules_during_public_construction()
    {
        var act = () =>
            new PublishContext<OrderPlaced>(
                new OrderPlaced("order-1"),
                MessageLane.Queue,
                new QueueOptions
                {
                    Delay = TimeSpan.FromMinutes(1),
                    ScheduledAt = DateTimeOffset.UnixEpoch.AddHours(1),
                },
                defaultDeliveryMode: DeliveryMode.Auto,
                now: DateTimeOffset.UnixEpoch,
                cancellationToken: AbortToken
            );

        act.Should().Throw<ArgumentException>().WithMessage("*Delay*ScheduledAt*");
    }

    [Fact]
    public void should_allow_options_and_delay_mutation_before_completion()
    {
        // given
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            new PublishOptions { CorrelationId = "corr-1", Delay = TimeSpan.FromSeconds(1) },
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            cancellationToken: AbortToken
        );

        // when
        context.Options = context.Options! with
        {
            TenantId = "tenant-1",
        };
        context.DelayTime = TimeSpan.FromSeconds(5);

        // then
        context.Options!.TenantId.Should().Be("tenant-1");
        context.Options.CorrelationId.Should().Be("corr-1");
        context.DelayTime.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void should_reject_options_and_delay_mutation_after_completion()
    {
        // given
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            new PublishOptions { TenantId = "tenant-1", Delay = TimeSpan.FromSeconds(1) },
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            cancellationToken: AbortToken
        );

        // when
        context.MarkCompleted();
        var optionsAct = () => context.Options = new PublishOptions { TenantId = "tenant-2" };
        var delayAct = () => context.DelayTime = TimeSpan.FromSeconds(10);

        // then
        optionsAct.Should().Throw<InvalidOperationException>().WithMessage("*read-only after next()*");
        delayAct.Should().Throw<InvalidOperationException>().WithMessage("*read-only after next()*");
        context.Options!.TenantId.Should().Be("tenant-1");
        context.DelayTime.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_update_cancellation_token_for_subsequent_reads_before_completion()
    {
        // given
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            options: null,
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            cancellationToken: first.Token
        );

        // when
        context.SetCancellationToken(second.Token);
        var observedBeforeCompletion = context.CancellationToken;
        context.MarkCompleted();
        var act = () => context.SetCancellationToken(first.Token);

        // then
        observedBeforeCompletion.Should().Be(second.Token);
        act.Should().Throw<InvalidOperationException>().WithMessage("*read-only after next()*");
        context.CancellationToken.Should().Be(second.Token);
    }

    [Fact]
    public void should_expose_base_and_typed_publish_fields()
    {
        // given
        using var cts = new CancellationTokenSource();
        var options = new PublishOptions
        {
            MessageName = "orders",
            TenantId = "tenant-1",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["x-feature"] = "enabled" },
        };

        // when
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            options,
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            isTransactional: true,
            cancellationToken: cts.Token
        );
        PublishContext baseContext = context;

        // then
        context.Content!.OrderId.Should().Be("order-1");
        context.IsTransactional.Should().BeTrue();
        baseContext.Content.Should().BeSameAs(context.Content);
        baseContext.MessageType.Should().Be<OrderPlaced>();
        baseContext.CancellationToken.Should().Be(cts.Token);
        baseContext.Headers["x-feature"].Should().Be("enabled");
        baseContext.MessageName.Should().Be("orders");
    }

    [Fact]
    public void should_snapshot_headers_from_publish_options()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["x-feature"] = "enabled" };
        var options = new PublishOptions { Headers = headers };

        // when
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            options,
            defaultDeliveryMode: DeliveryMode.Auto,
            now: DateTimeOffset.UnixEpoch,
            cancellationToken: AbortToken
        );
        headers["x-feature"] = "disabled";
        headers["x-new"] = "new";

        // then
        context.Headers.Should().ContainKey("x-feature");
        context.Headers["x-feature"].Should().Be("enabled");
        context.Headers.Should().NotContainKey("x-new");
    }

    [Fact]
    public void should_reject_delivery_mode_change_after_resolution()
    {
        // given
        var options = new PublishOptions { DeliveryMode = DeliveryMode.Auto };
        var context = _CreateFrozenContext(options);

        // when
        var act = () => context.WithOptions(options with { DeliveryMode = DeliveryMode.Durable });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot change*delivery mode*");
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(5, null)]
    [InlineData(5, 10)]
    public void should_reject_delay_change_through_same_mode_options_after_resolution(
        int? initialDelaySeconds,
        int? replacementDelaySeconds
    )
    {
        // given
        var options = new PublishOptions
        {
            DeliveryMode = DeliveryMode.Auto,
            Delay = initialDelaySeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };
        var context = _CreateFrozenContext(options);
        var replacementDelay = replacementDelaySeconds is { } replacementSeconds
            ? TimeSpan.FromSeconds(replacementSeconds)
            : (TimeSpan?)null;

        // when
        var act = () => context.WithOptions(options with { Delay = replacementDelay });

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot change*delivery delay*");
    }

    [Fact]
    public void should_allow_non_delivery_options_to_change_after_resolution()
    {
        // given
        var delay = TimeSpan.FromMinutes(2);
        var options = new PublishOptions { DeliveryMode = DeliveryMode.Auto, Delay = delay };
        var context = _CreateFrozenContext(options);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["x-test"] = "value" };

        // when
        context.WithOptions(options with { MessageName = "orders", Headers = headers });

        // then
        context.MessageName.Should().Be("orders");
        context.Headers["x-test"].Should().Be("value");
        context.DelayTime.Should().Be(delay);
    }

    [Theory]
    [InlineData(DeliveryMode.Auto, DeliveryMode.Direct, true)]
    [InlineData(DeliveryMode.Auto, DeliveryMode.Direct, false)]
    [InlineData(DeliveryMode.Durable, DeliveryMode.Durable, true)]
    [InlineData(DeliveryMode.Durable, DeliveryMode.Durable, false)]
    [InlineData(DeliveryMode.Direct, DeliveryMode.Direct, true)]
    [InlineData(DeliveryMode.Direct, DeliveryMode.Direct, false)]
    public void should_inherit_explicit_host_default_when_constructed_without_delivery_override(
        DeliveryMode hostDefault,
        DeliveryMode expectedMode,
        bool nullOptions
    )
    {
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            nullOptions ? null : new PublishOptions(),
            hostDefault,
            DateTimeOffset.UnixEpoch,
            cancellationToken: AbortToken
        );

        context.RequestedDeliveryMode.Should().Be(hostDefault);
        context.ResolvedDeliveryMode.Should().Be(expectedMode);
        context.IsTransactional.Should().BeFalse();
        context.DelayTime.Should().BeNull();
        context.PublishAt.Should().BeNull();
    }

    [Theory]
    [InlineData(DeliveryMode.Auto, false, DeliveryMode.Direct, false)]
    [InlineData(DeliveryMode.Auto, true, DeliveryMode.Durable, true)]
    [InlineData(DeliveryMode.Durable, false, DeliveryMode.Durable, false)]
    [InlineData(DeliveryMode.Durable, true, DeliveryMode.Durable, true)]
    [InlineData(DeliveryMode.Direct, false, DeliveryMode.Direct, false)]
    [InlineData(DeliveryMode.Direct, true, DeliveryMode.Direct, false)]
    public void should_resolve_explicit_mode_against_compatible_coordination(
        DeliveryMode requestedMode,
        bool isTransactional,
        DeliveryMode expectedMode,
        bool expectedTransactional
    )
    {
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Queue,
            new QueueOptions { DeliveryMode = requestedMode },
            defaultDeliveryMode: DeliveryMode.Durable,
            now: DateTimeOffset.UnixEpoch,
            isTransactional: isTransactional,
            cancellationToken: AbortToken
        );

        context.RequestedDeliveryMode.Should().Be(requestedMode);
        context.ResolvedDeliveryMode.Should().Be(expectedMode);
        context.IsTransactional.Should().Be(expectedTransactional);
    }

    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Queue, true)]
    public void should_calculate_utc_publish_at_from_option_delay(MessageLane lane, bool isTransactional)
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(3));
        var delay = TimeSpan.FromMinutes(5);
        MessageOptions options =
            lane == MessageLane.Bus ? new PublishOptions { Delay = delay } : new QueueOptions { Delay = delay };
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            lane,
            options,
            defaultDeliveryMode: DeliveryMode.Auto,
            now: now,
            isTransactional: isTransactional,
            cancellationToken: AbortToken
        );

        context.RequestedDeliveryMode.Should().Be(DeliveryMode.Auto);
        context.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        context.DelayTime.Should().Be(delay);
        context.PublishAt.Should().Be(new DateTimeOffset(2026, 9, 10, 9, 5, 0, TimeSpan.Zero));
        context.PublishAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        context.IsTransactional.Should().Be(isTransactional);
    }

    [Theory]
    [InlineData(MessageLane.Bus, null)]
    [InlineData(MessageLane.Bus, DeliveryMode.Direct)]
    [InlineData(MessageLane.Queue, null)]
    [InlineData(MessageLane.Queue, DeliveryMode.Direct)]
    public void should_reject_delayed_direct_delivery_from_host_or_option(MessageLane lane, DeliveryMode? mode)
    {
        MessageOptions options =
            lane == MessageLane.Bus
                ? new PublishOptions { Delay = TimeSpan.FromSeconds(1), DeliveryMode = mode }
                : new QueueOptions { Delay = TimeSpan.FromSeconds(1), DeliveryMode = mode };

        var act = () =>
            new PublishContext<OrderPlaced>(
                new OrderPlaced("order-1"),
                lane,
                options,
                defaultDeliveryMode: DeliveryMode.Direct,
                now: DateTimeOffset.UnixEpoch,
                isTransactional: true,
                cancellationToken: AbortToken
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Direct*delay*");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void should_reject_invalid_delay_during_public_construction(long delayTicks)
    {
        var act = () =>
            new PublishContext<OrderPlaced>(
                new OrderPlaced("order-1"),
                MessageLane.Bus,
                new PublishOptions { Delay = TimeSpan.FromTicks(delayTicks) },
                defaultDeliveryMode: DeliveryMode.Auto,
                now: DateTimeOffset.UnixEpoch,
                cancellationToken: AbortToken
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delay");
    }

    [Theory]
    [InlineData((MessageLane)99, DeliveryMode.Auto, null, "lane")]
    [InlineData(MessageLane.Bus, (DeliveryMode)99, null, "requestedMode")]
    [InlineData(MessageLane.Bus, DeliveryMode.Auto, (DeliveryMode)99, "requestedMode")]
    public void should_reject_invalid_lane_or_effective_mode_during_public_construction(
        MessageLane lane,
        DeliveryMode hostDefault,
        DeliveryMode? requestedMode,
        string parameter
    )
    {
        var act = () =>
            new PublishContext<OrderPlaced>(
                new OrderPlaced("order-1"),
                lane,
                new PublishOptions { DeliveryMode = requestedMode },
                hostDefault,
                DateTimeOffset.UnixEpoch,
                cancellationToken: AbortToken
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    private static PublishContext<OrderPlaced> _CreateFrozenContext(PublishOptions options)
    {
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            options.DeliveryMode ?? DeliveryMode.Auto,
            options.Delay,
            DeliveryCoordination.None,
            DateTimeOffset.UnixEpoch
        );

        return new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            MessageLane.Bus,
            options,
            decision,
            AbortToken
        );
    }

    private sealed record OrderPlaced(string OrderId);
}
