// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.RequestReply;
using Headless.MultiTenancy;
using Headless.Reliability;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.RequestReply;

/// <summary>
/// A responder answers each request once, after the request's outcome is durable, and only from the attempt whose state
/// write took effect. Ordinary messages, and requests that reach a responder by a plain enqueue, send nothing.
/// </summary>
public sealed class SubscribeExecutorReplyTests : TestBase
{
    [Fact]
    public async Task should_send_one_ok_reply_after_the_success_write_takes_effect()
    {
        // given — a request under a tenant, with no ambient tenant on the responder
        await using var host = ResponderExecutorHost.Create();
        var repliesAtSuccessWrite = -1;
        host.OnStateWrite(StatusName.Succeeded, () => repliesAtSuccessWrite = host.Replies.Sent.Count);
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(42))));
        var message = host.Request(TimeSpan.FromSeconds(30), tenantId: "tenant-a", correlationId: "chain-1");

        // when
        var result = await host.ExecuteAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        repliesAtSuccessWrite.Should().Be(0, "the reply leaves only after the success write");
        var (address, reply) = host.Replies.Sent.Should().ContainSingle().Subject;
        address.Should().Be(ResponderExecutorHost.ReplyAddress);

        reply.Headers[Headers.InReplyTo].Should().Be(message.Origin.Headers[Headers.RequestId]);
        reply.Headers[Headers.ReplyStatus].Should().Be(ReplyProtocol.StatusOk);
        reply.Headers[Headers.MessageName].Should().Be(PriceQuoteContract.Name);
        reply.Headers[Headers.ContractVersion].Should().Be(PriceQuoteContract.Version);
        reply.Headers[Headers.TenantId].Should().Be("tenant-a");
        reply.Headers[Headers.CorrelationId].Should().Be("chain-1");
        reply.Headers[Headers.CausationId].Should().Be(message.Origin.Id);
        reply.Headers[Headers.MessageId].Should().NotBeNullOrWhiteSpace().And.NotBe(message.Origin.Id);
        JsonSerializer
            .Deserialize<PriceQuote>(reply.Body.Span, JsonSerializerOptions.Web)
            .Should()
            .Be(new PriceQuote(42));
    }

    [Fact]
    public async Task should_not_reply_when_the_success_write_finds_the_row_already_terminal()
    {
        // given — a stale attempt: another attempt already finished the row
        await using var host = ResponderExecutorHost.Create(stateWriteTakesEffect: false);
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(42))));

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then
        host.Replies.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InboxCommit.Commit, 1)]
    [InlineData(InboxCommit.Rollback, 0)]
    [InlineData(InboxCommit.Indeterminate, 0)]
    public async Task should_reply_on_the_transactional_tier_only_after_the_commit_is_durable(
        InboxCommit commit,
        int expectedReplies
    )
    {
        // given
        RecordingReplyTransport? replies = null;
        FakeInboxTransactionRunner? runner = null;
        await using var host = ResponderExecutorHost.Create(
            options => options.RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional,
            services =>
                services.AddScoped<IInboxTransactionRunner>(sp =>
                    runner = new FakeInboxTransactionRunner(
                        sp.GetRequiredService<IUnitOfWorkFactory>(),
                        commit,
                        replies!
                    )
                )
        );
        replies = host.Replies;
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(42))));
        var message = host.Request(TimeSpan.FromSeconds(30));
        message.InboxKey = new InboxKey(
            TenantId: null,
            message.Origin.Id,
            MessageLane.Queue,
            ResponderExecutorHost.MessageName,
            "1",
            QuoteResponder.Identity,
            Generation: 0
        );

        // when
        await host.ExecuteAsync(message, AbortToken);

        // then
        runner.Should().NotBeNull();
        runner!.RepliesBeforeCommit.Should().Be(0, "nothing may leave before the commit");
        host.Replies.Sent.Should().HaveCount(expectedReplies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_send_one_handler_failed_fault_when_the_terminal_write_takes_effect(bool includeDetails)
    {
        // given — the default classifier treats NotSupportedException as permanent
        await using var host = ResponderExecutorHost.Create(options =>
            options.RequestReply.IncludeExceptionDetailsInFaults = includeDetails
        );
        host.OnInvoke(() =>
            Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku\nforged line"))
        );

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — details cross the boundary only when the responder host opts in
        var (_, reply) = host.Replies.Sent.Should().ContainSingle().Subject;
        reply.Headers[Headers.ReplyStatus].Should().Be(ReplyProtocol.StatusFault);
        reply.Headers.Should().NotContainKey(Headers.MessageName);
        var fault = ReplyProtocol.ReadFault(reply.Body);
        fault!.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        if (includeDetails)
        {
            fault.ExceptionType.Should().Be(typeof(NotSupportedException).FullName);
            fault.Detail.Should().StartWith("bad sku").And.NotContain("\n");
        }
        else
        {
            fault.ExceptionType.Should().BeNull();
            fault.Detail.Should().BeNull();
        }
    }

    [Fact]
    public async Task should_not_fault_when_the_terminal_write_finds_the_row_already_terminal()
    {
        // given
        await using var host = ResponderExecutorHost.Create(stateWriteTakesEffect: false);
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku")));

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fault_with_null_response_after_one_attempt_when_the_responder_returns_null()
    {
        // given — the failure policy would retry a transient failure, but a null response is not one
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(null)));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — terminal, with no ok reply and no exhausted callback
        result.Succeeded.Should().BeFalse();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        host.FaultCodes().Should().Equal(RequestFaultCodes.NullResponse);
        host.Replies.Sent.Should().ContainSingle();
        host.ExhaustedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_run_a_responder_reached_by_a_plain_enqueue_and_send_nothing()
    {
        // given — the same responder, reached by a Queue message nobody awaits
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(null)));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.Zero, asRequest: false), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        host.Invoker.ReceivedCalls().Should().ContainSingle();
        host.Replies.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_complete_the_consumed_message_when_the_reply_send_fails()
    {
        // given
        await using var host = ResponderExecutorHost.Create();
        host.Replies.FailWith = new InvalidOperationException("broker unavailable");
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(42))));

        // when
        var result = await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken);

        // then — the work is done and stays done; only the caller loses its answer
        result.Succeeded.Should().BeTrue();
        host.StateWrites().Should().ContainSingle().Which.Status.Should().Be(StatusName.Succeeded);
    }

    [Fact]
    public async Task should_finish_the_attempt_after_the_transport_publish_timeout_when_the_reply_send_never_completes()
    {
        // given — a broker that accepts the reply write but never completes it
        await using var host = ResponderExecutorHost.Create(options =>
            options.TransportPublishTimeout = TimeSpan.FromSeconds(3)
        );
        host.Replies.StallSends = true;
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(new PriceQuote(42))));
        var execution = host.Executor.ExecuteAsync(
            host.Request(TimeSpan.FromSeconds(30)),
            host.Provider,
            ResponderExecutorHost.ResponderDescriptor(),
            AbortToken
        );
        await host.Replies.SendStalled.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        host.Clock.Advance(TimeSpan.FromSeconds(3));

        // then — the reply is given up, the consumed message stays done
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        result.Succeeded.Should().BeTrue();
        host.Replies.Sent.Should().BeEmpty();
        host.StateWrites().Should().ContainSingle().Which.Status.Should().Be(StatusName.Succeeded);
    }

    [Fact]
    public async Task should_run_the_responder_under_the_request_tenant_when_the_host_propagates_tenants()
    {
        // given — the responder records the tenant it observes while it answers
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        await using var host = ResponderExecutorHost.Create(realInvoker: true, services: builder.Services);
        string? observedTenant = null;
        var descriptor = ResponderExecutorHost.ResponderDescriptor(
            (services, context, _) =>
            {
                observedTenant = services.GetRequiredService<ICurrentTenant>().Id;
                ((ConsumeContext<PriceQuoteRequest>)context).RecordReply(new PriceQuote(1));
                return ValueTask.CompletedTask;
            }
        );

        // when
        var result = await host.ExecuteAsync(
            host.Request(TimeSpan.FromSeconds(30), tenantId: "tenant-a"),
            AbortToken,
            descriptor
        );

        // then
        result.Succeeded.Should().BeTrue();
        observedTenant.Should().Be("tenant-a");
        host.Replies.Sent.Should().ContainSingle().Which.Reply.Headers[Headers.TenantId].Should().Be("tenant-a");
        host.Provider.GetRequiredService<ICurrentTenant>().Id.Should().BeNull("the tenant scope ends with the attempt");
    }

    [Fact]
    public async Task should_run_a_plain_queue_consumer_under_the_envelope_tenant_when_the_host_propagates_tenants()
    {
        // given — a plain Queue consumer, not a responder, records the tenant it observes
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        await using var host = ResponderExecutorHost.Create(realInvoker: true, services: builder.Services);
        string? observedTenant = null;
        var descriptor = ResponderExecutorHost.PlainQueueDescriptor(
            (services, _, _) =>
            {
                observedTenant = services.GetRequiredService<ICurrentTenant>().Id;
                return ValueTask.CompletedTask;
            }
        );

        // when
        var result = await host.ExecuteAsync(
            host.Request(TimeSpan.FromSeconds(30), tenantId: "tenant-a", asRequest: false),
            AbortToken,
            descriptor
        );

        // then
        result.Succeeded.Should().BeTrue();
        observedTenant.Should().Be("tenant-a");
        host.Replies.Sent.Should().BeEmpty();
        host.Provider.GetRequiredService<ICurrentTenant>().Id.Should().BeNull("the tenant scope ends with the attempt");
    }

    [Fact]
    public async Task should_run_a_plain_queue_consumer_under_the_envelope_tenant_on_the_transactional_tier()
    {
        // given — the attempt scope's services resolve under the envelope tenant, as the inbox runner's DbContext would
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        RecordingReplyTransport? replies = null;
        FakeInboxTransactionRunner? runner = null;
        await using var host = ResponderExecutorHost.Create(
            options => options.RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional,
            services =>
                services.AddScoped<IInboxTransactionRunner>(sp =>
                    runner = new FakeInboxTransactionRunner(
                        sp.GetRequiredService<IUnitOfWorkFactory>(),
                        InboxCommit.Commit,
                        replies!,
                        sp.GetRequiredService<ICurrentTenant>()
                    )
                ),
            realInvoker: true,
            services: builder.Services
        );
        replies = host.Replies;
        string? observedTenant = null;
        var descriptor = ResponderExecutorHost.PlainQueueDescriptor(
            (services, _, _) =>
            {
                observedTenant = services.GetRequiredService<ICurrentTenant>().Id;
                return ValueTask.CompletedTask;
            }
        );
        var message = host.Request(TimeSpan.FromSeconds(30), tenantId: "tenant-a", asRequest: false);
        message.InboxKey = new InboxKey(
            TenantId: "tenant-a",
            message.Origin.Id,
            MessageLane.Queue,
            ResponderExecutorHost.MessageName,
            "1",
            QuoteResponder.Identity,
            Generation: 0
        );

        // when
        var result = await host.ExecuteAsync(message, AbortToken, descriptor);

        // then — the attempt scope itself held the tenant before the consume middleware ran, and the consumer saw it too
        result.Succeeded.Should().BeTrue();
        runner!.TenantAtStart.Should().Be("tenant-a", "the inbox runner resolves under the envelope tenant");
        observedTenant.Should().Be("tenant-a");
        host.Provider.GetRequiredService<ICurrentTenant>().Id.Should().BeNull("the tenant scope ends with the attempt");
    }

    [Fact]
    public async Task should_fault_a_responder_that_tries_to_publish_a_callback_response()
    {
        // given — the responder answers through the callback slot instead of returning its response
        await using var host = ResponderExecutorHost.Create(
            options => options.RequestReply.IncludeExceptionDetailsInFaults = true,
            realInvoker: true
        );
        var descriptor = ResponderExecutorHost.ResponderDescriptor(
            (_, context, _) =>
            {
                context.SetResponse(new PriceQuote(1));
                return ValueTask.CompletedTask;
            },
            FailurePolicyDefinition.None
        );

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30)), AbortToken, descriptor);

        // then
        var fault = ReplyProtocol.ReadFault(host.Replies.Sent.Should().ContainSingle().Subject.Reply.Body);
        fault!.Code.Should().Be(RequestFaultCodes.HandlerFailed);
        fault.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    public enum InboxCommit
    {
        Commit,
        Rollback,
        Indeterminate,
    }

    /// <summary>
    /// Stands in for the EF inbox runners: it opens a unit around the handler, then commits it, rolls it back, or ends
    /// with a commit whose outcome is unknown.
    /// </summary>
    private sealed class FakeInboxTransactionRunner(
        IUnitOfWorkFactory unitOfWorkFactory,
        InboxCommit commit,
        RecordingReplyTransport replies,
        ICurrentTenant? currentTenant = null
    ) : IInboxTransactionRunner
    {
        public int RepliesBeforeCommit { get; private set; } = -1;

        /// <summary>
        /// The tenant the attempt scope held when the runner started, before any consume middleware ran: the
        /// scope an EF inbox runner's DbContext would resolve under.
        /// </summary>
        public string? TenantAtStart { get; private set; }

        public async Task ExecuteAsync(
            MediumMessage message,
            Func<IUnitOfWork, CancellationToken, Task> handler,
            CancellationToken cancellationToken
        )
        {
            TenantAtStart = currentTenant?.Id;
            await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken);

            await handler(unitOfWork, cancellationToken);
            RepliesBeforeCommit = replies.Sent.Count;

            switch (commit)
            {
                case InboxCommit.Commit:
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return;
                case InboxCommit.Rollback:
                    await unitOfWork.RollbackAsync();
                    throw new UncommittedInboxCommitException(
                        message.StorageId,
                        new InvalidOperationException("rolled back")
                    );
                default:
                    throw new IndeterminateInboxCommitException(
                        message.StorageId,
                        new TimeoutException("commit"),
                        new TimeoutException("probe")
                    );
            }
        }
    }
}
