// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        result.Succeeded.Should().BeFalse();
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
        result.Succeeded.Should().BeFalse();
        result.Error!.Code.Should().Be(PaymobMessageDescriptor.CashOut.InvalidRequest().Code);
    }

    private static ServiceProvider _CreateServices(string responseJson)
    {
        return _CreateServices(
            JsonSerializer.Deserialize<CashOutTransaction>(responseJson, CashOutJsonOptions.JsonOptions)!
        );
    }

    private static ServiceProvider _CreateServices(CashOutTransaction transaction)
    {
        var broker = Substitute.For<IPaymobCashOutBroker>();
        broker.DisburseAsync(Arg.Any<CashOutDisburseRequest>(), Arg.Any<CancellationToken>()).Returns(transaction);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(broker);
        services.AddPaymobServices();

        return services.BuildServiceProvider();
    }
}
