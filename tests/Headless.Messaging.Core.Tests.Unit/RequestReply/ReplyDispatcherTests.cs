// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Messaging;
using Headless.Messaging.RequestReply;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RequestReply;

/// <summary>
/// Covers how a received reply is matched to its call: at most one reply completes a call, and every other reply is
/// dropped and counted under the reason it could not answer the call.
/// </summary>
[Collection(RequestReplyCollection.Name)]
public sealed class ReplyDispatcherTests : TestBase
{
    private const string _ResponseName = "pricing.quote";
    private const string _ResponseVersion = "2";
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly PendingRequests _pending = new();
    private readonly ReplyDispatcher _dispatcher;

    public ReplyDispatcherTests()
    {
        _dispatcher = new ReplyDispatcher(
            _pending,
            new JsonUtf8Serializer(Options.Create(new MessagingOptions())),
            NullLogger<ReplyDispatcher>.Instance
        );
    }

    [Fact]
    public async Task should_complete_the_call_with_the_response_of_a_matching_ok_reply()
    {
        // given
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":12.5}"""), AbortToken);

        // then
        (await call.Outcome)
            .Should()
            .Be(new PriceQuote(12.5m));
        call.State.Should().Be(PendingRequestState.Replied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-sent")]
    public async Task should_drop_a_reply_naming_no_known_request_as_unknown(string? requestId)
    {
        // given
        using var measurements = new RequestReplyMeasurements();
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Ok(requestId, """{"Price":1}"""), AbortToken);

        // then
        measurements.Drops.Should().Equal("unknown");
        call.State.Should().Be(PendingRequestState.Pending);
    }

    [Fact]
    public async Task should_drop_a_reply_to_a_request_that_has_not_left_the_process_as_unknown()
    {
        // given
        using var measurements = new RequestReplyMeasurements();
        var call = _Register("request-1", tenantId: null, prepared: false);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":1}"""), AbortToken);

        // then
        measurements.Drops.Should().Equal("unknown");
        call.State.Should().Be(PendingRequestState.Pending);
    }

    [Fact]
    public async Task should_drop_a_second_reply_for_a_completed_call_as_duplicate()
    {
        // given
        using var measurements = new RequestReplyMeasurements();
        var call = _Register("request-1", tenantId: null);
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":1}"""), AbortToken);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":2}"""), AbortToken);

        // then
        measurements.Drops.Should().Equal("duplicate");
        (await call.Outcome).Should().Be(new PriceQuote(1m));
    }

    [Fact]
    public async Task should_drop_a_reply_for_a_timed_out_call_as_late()
    {
        // given
        using var measurements = new RequestReplyMeasurements();
        var call = _Register("request-1", tenantId: null);
        _time.Advance(_Timeout);
        await call.Awaiting(x => x.Outcome).Should().ThrowAsync<RequestTimeoutException>();

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":1}"""), AbortToken);

        // then
        measurements.Drops.Should().Equal("late");
    }

    [Theory]
    [InlineData("tenant-a", "tenant-b")]
    [InlineData("tenant-a", null)]
    [InlineData(null, "tenant-b")]
    public async Task should_drop_a_reply_whose_tenant_differs_and_keep_the_call_waiting(
        string? requestTenant,
        string? replyTenant
    )
    {
        // given
        using var measurements = new RequestReplyMeasurements();
        var call = _Register("request-1", requestTenant);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":1}""", replyTenant), AbortToken);

        // then
        measurements.Drops.Should().Equal("tenant_mismatch");
        call.State.Should().Be(PendingRequestState.Pending);
    }

    [Fact]
    public async Task should_complete_the_call_when_the_reply_carries_the_request_tenant()
    {
        // given
        var call = _Register("request-1", "tenant-a");

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":4}""", "tenant-a"), AbortToken);

        // then
        (await call.Outcome)
            .Should()
            .Be(new PriceQuote(4m));
    }

    [Fact]
    public async Task should_fail_with_the_fault_code_only_when_the_fault_shares_no_details()
    {
        // given
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Fault("request-1", """{"code":"no_responder"}"""), AbortToken);

        // then
        var thrown = await call.Awaiting(x => x.Outcome).Should().ThrowAsync<RequestFaultedException>();
        thrown.Which.Code.Should().Be(RequestFaultCodes.NoResponder);
        thrown.Which.RemoteExceptionType.Should().BeNull();
        thrown.Which.Detail.Should().BeNull();
        thrown.Which.RequestId.Should().Be("request-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"code":""}""")]
    public async Task should_fail_with_handler_failed_when_the_fault_body_is_unreadable(string body)
    {
        // given
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Fault("request-1", body), AbortToken);

        // then
        var thrown = await call.Awaiting(x => x.Outcome).Should().ThrowAsync<RequestFaultedException>();
        thrown.Which.Code.Should().Be(RequestFaultCodes.HandlerFailed);
    }

    [Fact]
    public async Task should_fail_with_null_response_when_an_ok_reply_has_no_body()
    {
        // given
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", body: ""), AbortToken);

        // then
        var thrown = await call.Awaiting(x => x.Outcome).Should().ThrowAsync<RequestFaultedException>();
        thrown.Which.Code.Should().Be(RequestFaultCodes.NullResponse);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pending")]
    public async Task should_fail_with_a_contract_mismatch_when_the_reply_status_is_not_recognized(string? status)
    {
        // given
        var call = _Register("request-1", tenantId: null);
        var reply = _Ok("request-1", """{"Price":1}""");
        if (status is null)
        {
            reply.Headers.Remove(Headers.ReplyStatus);
        }
        else
        {
            reply.Headers[Headers.ReplyStatus] = status;
        }

        // when
        await _dispatcher.DispatchAsync(reply, AbortToken);

        // then
        await call.Awaiting(x => x.Outcome).Should().ThrowAsync<ResponseContractMismatchException>();
    }

    [Fact]
    public async Task should_fail_the_call_when_the_reply_body_cannot_be_read_as_the_response()
    {
        // given
        var call = _Register("request-1", tenantId: null);

        // when
        await _dispatcher.DispatchAsync(_Ok("request-1", "not json"), AbortToken);

        // then
        await call.Awaiting(x => x.Outcome).Should().ThrowAsync<MessageDeserializationException>();
    }

    [Fact]
    public async Task should_complete_the_caller_continuation_off_the_listener_thread()
    {
        // given: a caller whose continuation blocks until released
        var call = _Register("request-1", tenantId: null);
        using var release = new ManualResetEventSlim();
        var continuation = call.Outcome.ContinueWith(
            // Bounded, so a continuation that wrongly runs on the listener's stack fails the test instead of hanging it.
            _ => release.Wait(TimeSpan.FromSeconds(5), AbortToken),
            AbortToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        // when: the dispatch returns although the caller's continuation is still blocked
        await _dispatcher.DispatchAsync(_Ok("request-1", """{"Price":1}"""), AbortToken);

        // then
        continuation.IsCompleted.Should().BeFalse();
        release.Set();
        await continuation;
    }

    private PendingRequest _Register(string requestId, string? tenantId, bool prepared = true)
    {
        var call = new PendingRequest(requestId, typeof(PriceQuote), _ResponseName, _ResponseVersion, _Timeout, _time);
        _pending.TryRegister(call, _Timeout, AbortToken).Should().BeTrue();

        if (prepared)
        {
            call.Prepare(tenantId);
        }

        return call;
    }

    private static TransportMessage _Ok(string? requestId, string body, string? tenantId = null)
    {
        var headers = _Headers(requestId, "ok", tenantId);
        headers[Headers.MessageName] = _ResponseName;
        headers[Headers.ContractVersion] = _ResponseVersion;
        return new TransportMessage(headers, Encoding.UTF8.GetBytes(body));
    }

    private static TransportMessage _Fault(string requestId, string body)
    {
        return new TransportMessage(_Headers(requestId, "fault", tenantId: null), Encoding.UTF8.GetBytes(body));
    }

    private static Dictionary<string, string?> _Headers(string? requestId, string status, string? tenantId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("D"),
            [Headers.ReplyStatus] = status,
        };

        if (requestId is not null)
        {
            headers[Headers.InReplyTo] = requestId;
        }

        if (tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        return headers;
    }
}
