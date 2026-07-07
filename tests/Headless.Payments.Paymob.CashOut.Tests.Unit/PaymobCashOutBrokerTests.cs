// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class PaymobCashOutBrokerTests(PaymobCashOutFixture fixture) : IClassFixture<PaymobCashOutFixture>
{
    private static CancellationToken _AbortToken => TestContext.Current.CancellationToken;

    private HttpClient _CreateHttpClient()
    {
        return new HttpClient { BaseAddress = new Uri(fixture.Server.Urls[0]) };
    }

    private static IPaymobCashOutAuthenticator _CreateAuthenticator(out string token)
    {
        token = Guid.NewGuid().ToString();
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(token);
        return authenticator;
    }

    [Fact]
    public async Task get_budget_should_deserialize_current_budget_message()
    {
        // given
        const string body = """{"current_budget":"Your current budget is 888.25 LE"}""";
        var authenticator = _CreateAuthenticator(out _);

        fixture
            .Server.Given(Request.Create().WithPath("/budget/inquire/").UsingGet())
            .RespondWith(Response.Create().WithBody(body));

        // when
        using var httpClient = _CreateHttpClient();
        var broker = new PaymobCashOutBroker(httpClient, authenticator);
        var result = await broker.GetBudgetAsync(_AbortToken);

        // then
        result.CurrentBudget.Should().Be("Your current budget is 888.25 LE");
        _ = await authenticator.Received(1).GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task get_budget_should_throw_paymob_exception_when_not_success()
    {
        // given
        var authenticator = _CreateAuthenticator(out _);

        fixture
            .Server.Given(Request.Create().WithPath("/budget/inquire/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests).WithBody("throttled"));

        // when
        using var httpClient = _CreateHttpClient();
        var broker = new PaymobCashOutBroker(httpClient, authenticator);
        var invocation = FluentActions.Awaiting(() => broker.GetBudgetAsync(_AbortToken));

        // then
        var assertion = await invocation.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        assertion.Which.Body.Should().Be("throttled");
    }

    [Fact]
    public async Task get_transactions_should_deserialize_paginated_results()
    {
        // given
        const string body = """
            {
                "count": 1,
                "next": null,
                "previous": null,
                "results": [
                    {
                        "transaction_id": "txn-1",
                        "issuer": "vodafone",
                        "amount": 100.0,
                        "disbursement_status": "successful",
                        "status_code": "200"
                    }
                ]
            }
            """;
        var authenticator = _CreateAuthenticator(out _);

        fixture
            .Server.Given(Request.Create().WithPath("/transaction/inquire/").UsingGet())
            .RespondWith(Response.Create().WithBody(body));

        // when
        using var httpClient = _CreateHttpClient();
        var broker = new PaymobCashOutBroker(httpClient, authenticator);
        var result = await broker.GetTransactionsAsync(["txn-1"], isBankTransactions: false, page: 1, _AbortToken);

        // then
        result.Count.Should().Be(1);
        result.Results.Should().ContainSingle();
        result.Results[0].TransactionId.Should().Be("txn-1");
        result.Results[0].IsSuccess().Should().BeTrue();
        _ = await authenticator.Received(1).GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task get_transactions_should_throw_when_ids_empty()
    {
        // given
        using var httpClient = _CreateHttpClient();
        var broker = new PaymobCashOutBroker(httpClient, authenticator: null!);

        // when
        var invocation = FluentActions.Awaiting(() =>
            broker.GetTransactionsAsync([], isBankTransactions: false, page: 1, _AbortToken)
        );

        // then
        await invocation.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task get_transactions_should_throw_when_page_not_positive()
    {
        // given
        using var httpClient = _CreateHttpClient();
        var broker = new PaymobCashOutBroker(httpClient, authenticator: null!);

        // when
        var invocation = FluentActions.Awaiting(() =>
            broker.GetTransactionsAsync(["txn-1"], isBankTransactions: false, page: 0, _AbortToken)
        );

        // then
        await invocation.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
