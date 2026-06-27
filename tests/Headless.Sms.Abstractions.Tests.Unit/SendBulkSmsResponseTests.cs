// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;

namespace Tests;

public sealed class SendBulkSmsResponseTests
{
    private static readonly SmsRequestDestination _A = new(20, "1001");
    private static readonly SmsRequestDestination _B = new(20, "1002");

    [Fact]
    public void should_build_from_explicit_per_recipient_results()
    {
        var results = new[]
        {
            new SmsRecipientResult(_A, SendSingleSmsResponse.Succeeded("id-a")),
            new SmsRecipientResult(_B, SendSingleSmsResponse.Failed("nope", SmsFailureKind.InvalidRecipient)),
        };

        var response = SendBulkSmsResponse.FromResults(results, "batch-1");

        response.Results.Should().HaveCount(2);
        response.ProviderBatchId.Should().Be("batch-1");
        response.AllSucceeded.Should().BeFalse();
        response.AnySucceeded.Should().BeTrue();
        response.Results[0].Result.ProviderMessageId.Should().Be("id-a");
        response.Results[1].Result.FailureKind.Should().Be(SmsFailureKind.InvalidRecipient);
    }

    [Fact]
    public void should_apply_one_aggregate_outcome_to_every_recipient()
    {
        var outcome = SendSingleSmsResponse.Failed("down", SmsFailureKind.Transient);

        var response = SendBulkSmsResponse.FromAggregate([_A, _B], outcome);

        response.Results.Should().HaveCount(2);
        response.AllSucceeded.Should().BeFalse();
        response.AnySucceeded.Should().BeFalse();
        response.Results.Should().AllSatisfy(r => r.Result.Should().BeSameAs(outcome));
        response.Results.Select(r => r.Destination).Should().Equal(_A, _B);
    }

    [Fact]
    public void should_report_all_succeeded_when_every_recipient_succeeds()
    {
        var response = SendBulkSmsResponse.FromAggregate([_A, _B], SendSingleSmsResponse.Succeeded());

        response.AllSucceeded.Should().BeTrue();
        response.AnySucceeded.Should().BeTrue();
    }

    [Fact]
    public void should_reject_null_results()
    {
        var act = () => SendBulkSmsResponse.FromResults(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_reject_null_aggregate_arguments()
    {
        var actDestinations = () => SendBulkSmsResponse.FromAggregate(null!, SendSingleSmsResponse.Succeeded());
        var actOutcome = () => SendBulkSmsResponse.FromAggregate([_A], null!);

        actDestinations.Should().Throw<ArgumentNullException>();
        actOutcome.Should().Throw<ArgumentNullException>();
    }
}
