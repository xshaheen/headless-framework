// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using NATS.Client.JetStream.Models;

namespace Tests;

public sealed class NatsStreamCatalogTests : TestBase
{
    [Fact]
    public void should_default_an_owned_stream_to_limits_retention_and_keep_its_settings()
    {
        // given
        var catalog = new NatsStreamCatalog();

        // when
        catalog.Own(
            "ORDERS",
            stream => stream.Subjects("orders.>", "headless.queue.orders.>").MaxAge(TimeSpan.FromDays(1)).MaxBytes(10)
        );

        // then
        var spec = catalog.Streams.Should().ContainSingle().Subject;
        spec.Owned.Should().BeTrue();
        spec.Subjects.Should().Equal("orders.>", "headless.queue.orders.>");
        spec.Retention.Should().Be(StreamConfigRetention.Limits);
        spec.MaxAge.Should().Be(TimeSpan.FromDays(1));
        spec.MaxBytes.Should().Be(10);
    }

    [Fact]
    public void should_declare_a_bound_stream_without_settings()
    {
        // given
        var catalog = new NatsStreamCatalog();

        // when
        catalog.Bind("ORDERS", "headless.bus.orders.>");

        // then
        var spec = catalog.Streams.Should().ContainSingle().Subject;
        spec.Owned.Should().BeFalse();
        spec.Retention.Should().BeNull();
        spec.MaxAge.Should().BeNull();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("star*")]
    [InlineData("gt>")]
    [InlineData("slash/")]
    [InlineData("headless-bus-orders")]
    public void should_reject_an_invalid_or_reserved_stream_name(string name)
    {
        var act = () => new NatsStreamCatalog().Bind(name, "orders.>");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_stream_name_declared_twice()
    {
        var catalog = new NatsStreamCatalog().Bind("ORDERS", "orders.>");

        var act = () => catalog.Bind("ORDERS", "payments.>");

        act.Should().Throw<ArgumentException>().WithMessage("*already declared*");
    }

    [Fact]
    public void should_reject_a_stream_without_subjects()
    {
        var act = () => new NatsStreamCatalog().Own("ORDERS", _ => { });

        act.Should().Throw<ArgumentException>().WithMessage("*at least one subject*");
    }

    [Theory]
    [InlineData("headless.reply.>")]
    [InlineData("headless.*.x")]
    [InlineData("$JS.API.>")]
    [InlineData("_INBOX.abc")]
    [InlineData(">")]
    public void should_reject_a_subject_in_a_reserved_space(string subject)
    {
        var act = () => new NatsStreamCatalog().Bind("ORDERS", subject);

        act.Should().Throw<ArgumentException>().WithMessage("*reserved*");
    }

    [Theory]
    [InlineData("orders..placed")]
    [InlineData(".orders")]
    [InlineData(" ")]
    public void should_reject_a_malformed_subject(string subject)
    {
        var act = () => new NatsStreamCatalog().Bind("ORDERS", subject);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_subjects_that_overlap_another_declared_stream()
    {
        var catalog = new NatsStreamCatalog().Own("ORDERS", stream => stream.Subjects("orders.>"));

        var act = () => catalog.Bind("ORDERS_EU", "orders.*.eu");

        act.Should().Throw<ArgumentException>().WithMessage("*overlaps stream 'ORDERS'*");
    }

    [Fact]
    public void should_reject_a_negative_max_age_and_a_non_positive_max_bytes()
    {
        var builder = new NatsStreamBuilder();

        ((Action)(() => builder.MaxAge(TimeSpan.FromSeconds(-1)))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.MaxBytes(0))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_reject_a_non_positive_duplicate_window()
    {
        var builder = new NatsStreamBuilder();

        ((Action)(() => builder.DuplicateWindow(TimeSpan.Zero))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_reject_a_declared_duplicate_window_longer_than_the_declared_max_age()
    {
        var catalog = new NatsStreamCatalog();

        var act = () =>
            catalog.Own(
                "ORDERS",
                stream =>
                    stream
                        .Subjects("headless.queue.orders.>")
                        .MaxAge(TimeSpan.FromMinutes(1))
                        .DuplicateWindow(TimeSpan.FromMinutes(5))
            );

        act.Should().Throw<ArgumentException>().WithMessage("*duplicate window*MaxAge*");
    }

    [Theory]
    [InlineData("a.b", "a.b", true)]
    [InlineData("a.b", "a.c", false)]
    [InlineData("a.*", "a.b", true)]
    [InlineData("a.*", "a.b.c", false)]
    [InlineData("a.>", "a.b.c", true)]
    [InlineData("a.>", "a", false)]
    [InlineData("*.b", "a.*", true)]
    [InlineData(">", "x.y", true)]
    [InlineData("a.b.c", "a.b", false)]
    public void should_tell_whether_two_subject_patterns_overlap(string left, string right, bool expected)
    {
        NatsStreamReconciliation.Overlaps(left, right).Should().Be(expected);
        NatsStreamReconciliation.Overlaps(right, left).Should().Be(expected);
    }
}
