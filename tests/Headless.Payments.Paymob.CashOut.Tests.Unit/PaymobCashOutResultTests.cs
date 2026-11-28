// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashOut;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PaymobCashOutResultTests : TestBase
{
    private static readonly PaymobCashOutResultError _Error = new("boom", HttpStatusCode.BadGateway);

    [Fact]
    public void should_invoke_the_branch_of_each_outcome_when_matching()
    {
        // when
        var succeeded = PaymobCashOutResult.Succeeded(5).Match(v => $"ok {v}", _ => "rejected", _ => "unknown");
        var rejected = PaymobCashOutResult
            .Rejected<int>(_Error)
            .Match(_ => "ok", e => $"rejected {e.Message}", _ => "unknown");
        var unknown = PaymobCashOutResult
            .Unknown<int>(_Error)
            .Match(_ => "ok", _ => "rejected", e => $"unknown {e.Message}");

        // then
        succeeded.Should().Be("ok 5");
        rejected.Should().Be("rejected boom");
        unknown.Should().Be("unknown boom");
    }

    [Fact]
    public void should_project_only_a_succeeded_value_when_mapping()
    {
        // when
        var succeeded = PaymobCashOutResult.Succeeded(5).Map(v => v * 2);
        var unknown = PaymobCashOutResult.Unknown(_Error, value: 5).Map(v => v * 2);

        // then
        succeeded.Value.Should().Be(10);
        unknown.IsUnknown.Should().BeTrue();
        unknown.Error.Should().BeSameAs(_Error);
        unknown.Value.Should().Be(0);
    }

    [Fact]
    public void should_keep_the_parsed_answer_on_a_rejected_result()
    {
        // when
        var result = PaymobCashOutResult.Rejected(_Error, value: "declined");

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Rejected);
        result.IsSucceeded.Should().BeFalse();
        result.Value.Should().Be("declined");
    }

    [Fact]
    public void should_read_an_unset_outcome_as_unknown()
    {
        // then - a default outcome must never look safe to act on as a success
        default(PaymobCashOutOutcome).Should().Be(PaymobCashOutOutcome.Unknown);
    }
}
