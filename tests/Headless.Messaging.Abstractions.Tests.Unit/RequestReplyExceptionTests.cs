// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RequestReplyExceptionTests : TestBase
{
    [Fact]
    public void should_expose_the_fault_code_and_leave_remote_details_null_when_not_supplied()
    {
        // when
        var exception = new RequestFaultedException("req-1", "handler_failed");

        // then
        exception.Code.Should().Be("handler_failed");
        exception.RequestId.Should().Be("req-1");
        exception.RemoteExceptionType.Should().BeNull();
        exception.Detail.Should().BeNull();
        exception.Message.Should().Contain("handler_failed");
    }

    [Fact]
    public void should_expose_remote_details_when_the_responder_supplied_them()
    {
        // when
        var exception = new RequestFaultedException(
            "req-1",
            "handler_failed",
            "System.InvalidOperationException",
            "price list is closed"
        );

        // then
        exception.RemoteExceptionType.Should().Be("System.InvalidOperationException");
        exception.Detail.Should().Be("price list is closed");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_a_blank_fault_code(string code)
    {
        var act = () => new RequestFaultedException("req-1", code);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_let_callers_catch_every_request_failure_through_one_base()
    {
        // given
        RequestReplyException[] failures =
        [
            new RequestTimeoutException("req-1", TimeSpan.FromSeconds(30)),
            new RequestFaultedException("req-1", "no_responder"),
            new ResponseContractMismatchException("req-1", "pricing.quote", "2", "pricing.quote", "1"),
            new RequestNotSentException("publish middleware suppressed the request"),
            new RequestAbortedException("req-1"),
        ];

        // then
        failures.Should().AllSatisfy(failure => failure.Should().NotBeAssignableTo<OperationCanceledException>());
        failures.Should().OnlyHaveUniqueItems(failure => failure.GetType());
    }

    [Fact]
    public void should_carry_the_timeout_on_a_timeout_failure()
    {
        // when
        var exception = new RequestTimeoutException("req-1", TimeSpan.FromSeconds(30));

        // then
        exception.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        exception.RequestId.Should().Be("req-1");
    }

    [Fact]
    public void should_carry_both_contracts_on_a_contract_mismatch()
    {
        // when
        var exception = new ResponseContractMismatchException("req-1", "pricing.quote", "2", "pricing.quote", "1");

        // then
        exception.ExpectedMessageName.Should().Be("pricing.quote");
        exception.ExpectedContractVersion.Should().Be("2");
        exception.ActualMessageName.Should().Be("pricing.quote");
        exception.ActualContractVersion.Should().Be("1");
        exception.Message.Should().Contain("'2'").And.Contain("'1'");
    }

    [Fact]
    public void should_keep_the_cause_of_a_request_that_was_never_sent()
    {
        // given
        var cause = new InvalidOperationException("broker unreachable");

        // when
        var exception = new RequestNotSentException("the transport refused the request", "req-1", cause);

        // then
        exception.InnerException.Should().BeSameAs(cause);
        exception.RequestId.Should().Be("req-1");
    }

    [Fact]
    public void should_pin_the_stable_fault_code_wire_values()
    {
        RequestFaultCodes.HandlerFailed.Should().Be("handler_failed");
        RequestFaultCodes.NoResponder.Should().Be("no_responder");
        RequestFaultCodes.RequestRejected.Should().Be("request_rejected");
        RequestFaultCodes.NullResponse.Should().Be("null_response");
    }
}
