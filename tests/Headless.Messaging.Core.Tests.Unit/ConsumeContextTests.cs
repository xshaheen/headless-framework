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
    public void should_record_a_reply_under_its_declared_type_apart_from_the_callback_response()
    {
        // given
        var context = _CreateContext();
        var reply = new ConcreteResponse("quoted");

        // when
        context.RecordReply<IResponseContract>(reply);

        // then
        context.HasReply.Should().BeTrue();
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
        context.HasReply.Should().BeTrue();
        context.Reply.Should().BeNull();
        context.ReplyType.Should().Be<ConcreteResponse>();
    }

    [Fact]
    public void should_leave_the_reply_empty_when_not_recorded()
    {
        // given
        var context = _CreateContext();

        // then
        context.HasReply.Should().BeFalse();
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
        context.HasReply.Should().BeFalse();
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
