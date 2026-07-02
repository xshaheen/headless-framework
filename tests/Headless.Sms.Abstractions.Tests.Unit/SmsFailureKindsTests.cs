// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Sms;

namespace Tests;

public sealed class SmsFailureKindsTests
{
    [Fact]
    public void should_classify_transport_exceptions_as_transient()
    {
        SmsFailureKinds.FromException(new HttpRequestException("x")).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new TimeoutException()).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new IOException()).Should().Be(SmsFailureKind.Transient);
        SmsFailureKinds.FromException(new SocketException()).Should().Be(SmsFailureKind.Transient);
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
