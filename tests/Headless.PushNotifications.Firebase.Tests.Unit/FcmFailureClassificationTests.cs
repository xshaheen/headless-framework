// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internal;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Drives every FCM error code, platform code, transport failure, and credential failure through the real
/// FirebaseAdmin SDK over a fake transport, and checks the <see cref="FcmSendResult"/> each one becomes on a single
/// send and on a multicast.
/// </summary>
public sealed class FcmFailureClassificationTests : TestBase
{
    private static readonly FcmMessage _Message = new()
    {
        Notification = new FcmNotification { Title = "title", Body = "body" },
    };

    private readonly FcmTestRig _rig = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _rig.Dispose();

        return base.DisposeAsyncCore();
    }

    [Theory]
    [InlineData("UNREGISTERED", PushNotificationResponseStatus.Unregistered, FcmFailureKind.TokenInvalid)]
    [InlineData("SENDER_ID_MISMATCH", PushNotificationResponseStatus.Failure, FcmFailureKind.Configuration)]
    [InlineData("INVALID_ARGUMENT", PushNotificationResponseStatus.Failure, FcmFailureKind.Payload)]
    [InlineData("THIRD_PARTY_AUTH_ERROR", PushNotificationResponseStatus.Failure, FcmFailureKind.Authentication)]
    public async Task should_classify_a_permanent_fcm_code_without_retrying_it(
        string code,
        PushNotificationResponseStatus status,
        FcmFailureKind kind
    )
    {
        // given
        _rig.Http.Responder = (_, _) => Task.FromResult(FakeFcmHttpHandler.Error(code));
        var sender = _rig.CreateSender();

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2", "fid-3"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.Status.Should().Be(status);
            result.ErrorCode.Should().Be(code);
            result.FailureKind.Should().Be(kind);
            result.IsRetryable.Should().BeFalse();
            result.RetryAfter.Should().BeNull();
        }

        // A 401 makes the SDK's credential handler refresh its access token and resend, 4 times, on its own; this
        // layer never resends a permanent code.
        _rig.Http.Requests.Should()
            .HaveCount(string.Equals(code, "THIRD_PARTY_AUTH_ERROR", StringComparison.Ordinal) ? 15 : 3);
        _rig.Time.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_classify_sender_id_mismatch_as_an_invalid_token_when_the_option_is_enabled()
    {
        // given
        _rig.Http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("SENDER_ID_MISMATCH"));
        var sender = _rig.CreateSender(o => o.TreatSenderIdMismatchAsUnregistered = true);

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.IsUnregistered().Should().BeTrue();
            result.FailureKind.Should().Be(FcmFailureKind.TokenInvalid);
            result.ErrorCode.Should().Be("SENDER_ID_MISMATCH");
        }
    }

    [Theory]
    [InlineData("QUOTA_EXCEEDED", null, FcmFailureKind.Throttled, 60)]
    [InlineData("QUOTA_EXCEEDED", 90, FcmFailureKind.Throttled, 90)]
    [InlineData("INTERNAL", null, FcmFailureKind.ServerError, 10)]
    [InlineData("INTERNAL", 45, FcmFailureKind.ServerError, 45)]
    // Over the SDK's 30-second cap on Retry-After, so the SDK returns the 503 instead of retrying it in real time.
    [InlineData("UNAVAILABLE", 120, FcmFailureKind.ServerError, 120)]
    public async Task should_classify_a_transient_fcm_code_as_retryable_with_retry_after_guidance(
        string code,
        int? retryAfterSeconds,
        FcmFailureKind kind,
        int expectedRetryAfterSeconds
    )
    {
        // given in-process retry disabled, so the first answer is the result
        TimeSpan? retryAfter = retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        _rig.Http.Responder = (_, _) => Task.FromResult(FakeFcmHttpHandler.Error(code, retryAfter));
        var sender = _rig.CreateSender(o => o.Retry.MaxAttempts = 0);

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.IsFailed().Should().BeTrue();
            result.ErrorCode.Should().Be(code);
            result.FailureKind.Should().Be(kind);
            result.IsRetryable.Should().BeTrue();
            result.RetryAfter.Should().Be(TimeSpan.FromSeconds(expectedRetryAfterSeconds));
        }

        _rig.Http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_surface_retry_after_when_it_exceeds_max_delay_and_the_send_is_not_retried()
    {
        // given
        _rig.Http.Responder = static (_, _) =>
            Task.FromResult(FakeFcmHttpHandler.Error("QUOTA_EXCEEDED", TimeSpan.FromMinutes(10)));
        var sender = _rig.CreateSender();

        // when
        var result = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);

        // then
        result.FailureKind.Should().Be(FcmFailureKind.Throttled);
        result.IsRetryable.Should().BeTrue();
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
        _rig.Http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task should_classify_the_last_failure_after_in_process_retries_are_spent()
    {
        // given
        _rig.Http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("INTERNAL"));
        var sender = _rig.CreateSender(o => o.Retry.MaxAttempts = 1);

        // when
        var send = sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        _rig.Time.Advance(await _rig.Time.WaitForTimerAsync(1, AbortToken));
        var result = await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        result.FailureKind.Should().Be(FcmFailureKind.ServerError);
        result.ErrorCode.Should().Be("INTERNAL");
        result.IsRetryable.Should().BeTrue();
        _rig.Http.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", FcmFailureKind.Payload, "INVALID_ARGUMENT")]
    // The SDK reads only a few google.rpc statuses from the body and otherwise maps the HTTP status.
    [InlineData(HttpStatusCode.BadRequest, "FAILED_PRECONDITION", FcmFailureKind.Payload, "INVALID_ARGUMENT")]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", FcmFailureKind.Authentication, "UNAUTHENTICATED")]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED", FcmFailureKind.Configuration, "PERMISSION_DENIED")]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", FcmFailureKind.Configuration, "NOT_FOUND")]
    [InlineData(HttpStatusCode.Conflict, "ABORTED", FcmFailureKind.ServerError, "CONFLICT")]
    [InlineData(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", FcmFailureKind.Throttled, "RESOURCE_EXHAUSTED")]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL", FcmFailureKind.ServerError, "INTERNAL")]
    [InlineData(HttpStatusCode.GatewayTimeout, "DEADLINE_EXCEEDED", FcmFailureKind.ServerError, "UNKNOWN")]
    public async Task should_classify_a_platform_code_when_fcm_sends_no_fcm_code(
        HttpStatusCode status,
        string rpcStatus,
        FcmFailureKind kind,
        string errorCode
    )
    {
        // given
        _rig.Http.Responder = (_, _) => Task.FromResult(FakeFcmHttpHandler.PlatformError(status, rpcStatus));
        var sender = _rig.CreateSender(o => o.Retry.MaxAttempts = 0);

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.IsFailed().Should().BeTrue("a platform code is never read as a dead token");
            result.ErrorCode.Should().Be(errorCode);
            result.FailureKind.Should().Be(kind);
        }
    }

    [Fact]
    public async Task should_classify_a_server_error_body_the_sdk_cannot_read_by_its_http_status()
    {
        // given a 502 from a proxy in front of FCM, whose HTML body carries no google.rpc status
        _rig.Http.Responder = static (_, _) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html"),
                }
            );
        var sender = _rig.CreateSender();

        // when
        var result = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);

        // then
        result.Response.IsFailed().Should().BeTrue();
        result.FailureKind.Should().Be(FcmFailureKind.ServerError);
        result.ErrorCode.Should().NotBeNull();
        result.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task should_classify_a_timeout_as_a_retryable_transport_failure_without_an_error_code()
    {
        // given an HttpClient timeout, which the SDK neither retries nor wraps
        _rig.Http.Responder = static (_, _) =>
            throw new TaskCanceledException("The request timed out.", new TimeoutException());
        var sender = _rig.CreateSender();

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.IsFailed().Should().BeTrue();
            result.FailureKind.Should().Be(FcmFailureKind.Transport);
            result.ErrorCode.Should().BeNull();
            result.IsRetryable.Should().BeTrue();
            result.RetryAfter.Should().BeNull();
        }
    }

    [Fact]
    public async Task should_classify_a_network_failure_left_after_the_sdk_retries_as_transport()
    {
        // given every attempt fails to connect; the SDK wraps that in an FCM exception with no code and no response,
        // after its own 4 retries, which wait 1 + 2 + 4 + 8 seconds in real time
        _rig.Http.Responder = static (_, _) => throw new HttpRequestException("connection refused");
        var sender = _rig.CreateSender();

        // when
        var result = await sender
            .SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(60), AbortToken);

        // then
        result.Response.IsFailed().Should().BeTrue();
        result.Response.FailureError.Should().StartWith("Unknown: ");
        result.FailureKind.Should().Be(FcmFailureKind.Transport);
        result.ErrorCode.Should().BeNull();
        result.IsRetryable.Should().BeTrue();
        _rig.Http.Requests.Should().HaveCount(5);
    }

    [Fact]
    public async Task should_classify_credentials_that_cannot_be_loaded_as_authentication()
    {
        // given
        var sender = _rig.CreateSender(o => o.Json = "{}");

        // when
        var single = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);
        var batch = await sender.SendBatchAsync(_Message, ["fid-2", "fid-3"], AbortToken);

        // then
        foreach (var result in batch.Prepend(single))
        {
            result.Response.IsFailed().Should().BeTrue();
            result.FailureKind.Should().Be(FcmFailureKind.Authentication);
            result.ErrorCode.Should().BeNull();
            result.IsRetryable.Should().BeFalse();
        }

        _rig.Http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_classify_a_rejected_token_exchange_as_authentication()
    {
        // given Google refuses to exchange the service account's signed assertion for an access token
        _rig.Http.TokenResponder = static () =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"invalid_grant","error_description":"Invalid JWT Signature."}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        var sender = _rig.CreateSender();

        // when
        var result = await sender.SendAsync(_Message, FcmTarget.Token("fid-1"), AbortToken);

        // then
        result.Response.IsFailed().Should().BeTrue();
        result.FailureKind.Should().Be(FcmFailureKind.Authentication);
        result.IsRetryable.Should().BeFalse();
        _rig.Http.Requests.Should().BeEmpty("no FCM request is sent without an access token");
    }

    [Fact]
    public async Task should_classify_a_rejected_token_exchange_as_authentication_for_every_multicast_target()
    {
        // given the multicast path, where the SDK wraps each send's exception instead of letting it escape
        _rig.Http.TokenResponder = static () =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"invalid_grant","error_description":"Invalid JWT Signature."}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        var sender = _rig.CreateSender();

        // when
        var results = await sender.SendBatchAsync(_Message, ["fid-1", "fid-2"], AbortToken);

        // then a revoked key is not reported as a retryable network failure
        results.Should().HaveCount(2);
        results
            .Should()
            .AllSatisfy(r =>
            {
                r.Response.IsFailed().Should().BeTrue();
                r.FailureKind.Should().Be(FcmFailureKind.Authentication);
                r.IsRetryable.Should().BeFalse();
            });
        _rig.Http.Requests.Should().BeEmpty("no FCM request is sent without an access token");
    }
}
