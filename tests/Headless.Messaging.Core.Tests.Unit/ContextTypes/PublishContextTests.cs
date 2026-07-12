// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests.ContextTypes;

public sealed class PublishContextTests : TestBase
{
    [Fact]
    public void should_allow_options_and_delay_mutation_before_completion()
    {
        // given
        var context = new PublishContext<OrderPlaced>(
            new OrderPlaced("order-1"),
            IntentType.Bus,
            new PublishOptions { CorrelationId = "corr-1" },
            TimeSpan.FromSeconds(1)
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
            IntentType.Bus,
            new PublishOptions { TenantId = "tenant-1" },
            TimeSpan.FromSeconds(1)
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
            IntentType.Bus,
            options: null,
            delayTime: null,
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
            IntentType.Bus,
            options,
            delayTime: null,
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
            IntentType.Bus,
            options,
            delayTime: null
        );
        headers["x-feature"] = "disabled";
        headers["x-new"] = "new";

        // then
        context.Headers.Should().ContainKey("x-feature");
        context.Headers["x-feature"].Should().Be("enabled");
        context.Headers.Should().NotContainKey("x-new");
    }

    private sealed record OrderPlaced(string OrderId);
}
