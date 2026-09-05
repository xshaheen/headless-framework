// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class QueueOptionsBuilderTests : TestBase
{
    [Fact]
    public void should_preserve_canonical_defaults_and_distinguish_empty_headers()
    {
        var builder = new QueueOptionsBuilder();

        builder.Build().Should().Be(new QueueOptions());
        builder.Build().DeliveryMode.Should().Be(DeliveryMode.Durable);
        builder.Build().Headers.Should().BeNull();
        builder.WithHeaders([]).Build().Headers.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void should_merge_ordinal_headers_and_isolate_inputs_builder_and_each_snapshot()
    {
        var input = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "checkout" };
        var builder = new QueueOptionsBuilder().WithHeaders(input);
        input["source"] = "mutated-input";
        builder.WithHeader("Source", "different-case").WithHeader("nullable", null);
        var first = builder.Build();
        var second = builder.Build();

        builder.WithHeaders([new("source", "updated"), new("source", "last")]);
        first.Headers!["source"] = "mutated-snapshot";

        second
            .Headers.Should()
            .BeEquivalentTo(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["source"] = "checkout",
                    ["Source"] = "different-case",
                    ["nullable"] = null,
                }
            );
        var next = builder.Build();
        next.Headers!["source"].Should().Be("last");
        next.Headers["Source"].Should().Be("different-case");
        next.Headers["nullable"].Should().BeNull();
        next.Headers.Should().NotBeSameAs(second.Headers);
    }

    [Fact]
    public void should_enumerate_headers_at_the_mutator_boundary()
    {
        var enumerations = 0;
        IEnumerable<KeyValuePair<string, string?>> headers()
        {
            ++enumerations;
            yield return new("source", "checkout");
        }

        var builder = new QueueOptionsBuilder().WithHeaders(headers());
        enumerations.Should().Be(1);
        builder.Build();
        builder.Build();
        enumerations.Should().Be(1);
    }

    [Fact]
    public void should_preserve_metadata_and_clear_nullable_overrides_on_reuse()
    {
        var builder = new QueueOptionsBuilder();
        var configured = builder
            .WithCorrelationId("corr")
            .WithCausationId("cause")
            .WithMessageId("message")
            .WithTenantId("tenant")
            .WithDelay(TimeSpan.FromMinutes(2));
        configured.Should().BeSameAs(builder);
        var first = builder.Build();
        first
            .Should()
            .Be(
                new QueueOptions
                {
                    CorrelationId = "corr",
                    CausationId = "cause",
                    MessageId = "message",
                    TenantId = "tenant",
                    Delay = TimeSpan.FromMinutes(2),
                }
            );

        builder.WithCorrelationId(null).WithCausationId(null).WithMessageId(null).WithTenantId(null).WithDelay(null);

        builder.Build().Should().Be(new QueueOptions());
        first.TenantId.Should().Be("tenant");
        first.Delay.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void should_guard_null_inputs_without_taking_over_publisher_validation()
    {
        var builder = new QueueOptionsBuilder();
        Action nullHeaders = () => builder.WithHeaders(null!);
        Action nullName = () => builder.WithHeader(null!, "value");
        Action nullBulkName = () => builder.WithHeaders([new(null!, "value")]);
        nullHeaders.Should().Throw<ArgumentNullException>().WithParameterName("headers");
        nullName.Should().Throw<ArgumentNullException>().WithParameterName("name");
        nullBulkName.Should().Throw<ArgumentNullException>().WithParameterName("name");

        var snapshot = builder
            .WithHeader(Headers.MessageName, "reserved")
            .WithHeader("bad\r\nname", "bad\nvalue")
            .WithTenantId(" ")
            .WithDelay(TimeSpan.Zero)
            .Build();
        snapshot.Delay.Should().Be(TimeSpan.Zero);
        snapshot.TenantId.Should().Be(" ");
        snapshot.Headers![Headers.MessageName].Should().Be("reserved");
    }
}
