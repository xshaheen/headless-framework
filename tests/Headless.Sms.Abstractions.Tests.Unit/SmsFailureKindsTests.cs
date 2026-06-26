// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Sms;

namespace Tests;

public sealed class SmsFailureKindsTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, SmsFailureKind.None)]
    [InlineData(HttpStatusCode.Accepted, SmsFailureKind.None)]
    [InlineData(HttpStatusCode.Unauthorized, SmsFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, SmsFailureKind.AuthFailure)]
    [InlineData(HttpStatusCode.PaymentRequired, SmsFailureKind.OutOfCredit)]
    [InlineData(HttpStatusCode.TooManyRequests, SmsFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.RequestTimeout, SmsFailureKind.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, SmsFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SmsFailureKind.Transient)]
    [InlineData(HttpStatusCode.BadRequest, SmsFailureKind.Unknown)]
    [InlineData(HttpStatusCode.NotFound, SmsFailureKind.Unknown)]
    public void should_classify_http_status_codes(HttpStatusCode statusCode, SmsFailureKind expected)
    {
        SmsFailureKinds.FromHttpStatusCode(statusCode).Should().Be(expected);
    }

    [Fact]
    public void should_classify_transport_exceptions_as_transient()
    {
        SmsFailureKinds.FromException(new HttpRequestException("x")).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new TimeoutException()).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new IOException()).Should().Be(SmsFailureKind.Transient);
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
