// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class ConsumeContextTests
{
    [Fact]
    public void should_store_response_value_and_static_type()
    {
        // given
        var context = _CreateContext();
        IResponseContract response = new ConcreteResponse("accepted");

        // when
        context.SetResponse(response);

        // then
        context.Response.Should().BeSameAs(response);
        context.ResponseType.Should().Be<IResponseContract>();
    }

    [Fact]
    public void should_leave_response_empty_when_not_set()
    {
        // given
        var context = _CreateContext();

        // then
        context.Response.Should().BeNull();
        context.ResponseType.Should().BeNull();
    }

    [Fact]
    public void should_keep_last_response_when_set_multiple_times()
    {
        // given
        var context = _CreateContext();
        var response = new ConcreteResponse("final");

        // when
        context.SetResponse(new ConcreteResponse("initial"));
        context.SetResponse<IResponseContract>(response);

        // then
        context.Response.Should().BeSameAs(response);
        context.ResponseType.Should().Be<IResponseContract>();
    }

    [Fact]
    public void should_store_typed_null_response()
    {
        // given
        var context = _CreateContext();

        // when
        context.SetResponse<ConcreteResponse>(null!);

        // then
        context.Response.Should().BeNull();
        context.ResponseType.Should().Be<ConcreteResponse>();
    }

    [Fact]
    public void should_throw_when_setting_response_after_completion()
    {
        // given
        var context = _CreateContext();
        context.MarkCompleted();

        // when
        var act = () => context.SetResponse(new ConcreteResponse("too-late"));

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(nameof(ConsumeContext.SetResponse))]
    [InlineData(nameof(ConsumeContext.SetResponseCallbackName))]
    [InlineData(nameof(ConsumeContext.SetResponseDestination))]
    public void should_throw_when_a_responder_sets_a_callback_response(string member)
    {
        // given — a responder answers its caller only by returning the response
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        Action act = member switch
        {
            nameof(ConsumeContext.SetResponse) => () => context.SetResponse(new ConcreteResponse("callback")),
            nameof(ConsumeContext.SetResponseCallbackName) => () => context.SetResponseCallbackName("next-hop"),
            _ => () => context.SetResponseDestination("elsewhere"),
        };

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*responder*");
        context.Response.Should().BeNull();
        context.ResponseCallbackName.Should().BeNull();
        context.ResponseDestination.Should().BeNull();
    }

    [Fact]
    public void should_keep_a_header_a_responder_sets_for_its_reply()
    {
        // given
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        context.SetResponseHeader("x-quote-source", "cache");

        // then
        context.ResponseHeaders.Should().ContainKey("x-quote-source").WhoseValue.Should().Be("cache");
    }

    [Fact]
    public void should_refuse_a_null_reply_header_value_from_a_responder()
    {
        // given — some transports drop a null header value and others deliver it, so a reply never carries one
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var act = () => context.SetResponseHeader("x-quote-source", value: null);

        // then
        act.Should().Throw<ArgumentNullException>();
        context.ResponseHeaders.Should().BeNull();
    }

    [Fact]
    public void should_keep_a_null_callback_header_value_from_a_consumer()
    {
        // given
        var context = _CreateContext();

        // when
        context.SetResponseHeader("x-quote-source", value: null);

        // then
        context.ResponseHeaders.Should().ContainKey("x-quote-source").WhoseValue.Should().BeNull();
    }

    [Theory]
    [InlineData("x price")]
    [InlineData("x:price")]
    [InlineData("x-preis-\u00e4")]
    [InlineData("x-bad\nname")]
    public void should_refuse_a_reply_header_name_a_transport_cannot_carry_from_a_responder(string key)
    {
        // given — NATS rejects a header key with a space, ':', a non-ASCII or a control character
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var act = () => context.SetResponseHeader(key, "value");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*printable ASCII*");
        context.ResponseHeaders.Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_reply_header_name_longer_than_the_limit_from_a_responder()
    {
        // given — AMQP limits a header name to 255 bytes
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var accepted = () => context.SetResponseHeader(new string('x', 255), "value");
        var refused = () => context.SetResponseHeader(new string('x', 256), "value");

        // then
        accepted.Should().NotThrow();
        refused.Should().Throw<ArgumentException>().WithMessage("*printable ASCII*");
    }

    [Theory]
    [InlineData("headless-reply-status")]
    [InlineData("Headless-Tenant-Id")]
    [InlineData("headless-anything")]
    [InlineData("traceparent")]
    [InlineData("TraceState")]
    [InlineData("baggage")]
    public void should_refuse_a_reserved_reply_header_from_a_responder(string key)
    {
        // given — the reply protocol and trace propagation own these names
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var act = () => context.SetResponseHeader(key, "forged");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*reserved*");
        context.ResponseHeaders.Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_reply_header_value_with_control_characters_from_a_responder()
    {
        // given
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var act = () => context.SetResponseHeader("x-name", "bad\r\nvalue");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*control characters*");
    }

    [Fact]
    public void should_refuse_a_reply_header_name_that_differs_only_in_case_from_a_responder()
    {
        // given — a transport that folds header case would merge the two names into one
        var context = _CreateContext();
        context.IsResponder = true;
        context.SetResponseHeader("X-Source", "cache");

        // when
        var act = () => context.SetResponseHeader("x-source", "live");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*differs only in case*");
        context
            .ResponseHeaders.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string?>("X-Source", "cache"));
    }

    [Fact]
    public void should_overwrite_a_reply_header_set_again_under_the_same_name_from_a_responder()
    {
        // given
        var context = _CreateContext();
        context.IsResponder = true;
        context.SetResponseHeader("x-source", "cache");

        // when
        context.SetResponseHeader("x-source", "live");

        // then
        context.ResponseHeaders.Should().ContainSingle().Which.Value.Should().Be("live");
    }

    [Fact]
    public void should_refuse_reply_headers_past_the_size_limit_from_a_responder()
    {
        // given — two headers that together fill the budget exactly: name bytes plus value bytes
        var context = _CreateContext();
        context.IsResponder = true;
        const int half = Headers.MaxReplyHeadersBytes / 2;
        context.SetResponseHeader("x-a", new string('a', half - 3));
        context.SetResponseHeader("x-b", new string('b', half - 3));

        // when
        var act = () => context.SetResponseHeader("x-c", "c");

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*byte limit*");
        context.ResponseHeaders.Should().NotContainKey("x-c");
    }

    [Fact]
    public void should_count_reply_header_values_in_utf8_bytes_from_a_responder()
    {
        // given — 'é' is one character but two UTF-8 bytes, so this value is twice the budget in bytes
        var context = _CreateContext();
        context.IsResponder = true;

        // when
        var act = () => context.SetResponseHeader("x-name", new string('é', Headers.MaxReplyHeadersBytes / 2));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*byte limit*");
    }

    [Fact]
    public void should_count_an_overwritten_reply_header_once_toward_the_size_limit()
    {
        // given — one header that takes nearly the whole budget
        var context = _CreateContext();
        context.IsResponder = true;
        var large = new string('a', Headers.MaxReplyHeadersBytes - 10);
        context.SetResponseHeader("x-a", large);

        // when — replacing it with a value of the same size frees its old bytes first
        var act = () => context.SetResponseHeader("x-a", large);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_leave_a_consumer_callback_header_to_the_publish_checks()
    {
        // given — a plain consumer's callback headers are checked when the callback is published
        var context = _CreateContext();

        // when
        context.SetResponseHeader("headless-anything", "value");

        // then
        context.ResponseHeaders.Should().ContainKey("headless-anything");
    }

    [Fact]
    public void should_record_a_reply_under_its_declared_type_apart_from_the_callback_response()
    {
        // given
        var context = _CreateContext();
        var reply = new ConcreteResponse("quoted");

        // when
        context.RecordReply<IResponseContract>(reply);

        // then
        context.Reply.Should().BeSameAs(reply);
        context.ReplyType.Should().Be<IResponseContract>();
        context.Response.Should().BeNull();
        context.ResponseType.Should().BeNull();
    }

    [Fact]
    public void should_record_a_null_reply_as_a_reply()
    {
        // given
        var context = _CreateContext();

        // when
        context.RecordReply<ConcreteResponse>(null);

        // then
        context.Reply.Should().BeNull();
        context.ReplyType.Should().Be<ConcreteResponse>();
    }

    [Fact]
    public void should_leave_the_reply_empty_when_not_recorded()
    {
        // given
        var context = _CreateContext();

        // then
        context.Reply.Should().BeNull();
        context.ReplyType.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_recording_a_reply_after_completion()
    {
        // given
        var context = _CreateContext();
        context.MarkCompleted();

        // when
        var act = () => context.RecordReply(new ConcreteResponse("too-late"));

        // then
        act.Should().Throw<InvalidOperationException>();
        context.Reply.Should().BeNull();
        context.ReplyType.Should().BeNull();
    }

    [Fact]
    public void should_store_next_callback_name()
    {
        // given
        var context = _CreateContext();

        // when
        context.SetResponseCallbackName("chain-final");

        // then
        context.ResponseCallbackName.Should().Be("chain-final");
    }

    [Fact]
    public void should_leave_next_callback_name_empty_when_not_set()
    {
        // given
        var context = _CreateContext();

        // then
        context.ResponseCallbackName.Should().BeNull();
    }

    [Fact]
    public void should_keep_last_next_callback_name_when_set_multiple_times()
    {
        // given
        var context = _CreateContext();

        // when
        context.SetResponseCallbackName("chain-first");
        context.SetResponseCallbackName("chain-final");

        // then
        context.ResponseCallbackName.Should().Be("chain-final");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_next_callback_name_is_null_empty_or_whitespace(string? callbackName)
    {
        // given
        var context = _CreateContext();

        // when
        var act = () => context.SetResponseCallbackName(callbackName!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_setting_next_callback_after_completion()
    {
        // given
        var context = _CreateContext();
        context.MarkCompleted();

        // when
        var act = () => context.SetResponseCallbackName("too-late");

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    private static ConsumeContext _CreateContext()
    {
        return new()
        {
            Message = new object(),
            MessageId = "message-1",
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UnixEpoch,
            MessageName = "test.message",
            Lane = MessageLane.Bus,
        };
    }

    private interface IResponseContract;

    private sealed record ConcreteResponse(string Status) : IResponseContract;
}
