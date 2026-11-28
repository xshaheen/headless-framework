// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Internal;
using Headless.Payments.Paymob.Resources;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PaymobCashOutServiceTests : TestBase
{
    private const string _BudgetExceeded =
        """{"transaction_id":"tx-budget","issuer":"vodafone","msisdn":"01012345678","amount":150,"disbursement_status":"failed","status_code":"400","status_description":"the amount to be disbursed exceeds you budget limit"}""";

    private const string _FieldError =
        """{"transaction_id":"tx-field","issuer":"vodafone","msisdn":"01012345678","amount":150,"disbursement_status":"failed","status_code":"400","status_description":{"msisdn":["Phonenumbers entered are incorrect"]}}""";

    [Fact]
    public async Task should_report_a_provider_failure_when_paymob_says_the_budget_is_exceeded()
    {
        // given: the description arrives as JSON text, the way the broker deserializes Paymob's response
        await using var services = _CreateServices(_BudgetExceeded);
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then: an exhausted payout wallet is the operator's problem, not an invalid request from the caller
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.ProviderConnectionFailed().Code);
    }

    [Fact]
    public async Task should_report_a_provider_failure_when_a_hand_built_transaction_says_the_budget_is_exceeded()
    {
        // given
        var transaction = new CashOutTransaction
        {
            Issuer = "vodafone",
            DisbursementStatus = "failed",
            StatusCode = "400",
            StatusDescription = "The amount to be disbursed exceeds you budget limit",
        };
        await using var services = _CreateServices(transaction);
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.ProviderConnectionFailed().Code);
    }

    [Fact]
    public async Task should_report_an_invalid_request_when_paymob_rejects_a_field()
    {
        // given
        await using var services = _CreateServices(_FieldError);
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.InvalidRequest().Code);
    }

    [Fact]
    public async Task should_report_outcome_unknown_and_keep_the_body_when_the_broker_cannot_tell_if_paymob_paid()
    {
        // given
        await using var services = _CreateServices(
            PaymobCashOutResult.Unknown<CashOutTransaction>(
                new PaymobCashOutResultError("5xx", HttpStatusCode.BadGateway, Body: "upstream")
            )
        );
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then - the caller must not see this as a rejection it can retry
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.IsRejected.Should().BeFalse();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.OutcomeUnknown().Code);
        result.Response.Should().Be("upstream");
    }

    [Fact]
    public async Task should_report_a_provider_failure_when_the_broker_rejects_without_a_body()
    {
        // given
        await using var services = _CreateServices(
            PaymobCashOutResult.Rejected<CashOutTransaction>(
                new PaymobCashOutResultError("4xx", HttpStatusCode.Unauthorized)
            )
        );
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.ProviderConnectionFailed().Code);
    }

    [Fact]
    public async Task should_report_success_with_status_when_paymob_accepts_a_pending_payout()
    {
        // given
        await using var services = _CreateServices(
            """{"transaction_id":"tx-ok","issuer":"vodafone","amount":150,"disbursement_status":"pending","status_code":"8000"}"""
        );
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(new VodafoneCashOutRequest(150m, "01012345678"), AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Data!.TransactionId.Should().Be("tx-ok");
        result.Data.Status.Should().Be(CashOutResponseStatus.Pending);
        result.Response.Should().Contain("tx-ok");
    }

    [Theory]
    [InlineData(PaymobCashOutOutcome.Rejected)]
    [InlineData(PaymobCashOutOutcome.Unknown)]
    public async Task should_forward_the_outcome_of_a_kiosk_payout(PaymobCashOutOutcome outcome)
    {
        // given
        var error = new PaymobCashOutResultError("failed", HttpStatusCode.BadGateway);
        await using var services = _CreateServices(
            outcome is PaymobCashOutOutcome.Rejected
                ? PaymobCashOutResult.Rejected<CashOutTransaction>(error)
                : PaymobCashOutResult.Unknown<CashOutTransaction>(error)
        );
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(_KioskRequest(), AbortToken);

        // then
        result.Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task should_report_outcome_unknown_when_paymob_accepts_a_kiosk_payout_without_a_billing_reference()
    {
        // given - the payout is committed, but the recipient has nothing to collect it with
        await using var services = _CreateServices(
            """{"transaction_id":"tx-aman","issuer":"aman","amount":150,"disbursement_status":"successful","status_code":"200"}"""
        );
        var service = services.GetRequiredService<ICashOutService>();

        // when
        var result = await service.DisburseAsync(_KioskRequest(), AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.OutcomeUnknown().Code);
    }

    private static KioskCashOutRequest _KioskRequest()
    {
        return new KioskCashOutRequest(150m, "01012345678", "First", "Last", "first@example.com");
    }

    private static ServiceProvider _CreateServices(string responseJson)
    {
        return _CreateServices(
            JsonSerializer.Deserialize<CashOutTransaction>(responseJson, CashOutJsonOptions.JsonOptions)!
        );
    }

    private static ServiceProvider _CreateServices(CashOutTransaction transaction)
    {
        var outcome =
            transaction.IsSuccess() || transaction.IsPending()
                ? PaymobCashOutResult.Succeeded(transaction)
                : PaymobCashOutResult.Rejected(
                    new PaymobCashOutResultError("rejected", HttpStatusCode.OK),
                    transaction
                );

        return _CreateServices(outcome);
    }

    private static ServiceProvider _CreateServices(PaymobCashOutResult<CashOutTransaction> outcome)
    {
        var broker = Substitute.For<IPaymobCashOutBroker>();
        broker.DisburseAsync(Arg.Any<CashOutDisburseRequest>(), Arg.Any<CancellationToken>()).Returns(outcome);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(broker);
        services.AddPaymobServices();

        return services.BuildServiceProvider();
    }
}
