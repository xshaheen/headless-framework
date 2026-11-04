// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Reliability;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.RequestReply;

namespace Tests.Consume;

/// <summary>
/// Every terminal outcome a consume attempt stores reaches <see cref="RetryPolicyOptions.OnTerminal"/> once, naming why
/// the framework gave up and which tenant the envelope belongs to, so one callback can alert, dead-letter, or route
/// per tenant without telling the reasons apart from the exception type.
/// </summary>
[Collection(RequestReplyCollection.Name)]
public sealed class ConsumeOnTerminalTests : TestBase
{
    private const string _Tenant = "tenant-a";

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_retries_exhausted_with_the_envelope_tenant_when_the_budget_is_spent()
    {
        // given — a policy with no retries, so the first transient failure spends the budget
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new TimeoutException("transient")));
        var message = host.Request(TimeSpan.Zero, tenantId: _Tenant, asRequest: false);

        // when
        await host.ExecuteAsync(
            message,
            AbortToken,
            ResponderExecutorHost.ResponderDescriptor(failurePolicy: FailurePolicyDefinition.None)
        );

        // then
        var info = host.Exhausted.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.RetriesExhausted);
        info.TenantId.Should().Be(_Tenant);
        info.MessageType.Should().Be(MessageType.Subscribe);
        info.StorageId.Should().Be(message.StorageId);
        info.Exception!.GetBaseException().Should().BeOfType<TimeoutException>();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_permanent_failure_when_a_failure_in_the_permanent_set_ends_the_message()
    {
        // given — NotSupportedException is in the built-in permanent set
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku")));

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.Zero, tenantId: _Tenant, asRequest: false), AbortToken);

        // then
        var info = host.Exhausted.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.PermanentFailure);
        info.TenantId.Should().Be(_Tenant);
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_permanent_failure_when_a_responder_returns_a_null_response()
    {
        // given — retrying cannot turn a null response into one, so the request ends at once
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromResult(ResponderExecutorHost.Replied(null)));

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.FromSeconds(30), tenantId: _Tenant), AbortToken);

        // then
        host.StateWrites().Should().ContainSingle().Which.Should().Be((StatusName.Failed, (RetryDelay?)null));
        var info = host.Exhausted.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.PermanentFailure);
        info.TenantId.Should().Be(_Tenant);
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_delivery_deadline_expired_when_a_stored_message_outlives_its_deadline()
    {
        // given — admitted before its delivery deadline, dispatched after it
        await using var host = ResponderExecutorHost.Create();
        var message = host.Request(TimeSpan.Zero, tenantId: _Tenant, asRequest: false);
        message.Origin.Headers[Headers.DeliverBy] = host
            .Clock.GetUtcNow()
            .AddSeconds(5)
            .ToString("O", CultureInfo.InvariantCulture);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        await host.ExecuteAsync(message, AbortToken);

        // then — the consumer never runs, and the callback still learns the message ended
        host.Invoker.ReceivedCalls().Should().BeEmpty();
        var info = host.Exhausted.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.DeliveryDeadlineExpired);
        info.TenantId.Should().Be(_Tenant);
        info.Exception.Should().BeOfType<MessageDeliverByExceededException>();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_delivery_deadline_expired_when_a_stored_request_outlives_its_caller()
    {
        // given — the caller stopped waiting before the attempt started
        await using var host = ResponderExecutorHost.Create();
        var request = host.Request(TimeSpan.FromSeconds(5), tenantId: _Tenant);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        await host.ExecuteAsync(request, AbortToken);

        // then — no reply to a caller that is gone, but the callback fires once
        host.Replies.Sent.Should().BeEmpty();
        var info = host.Exhausted.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.DeliveryDeadlineExpired);
        info.TenantId.Should().Be(_Tenant);
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_no_tenant_when_the_envelope_carries_none()
    {
        // given
        await using var host = ResponderExecutorHost.Create();
        host.OnInvoke(() => Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku")));

        // when
        await host.ExecuteAsync(host.Request(TimeSpan.Zero, asRequest: false), AbortToken);

        // then
        host.Exhausted.Should().ContainSingle().Which.TenantId.Should().BeNull();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_not_report_again_when_storage_proves_the_row_already_terminal()
    {
        // given — a racing writer already ended the row, so this terminal write lands nowhere
        await using var host = ResponderExecutorHost.Create(stateWriteTakesEffect: false);
        var message = host.Request(TimeSpan.Zero, tenantId: _Tenant, asRequest: false);
        message.Origin.Headers[Headers.DeliverBy] = host
            .Clock.GetUtcNow()
            .AddSeconds(5)
            .ToString("O", CultureInfo.InvariantCulture);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        // when
        await host.ExecuteAsync(message, AbortToken);

        // then
        host.ExhaustedCalls.Should().Be(0);
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_keep_consuming_when_the_terminal_callback_throws()
    {
        // given — a callback that throws on every call
        await using var host = ResponderExecutorHost.Create(configure: options =>
            options.RetryPolicy.OnTerminal = (_, _) => throw new InvalidOperationException("callback failed")
        );
        var failing = true;
        host.OnInvoke(() =>
            failing
                ? Task.FromException<ConsumerExecutedResult>(new NotSupportedException("bad sku"))
                : Task.FromResult(ResponderExecutorHost.Completed())
        );

        // when — one message ends terminally, then the next one is consumed by the same executor
        var terminal = await host.ExecuteAsync(host.Request(TimeSpan.Zero, asRequest: false), AbortToken);
        failing = false;
        var next = await host.ExecuteAsync(host.Request(TimeSpan.Zero, asRequest: false), AbortToken);

        // then — the throw is absorbed: the terminal attempt reports its own failure, and the next attempt succeeds
        terminal.Succeeded.Should().BeFalse();
        terminal.Exception!.GetBaseException().Should().BeOfType<NotSupportedException>();
        host.ExhaustedCalls.Should().Be(1);
        next.Succeeded.Should().BeTrue();
    }
}
