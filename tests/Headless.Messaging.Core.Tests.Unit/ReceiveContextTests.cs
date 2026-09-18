// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class ReceiveContextTests
{
    [Fact]
    public void should_expose_identity_and_consumer_metadata_from_construction()
    {
        // given
        var received = _ReceivedHeaders();

        // when
        var context = _CreateContext(received);

        // then
        context.MessageId.Should().Be("message-1");
        context.MessageName.Should().Be("order.placed");
        context.GroupName.Should().Be("checkout");
        context.Lane.Should().Be(MessageLane.Bus);
        context.MessageType.Should().Be<OrderPlaced>();
        context.ConsumerContractVersion.Should().Be("1.3");
        context.Body.ToArray().Should().Equal(_Body);
    }

    [Fact]
    public void should_throw_when_constructed_with_whitespace_identity()
    {
        // given / when
        var act = () => _CreateContext(messageId: " ");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_not_mutate_the_received_headers_dictionary_when_setting_a_header()
    {
        // given
        var received = _ReceivedHeaders();
        var context = _CreateContext(received);

        // when
        context.SetHeader("x-signature-valid", "true");

        // then
        received.Should().NotContainKey("x-signature-valid");
        received["x-custom"].Should().Be("kept");
        context.Headers["x-signature-valid"].Should().Be("true");
    }

    [Fact]
    public void should_not_mutate_the_received_headers_dictionary_when_removing_a_header()
    {
        // given
        var received = _ReceivedHeaders();
        var context = _CreateContext(received);

        // when
        context.RemoveHeader("x-custom");

        // then
        received.Should().ContainKey("x-custom");
        context.Headers.Should().NotContainKey("x-custom");
    }

    [Fact]
    public void should_show_prior_writes_to_later_readers()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        context.SetHeader("x-stage", "first");
        context.SetHeader("x-stage", "second");
        context.RemoveHeader("x-removed");
        context.ReplaceBody("replacement"u8.ToArray());

        // then
        context.Headers["x-stage"].Should().Be("second");
        context.Headers.Should().NotContainKey("x-removed");
        context.Body.ToArray().Should().Equal("replacement"u8.ToArray());
    }

    [Fact]
    public void should_keep_header_lookups_ordinal_through_copy_on_write()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        context.SetHeader("X-Case", "upper");

        // then
        context.Headers["X-Case"].Should().Be("upper");
        context.SetHeader("x-case", "lower");
        context.Headers["X-Case"].Should().Be("upper");
        context.Headers["x-case"].Should().Be("lower");
        context.RemoveHeader("X-Case");
        context.Headers.Should().NotContainKey("X-Case");
        context.Headers["x-case"].Should().Be("lower");
    }

    [Theory]
    [InlineData(Headers.MessageId)]
    [InlineData(Headers.MessageName)]
    [InlineData(Headers.Group)]
    [InlineData(Headers.Exception)]
    public void should_throw_when_writing_an_identity_header_before_completion(string key)
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        var set = () => context.SetHeader(key, "forged");
        var remove = () => context.RemoveHeader(key);

        // then
        set.Should().Throw<InvalidOperationException>();
        remove.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(Headers.MessageId)]
    [InlineData(Headers.MessageName)]
    [InlineData(Headers.Group)]
    [InlineData(Headers.Exception)]
    public void should_throw_when_writing_an_identity_header_after_completion(string key)
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        context.MarkCompleted();

        // when
        var set = () => context.SetHeader(key, "forged");
        var remove = () => context.RemoveHeader(key);

        // then
        set.Should().Throw<InvalidOperationException>();
        remove.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_setting_null_or_whitespace_header_key()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        var setNull = () => context.SetHeader(null!, "value");
        var setBlank = () => context.SetHeader("  ", "value");
        var removeBlank = () => context.RemoveHeader("");

        // then
        setNull.Should().Throw<ArgumentException>();
        setBlank.Should().Throw<ArgumentException>();
        removeBlank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_record_skip_reason()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        context.Skip("tenant-filtered");

        // then
        context.Outcome.Should().Be(ReceiveOutcome.Skip);
        context.OutcomeReason.Should().Be("tenant-filtered");
        context.RejectCause.Should().BeNull();
    }

    [Fact]
    public void should_record_reject_reason_and_cause()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        var cause = new InvalidOperationException("bad signature");

        // when
        context.Reject("signature-mismatch", cause);

        // then
        context.Outcome.Should().Be(ReceiveOutcome.Reject);
        context.OutcomeReason.Should().Be("signature-mismatch");
        context.RejectCause.Should().BeSameAs(cause);
    }

    [Fact]
    public void should_allow_reject_without_a_cause()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        context.Reject("poison");

        // then
        context.Outcome.Should().Be(ReceiveOutcome.Reject);
        context.RejectCause.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_rejecting_after_a_skip()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        context.Skip("filtered");

        // when
        var act = () => context.Reject("changed-mind");

        // then
        act.Should().Throw<InvalidOperationException>();
        context.Outcome.Should().Be(ReceiveOutcome.Skip);
    }

    [Fact]
    public void should_throw_when_skipping_after_a_reject()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        context.Reject("poison");

        // when
        var act = () => context.Skip("changed-mind");

        // then
        act.Should().Throw<InvalidOperationException>();
        context.Outcome.Should().Be(ReceiveOutcome.Reject);
    }

    [Fact]
    public void should_throw_when_declaring_an_outcome_with_a_blank_reason()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());

        // when
        var skip = () => context.Skip(" ");
        var reject = () => context.Reject(null!);

        // then
        skip.Should().Throw<ArgumentException>();
        reject.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_replace_the_cancellation_token_before_completion()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        using var replacement = new CancellationTokenSource();

        // when
        context.SetCancellationToken(replacement.Token);

        // then
        context.CancellationToken.Should().Be(replacement.Token);
    }

    [Fact]
    public void should_throw_when_every_mutator_runs_after_completion()
    {
        // given
        var context = _CreateContext(_ReceivedHeaders());
        using var source = new CancellationTokenSource();
        context.MarkCompleted();

        // when
        var setHeader = () => context.SetHeader("x-late", "value");
        var removeHeader = () => context.RemoveHeader("x-custom");
        var replaceBody = () => context.ReplaceBody("late"u8.ToArray());
        var skip = () => context.Skip("late");
        var reject = () => context.Reject("late");
        var setToken = () => context.SetCancellationToken(source.Token);

        // then
        setHeader.Should().Throw<InvalidOperationException>();
        removeHeader.Should().Throw<InvalidOperationException>();
        replaceBody.Should().Throw<InvalidOperationException>();
        skip.Should().Throw<InvalidOperationException>();
        reject.Should().Throw<InvalidOperationException>();
        setToken.Should().Throw<InvalidOperationException>();
    }

    private static readonly byte[] _Body = """{"orderId":"order-1"}"""u8.ToArray();

    private static Dictionary<string, string?> _ReceivedHeaders()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "order.placed",
            [Headers.Group] = "checkout",
            ["x-custom"] = "kept",
            ["x-removed"] = "gone",
        };
    }

    private static ReceiveContext _CreateContext(IDictionary<string, string?>? headers = null, string? messageId = null)
    {
        return new ReceiveContext(
            messageId ?? "message-1",
            "order.placed",
            "checkout",
            MessageLane.Bus,
            typeof(OrderPlaced),
            consumerContractVersion: "1.3",
            headers ?? _ReceivedHeaders(),
            _Body,
            CancellationToken.None
        );
    }

    private sealed record OrderPlaced(string OrderId);
}
