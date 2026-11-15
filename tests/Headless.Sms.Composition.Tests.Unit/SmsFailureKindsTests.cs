// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Sms;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Tests;

public sealed class SmsFailureKindsTests
{
    [Fact]
    public void should_classify_faults_before_the_request_was_sent_as_transient()
    {
        SmsFailureKinds
            .FromException(new HttpRequestException(HttpRequestError.ConnectionError))
            .Should()
            .Be(SmsFailureKind.Transient);
        SmsFailureKinds
            .FromException(new HttpRequestException(HttpRequestError.NameResolutionError))
            .Should()
            .Be(SmsFailureKind.Transient);
        SmsFailureKinds
            .FromException(new SocketException((int)SocketError.ConnectionRefused))
            .Should()
            .Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new BrokenCircuitException()).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new RateLimiterRejectedException()).Should().Be(SmsFailureKind.Transient);
    }

    [Fact]
    public void should_classify_faults_that_may_follow_delivery_as_unknown()
    {
        // A timeout or reset can come after the provider accepted the message; a retry could send the SMS twice.
        SmsFailureKinds.FromException(new TimeoutRejectedException()).Should().Be(SmsFailureKind.Unknown);
        SmsFailureKinds.FromException(new TimeoutException()).Should().Be(SmsFailureKind.Unknown);
        SmsFailureKinds.FromException(new IOException()).Should().Be(SmsFailureKind.Unknown);
        SmsFailureKinds.FromException(new HttpRequestException("x")).Should().Be(SmsFailureKind.Unknown);
        SmsFailureKinds
            .FromException(new SocketException((int)SocketError.ConnectionReset))
            .Should()
            .Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public void should_classify_other_exceptions_as_unknown()
    {
        SmsFailureKinds.FromException(new InvalidOperationException()).Should().Be(SmsFailureKind.Unknown);
        SmsFailureKinds.FromException(new FormatException()).Should().Be(SmsFailureKind.Unknown);
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        var act = () => SmsFailureKinds.FromException(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
