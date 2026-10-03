// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests.RequestReply;

/// <summary>
/// A fault reply carries what the caller may learn about the failure, bounded so a responder's exception never floods
/// the caller's logs, and a reply that cannot be built costs the caller a timeout, never the consumed message.
/// </summary>
public sealed class ResponderRepliesTests : TestBase
{
    [Fact]
    public async Task should_truncate_a_long_failure_detail_to_the_protocol_limit()
    {
        // given — the host opts in to exception details, and the failure message is far longer than the limit
        await using var host = ResponderExecutorHost.Create(configure: options =>
            options.RequestReply.IncludeExceptionDetailsInFaults = true
        );
        var request = host.Request(TimeSpan.FromSeconds(30)).Origin;
        var failure = new InvalidOperationException(new string('x', ReplyProtocol.MaxFaultDetailLength * 2));

        // when
        await _Replies(host).SendFaultAsync(request, RequestFaultCodes.HandlerFailed, failure);

        // then
        var fault = _SingleFault(host);
        fault.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        fault.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        fault.Detail.Should().HaveLength(ReplyProtocol.MaxFaultDetailLength).And.EndWith("...");
    }

    [Fact]
    public async Task should_report_the_handler_exception_inside_the_executor_wrapper()
    {
        // given — the executor wraps the handler's exception before the fault is built
        await using var host = ResponderExecutorHost.Create(configure: options =>
            options.RequestReply.IncludeExceptionDetailsInFaults = true
        );
        var request = host.Request(TimeSpan.FromSeconds(30)).Origin;
        var wrapped = new SubscriberExecutionFailedException("wrapper", new TimeoutException("db slow"));

        // when
        await _Replies(host).SendFaultAsync(request, RequestFaultCodes.HandlerFailed, wrapped);

        // then — the caller sees the handler's own failure, not the wrapper
        var fault = _SingleFault(host);
        fault.ExceptionType.Should().Be(typeof(TimeoutException).FullName);
        fault.Detail.Should().Be("db slow");
    }

    [Fact]
    public async Task should_keep_the_detail_out_of_the_fault_unless_the_host_opts_in()
    {
        // given — the default host
        await using var host = ResponderExecutorHost.Create();
        var request = host.Request(TimeSpan.FromSeconds(30)).Origin;

        // when
        await _Replies(host).SendFaultAsync(request, RequestFaultCodes.HandlerFailed, new TimeoutException("db slow"));

        // then
        var fault = _SingleFault(host);
        fault.ExceptionType.Should().BeNull();
        fault.Detail.Should().BeNull();
    }

    [Fact]
    public async Task should_log_and_send_nothing_when_the_response_cannot_be_serialized()
    {
        // given — a serializer that fails on the response body
        var log = new List<(LogLevel Level, EventId EventId, string Message)>();
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns<TransportMessage>(_ => throw new InvalidOperationException("cannot serialize"));
        await using var host = ResponderExecutorHost.Create(configureServices: services =>
        {
            services.AddSingleton(serializer);
            services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider(log)));
        });
        var request = host.Request(TimeSpan.FromSeconds(30)).Origin;

        // when
        var act = () =>
            _Replies(host).SendResponseAsync(request, new PriceQuote(1), typeof(PriceQuote), default).AsTask();

        // then — the consumed message is not at risk: nothing throws, nothing is sent, the failure is logged
        await act.Should().NotThrowAsync();
        host.Replies.Sent.Should().BeEmpty();
        log.Should().Contain(entry => entry.EventId.Id == 117 && entry.Level == LogLevel.Warning);
    }

    private static ResponderReplies _Replies(ResponderExecutorHost host) =>
        host.Provider.GetRequiredService<ResponderReplies>();

    private static ReplyFault _SingleFault(ResponderExecutorHost host)
    {
        var (address, reply) = host.Replies.Sent.Should().ContainSingle().Subject;
        address.Should().Be(ResponderExecutorHost.ReplyAddress);
        reply.Headers[Headers.ReplyStatus].Should().Be(ReplyProtocol.StatusFault);
        var fault = ReplyProtocol.ReadFault(reply.Body);
        fault.Should().NotBeNull();
        return fault!;
    }
}
