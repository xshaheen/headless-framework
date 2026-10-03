// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internal;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Drives <see cref="FcmMessageSender"/> through the real FirebaseAdmin SDK over a fake HTTP transport, so the SDK's
/// own error mapping, its built-in 503 and transport retries, and its multicast fan-out all run as in production.
/// Every retry delay of ours is a <see cref="RecordingTimeProvider"/> timer, so no test waits on it in real time.
/// </summary>
public sealed class FcmMessageSenderTests : TestBase
{
    private static readonly FcmMessage _Message = new()
    {
        Notification = new FcmNotification { Title = "title", Body = "body" },
    };

    private readonly FcmTestRig _rig = new();
    private readonly RecordingTimeProvider _time;
    private readonly FakeFcmHttpHandler _http;

    public FcmMessageSenderTests()
    {
        _time = _rig.Time;
        _http = _rig.Http;
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _rig.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_return_succeeded_with_the_fcm_message_name_when_fcm_accepts_the_send()
    {
        // given
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().Be("projects/test-project/messages/fid-1");
        _http.Requests.Should().ContainSingle().Which.Target.Should().Be("fid-1");
        _http.TokenRequests.Should().BePositive("the credential exchange must run against the fake, not Google");
    }

    [Fact]
    public async Task should_return_unregistered_when_fcm_reports_unregistered()
    {
        // given
        _http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("UNREGISTERED"));
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken);

        // then
        result.IsUnregistered().Should().BeTrue();
        _http.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, PushNotificationResponseStatus.Failure)]
    [InlineData(true, PushNotificationResponseStatus.Unregistered)]
    public async Task should_report_sender_id_mismatch_as_unregistered_only_when_the_option_is_enabled(
        bool treatAsUnregistered,
        PushNotificationResponseStatus expected
    )
    {
        // given
        _http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("SENDER_ID_MISMATCH"));
        var sender = _CreateSender(o => o.TreatSenderIdMismatchAsUnregistered = treatAsUnregistered);

        // when
        var single = await _SendAsync(sender, "fid-1", AbortToken);
        var batch = await _SendBatchAsync(sender, ["fid-2"], AbortToken);

        // then
        single.Status.Should().Be(expected);
        batch.Should().ContainSingle().Which.Status.Should().Be(expected);
    }

    [Fact]
    public async Task should_return_failed_without_retrying_when_fcm_reports_invalid_argument()
    {
        // given
        _http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("INVALID_ARGUMENT"));
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken);

        // then
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("InvalidArgument: ");
        _http.Requests.Should().ContainSingle();
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_describe_an_error_without_an_fcm_code_by_its_platform_code()
    {
        // given a 404 whose body is not a google.rpc error, so the SDK sets no MessagingErrorCode
        _http.Responder = static (_, _) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") }
            );
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken);

        // then
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("NotFound: ");
    }

    [Fact]
    public async Task should_retry_internal_after_a_jittered_delay_of_at_least_ten_seconds_and_succeed()
    {
        // given
        _http.Responder = static (request, _) =>
            Task.FromResult(
                request.Attempt == 1 ? FakeFcmHttpHandler.Error("INTERNAL") : FakeFcmHttpHandler.Success(request.Target)
            );
        var sender = _CreateSender();

        // when
        var send = _SendAsync(sender, "fid-1", AbortToken);
        var delay = await _time.WaitForTimerAsync(1, AbortToken);

        // then
        delay
            .Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10))
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(15));
        _http.Requests.Should().ContainSingle("the retry must not start before the delay elapses");

        _time.Advance(delay);
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        result.IsSucceeded().Should().BeTrue();
        _http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_wait_the_retry_after_value_when_fcm_reports_quota_exceeded_with_a_longer_retry_after()
    {
        // given
        _http.Responder = static (request, _) =>
            Task.FromResult(
                request.Attempt == 1
                    ? FakeFcmHttpHandler.Error("QUOTA_EXCEEDED", TimeSpan.FromSeconds(120))
                    : FakeFcmHttpHandler.Success(request.Target)
            );
        var sender = _CreateSender();

        // when
        var send = _SendAsync(sender, "fid-1", AbortToken);
        var delay = await _time.WaitForTimerAsync(1, AbortToken);

        // then
        delay.Should().Be(TimeSpan.FromSeconds(120));
        _time.Advance(TimeSpan.FromSeconds(119));
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        _http.Requests.Should().ContainSingle("the retry must not start before Retry-After elapses");

        _time.Advance(TimeSpan.FromSeconds(1));
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        result.IsSucceeded().Should().BeTrue();
        _http.Requests.Should().HaveCount(2);
        (_http.Requests[1].ReceivedAt - _http.Requests[0].ReceivedAt).Should().Be(TimeSpan.FromSeconds(120));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    public async Task should_wait_sixty_seconds_when_fcm_reports_quota_exceeded_without_a_longer_retry_after(
        int? retryAfterSeconds
    )
    {
        // given
        TimeSpan? retryAfter = retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        _http.Responder = (request, _) =>
            Task.FromResult(
                request.Attempt == 1
                    ? FakeFcmHttpHandler.Error("QUOTA_EXCEEDED", retryAfter)
                    : FakeFcmHttpHandler.Success(request.Target)
            );
        var sender = _CreateSender();

        // when
        var send = _SendAsync(sender, "fid-1", AbortToken);
        var delay = await _time.WaitForTimerAsync(1, AbortToken);
        _time.Advance(delay);
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        delay.Should().Be(TimeSpan.FromSeconds(60));
        result.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_not_retry_quota_exceeded_when_retry_after_exceeds_max_delay()
    {
        // given
        _http.Responder = static (_, _) =>
            Task.FromResult(FakeFcmHttpHandler.Error("QUOTA_EXCEEDED", TimeSpan.FromMinutes(10)));
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken);

        // then
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("QuotaExceeded: ");
        _http.Requests.Should().ContainSingle();
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_after_max_attempts_retries_of_internal()
    {
        // given
        _http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("INTERNAL"));
        var sender = _CreateSender(o => o.Retry.MaxAttempts = 2);

        // when
        var send = _SendAsync(sender, "fid-1", AbortToken);
        _time.Advance(await _time.WaitForTimerAsync(1, AbortToken));
        _time.Advance(await _time.WaitForTimerAsync(2, AbortToken));
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("Internal: ");
        _http.Requests.Should().HaveCount(3);
        _time.Timers.Should().HaveCount(2);
        _time.Timers[1].Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20), "the backoff doubles each retry");
    }

    [Fact]
    public async Task should_not_retry_when_max_attempts_is_zero()
    {
        // given
        _http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("INTERNAL"));
        var sender = _CreateSender(o => o.Retry.MaxAttempts = 0);

        // when
        var single = await _SendAsync(sender, "fid-1", AbortToken);
        var batch = await _SendBatchAsync(sender, ["fid-2", "fid-3"], AbortToken);

        // then
        single.IsFailed().Should().BeTrue();
        batch.Should().OnlyContain(r => r.IsFailed());
        _http.Requests.Should().HaveCount(3);
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_unavailable_to_the_sdk_retry_and_not_retry_it_again()
    {
        // given the SDK retries a 503 up to 4 times itself, honoring a Retry-After of up to 30 seconds in real time;
        // a 1-second Retry-After keeps that real wait at 4 seconds
        _http.Responder = static (_, _) =>
            Task.FromResult(FakeFcmHttpHandler.Error("UNAVAILABLE", TimeSpan.FromSeconds(1)));
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then 1 request plus the SDK's 4 retries, and no retry delay of ours
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("Unavailable: ");
        _http.Requests.Should().HaveCount(5);
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_the_sdk_retry_a_transport_failure()
    {
        // given
        _http.Responder = static (request, _) =>
            request.Attempt == 1
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult(FakeFcmHttpHandler.Success(request.Target));
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        result.IsSucceeded().Should().BeTrue();
        _http.Requests.Should().HaveCount(2);
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_failed_when_the_request_times_out()
    {
        // given an HttpClient timeout, which surfaces as a TaskCanceledException the caller did not ask for
        _http.Responder = static (_, _) =>
            throw new TaskCanceledException("The request timed out.", new TimeoutException());
        var sender = _CreateSender();

        // when
        var result = await _SendAsync(sender, "fid-1", AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        result.IsFailed().Should().BeTrue();
        result.FailureError.Should().StartWith("TaskCanceledException: ");
        _http.Requests.Should().ContainSingle();
        _time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_failed_when_the_credentials_cannot_be_loaded()
    {
        // given
        var sender = _CreateSender(o => o.Json = "{}");

        // when
        var single = await _SendAsync(sender, "fid-1", AbortToken);
        var batch = await _SendBatchAsync(sender, ["fid-2", "fid-3"], AbortToken);

        // then
        single.IsFailed().Should().BeTrue();
        batch.Should().HaveCount(2).And.OnlyContain(r => r.IsFailed());
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_the_caller_cancels_a_single_send()
    {
        // given
        _http.Responder = static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("unreachable");
        };
        var sender = _CreateSender();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var send = sender.SendAsync(_Message, FcmTarget.Token("fid-1"), cts.Token);
        await _http.WaitForRequestsAsync(1, AbortToken);
        await cts.CancelAsync();
        var action = async () => await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_resend_only_transient_failures_of_a_multicast_and_keep_input_order()
    {
        // given
        _http.Responder = static (request, _) =>
            Task.FromResult(
                (request.Target, request.Attempt) switch
                {
                    ("fid-unregistered", _) => FakeFcmHttpHandler.Error("UNREGISTERED"),
                    ("fid-internal", 1) => FakeFcmHttpHandler.Error("INTERNAL"),
                    ("fid-quota", 1) => FakeFcmHttpHandler.Error("QUOTA_EXCEEDED", TimeSpan.FromSeconds(90)),
                    ("fid-invalid", _) => FakeFcmHttpHandler.Error("INVALID_ARGUMENT"),
                    _ => FakeFcmHttpHandler.Success(request.Target),
                }
            );
        var sender = _CreateSender();
        string[] fids = ["fid-ok", "fid-unregistered", "fid-internal", "fid-quota", "fid-invalid"];

        // when
        var send = _SendBatchAsync(sender, fids, AbortToken);
        var delay = await _time.WaitForTimerAsync(1, AbortToken);
        _time.Advance(delay);
        var results = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then the round waits for the longest of its transient delays
        delay.Should().Be(TimeSpan.FromSeconds(90));
        results.Select(r => r.ClientIdentifier).Should().Equal(fids);
        results
            .Select(r => r.Status)
            .Should()
            .Equal(
                PushNotificationResponseStatus.Success,
                PushNotificationResponseStatus.Unregistered,
                PushNotificationResponseStatus.Success,
                PushNotificationResponseStatus.Success,
                PushNotificationResponseStatus.Failure
            );
        _http.RequestsFor("fid-ok").Should().ContainSingle();
        _http.RequestsFor("fid-unregistered").Should().ContainSingle();
        _http.RequestsFor("fid-invalid").Should().ContainSingle();
        _http.RequestsFor("fid-internal").Should().HaveCount(2);
        _http.RequestsFor("fid-quota").Should().HaveCount(2);
        _time.Timers.Should().ContainSingle();
    }

    [Fact]
    public async Task should_throw_when_the_caller_cancels_a_multicast()
    {
        // given the SDK turns each send's cancellation into a per-message failure instead of throwing
        _http.Responder = static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("unreachable");
        };
        var sender = _CreateSender();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var send = sender.SendBatchAsync(_Message, ["fid-1", "fid-2"], cts.Token);
        await _http.WaitForRequestsAsync(2, AbortToken);
        await cts.CancelAsync();
        var action = async () => await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_resend_a_multicast_over_several_rounds_and_settle_each_token_in_its_own_round()
    {
        // given one token that needs two retries and one that turns permanent in the second round
        _http.Responder = static (request, _) =>
            Task.FromResult(
                (request.Target, request.Attempt) switch
                {
                    ("fid-twice", < 3) => FakeFcmHttpHandler.Error("INTERNAL"),
                    ("fid-gone", 1) => FakeFcmHttpHandler.Error("INTERNAL"),
                    ("fid-gone", _) => FakeFcmHttpHandler.Error("UNREGISTERED"),
                    _ => FakeFcmHttpHandler.Success(request.Target),
                }
            );
        var sender = _CreateSender();
        string[] fids = ["fid-twice", "fid-ok", "fid-gone"];

        // when
        var send = _SendBatchAsync(sender, fids, AbortToken);
        var first = await _time.WaitForTimerAsync(1, AbortToken);
        _time.Advance(first);
        var second = await _time.WaitForTimerAsync(2, AbortToken);
        _time.Advance(second);
        var results = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then the backoff doubles per round and results stay in input order
        first.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10)).And.BeLessThan(TimeSpan.FromSeconds(15));
        second.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20)).And.BeLessThan(TimeSpan.FromSeconds(30));
        results.Select(r => r.ClientIdentifier).Should().Equal(fids);
        results
            .Select(r => r.Status)
            .Should()
            .Equal(
                PushNotificationResponseStatus.Success,
                PushNotificationResponseStatus.Success,
                PushNotificationResponseStatus.Unregistered
            );
        _http.RequestsFor("fid-twice").Should().HaveCount(3);
        _http.RequestsFor("fid-ok").Should().ContainSingle();
        _http.RequestsFor("fid-gone").Should().HaveCount(2);
        _time.Timers.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_wait_until_a_retry_after_date_when_fcm_reports_quota_exceeded()
    {
        // given a Retry-After sent as an HTTP date rather than a number of seconds
        var retryAt = _time.GetUtcNow().AddSeconds(120);
        _http.Responder = (request, _) =>
        {
            if (request.Attempt > 1)
            {
                return Task.FromResult(FakeFcmHttpHandler.Success(request.Target));
            }

            var response = FakeFcmHttpHandler.Error("QUOTA_EXCEEDED");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAt);

            return Task.FromResult(response);
        };
        var sender = _CreateSender();

        // when
        var send = _SendAsync(sender, "fid-1", AbortToken);
        var delay = await _time.WaitForTimerAsync(1, AbortToken);
        _time.Advance(delay);
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then the header's one-second resolution may shave the fraction off the wait, never more
        delay.Should().BeGreaterThan(TimeSpan.FromSeconds(119)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(120));
        result.IsSucceeded().Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_the_caller_cancels_while_a_single_send_fails()
    {
        // given the caller cancels while FCM's answer is an error, so the SDK throws that error and not a cancellation
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        _http.Responder = async (_, _) =>
        {
            await cts.CancelAsync();

            return FakeFcmHttpHandler.Error("INVALID_ARGUMENT");
        };
        var sender = _CreateSender();

        // when
        var send = sender.SendAsync(_Message, FcmTarget.Token("fid-1"), cts.Token);
        var action = async () => await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private FcmMessageSender _CreateSender(Action<FirebaseOptions>? configure = null)
    {
        return _rig.CreateSender(configure);
    }

    private static async Task<PushNotificationResponse> _SendAsync(
        FcmMessageSender sender,
        string fid,
        CancellationToken cancellationToken
    )
    {
        var result = await sender.SendAsync(_Message, FcmTarget.Token(fid), cancellationToken);

        return result.Response;
    }

    private static async Task<IReadOnlyList<PushNotificationResponse>> _SendBatchAsync(
        FcmMessageSender sender,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    )
    {
        var results = await sender.SendBatchAsync(_Message, fids, cancellationToken);

        return [.. results.Select(static r => r.Response)];
    }
}
