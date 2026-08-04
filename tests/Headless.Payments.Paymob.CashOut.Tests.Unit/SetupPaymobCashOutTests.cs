// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Covers the documented registration path — options only, no <c>configureClient</c> — where the
/// typed <c>HttpClient</c> has no <c>BaseAddress</c>. Every broker call must still resolve against
/// <c>PaymobCashOutOptions.ApiBaseUrl</c>.
/// </summary>
public sealed class SetupPaymobCashOutTests(PaymobCashOutFixture fixture)
    : TestBase,
        IClassFixture<PaymobCashOutFixture>
{
    private const string _Token = "registration-access-token";

    [Fact]
    public async Task should_disburse_against_api_base_url_when_broker_resolved_from_registration()
    {
        // given
        await using var provider = _BuildProvider();
        var request = CashOutDisburseRequest.Vodafone(amount: 25m, phoneNumber: "01012345678");

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {_Token}")
            )
            .RespondWith(
                Response
                    .Create()
                    .WithBody(
                        """{"transaction_id":"tx-di","issuer":"vodafone","amount":25.0,"disbursement_status":"successful","status_code":"200"}"""
                    )
            );

        // when
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.TransactionId.Should().Be("tx-di");
    }

    [Fact]
    public void should_not_set_base_address_on_the_registered_client()
    {
        // The broker must compose absolute URLs itself; this pins the precondition the other tests rely on.
        using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(SetupPaymobCashOut.HttpClientName);

        client.BaseAddress.Should().BeNull();
    }

    [Fact]
    public async Task should_query_budget_against_api_base_url_when_broker_resolved_from_registration()
    {
        // given
        await using var provider = _BuildProvider();

        fixture
            .Server.Given(
                Request.Create().WithPath("/budget/inquire/").UsingGet().WithHeader("Authorization", $"Bearer {_Token}")
            )
            .RespondWith(Response.Create().WithBody("""{"current_budget":"Your current budget is 12.5 LE"}"""));

        // when
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();
        var result = await broker.GetBudgetAsync(AbortToken);

        // then
        result.CurrentBudget.Should().Be("Your current budget is 12.5 LE");
    }

    [Fact]
    public async Task should_query_transactions_against_api_base_url_when_broker_resolved_from_registration()
    {
        // given
        await using var provider = _BuildProvider();

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/transaction/inquire/")
                    .UsingGet()
                    .WithParam("page", "2")
                    .WithHeader("Authorization", $"Bearer {_Token}")
            )
            .RespondWith(Response.Create().WithBody("""{"count":0,"next":null,"previous":null,"results":[]}"""));

        // when
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();
        var result = await broker.GetTransactionsAsync(["tx-1"], isBankTransactions: false, page: 2, AbortToken);

        // then
        result.Count.Should().Be(0);
    }

    private ServiceProvider _BuildProvider()
    {
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_Token);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ApiBaseUrl"] = fixture.Server.Urls[0],
                    ["UserName"] = "username",
                    ["Password"] = "password",
                    ["ClientId"] = "client_id",
                    ["ClientSecret"] = "client_secret",
                }
            )
            .Build();

        var services = new ServiceCollection();

        services.AddPaymobCashOut(configuration);
        // Registered after AddPaymobCashOut so the last registration wins and no real token call is made.
        services.AddSingleton(authenticator);

        return services.BuildServiceProvider();
    }
}
