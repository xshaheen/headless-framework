// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Encodings.Web;
using AutoFixture;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Testing.Tests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class PaymobCashOutBrokerTests(PaymobCashOutFixture fixture)
    : TestBase,
        IClassFixture<PaymobCashOutFixture>
{
    // Mirrors the wire shape produced by the package-internal CashOutJsonOptions
    private static readonly JsonSerializerOptions _WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task should_post_disburse_with_bearer_token_and_return_transaction()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 150.25m, phoneNumber: "01012345678");
        var requestJson = JsonSerializer.Serialize(request, _WireOptions);
        var transactionId = fixture.AutoFixture.Create<string>();
        var responseJson =
            $$"""{"transaction_id":"{{transactionId}}","issuer":"vodafone","msisdn":"01012345678","amount":150.25,"disbursement_status":"success","status_code":"200"}""";

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/disburse")
                    .UsingPost()
                    .WithHeader("Authorization", $"Bearer {token}")
                    .WithBody(requestJson)
            )
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.TransactionId.Should().Be(transactionId);
        result.Amount.Should().Be(150.25m);
        result.IsSuccess().Should().BeTrue();
        result.IsFailed().Should().BeFalse();
        result.IsPending().Should().BeFalse();
    }

    [Fact]
    public async Task should_read_disburse_response_when_paymob_uses_string_numbers_and_object_description()
    {
        // given - Paymob quirks: numeric fields as strings, status_description as object, unknown fields
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Etisalat(amount: 75m, phoneNumber: "01112345678");
        const string responseJson =
            """{"transaction_id":"tx-quirks","issuer":"etisalat","amount":"75.5","disbursement_status":"failed","status_code":"400","status_description":{"msisdn":["invalid"]},"unknown_field":"kept"}""";

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.TransactionId.Should().Be("tx-quirks");
        result.Amount.Should().Be(75.5m);
        result.IsRequestValidationError().Should().BeTrue();
        result.StatusDescription.Should().NotBeNull();
        result.ExtensionData.Should().ContainKey("unknown_field");
    }

    [Fact]
    public async Task should_throw_paymob_cash_out_exception_with_status_and_body_when_disburse_fails()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");
        var errorBody = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError).WithBody(errorBody));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var act = () => broker.DisburseAsync(request, AbortToken);

        // then
        var assertion = await act.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        assertion.Which.Body.Should().Be(errorBody);
        assertion.Which.Message.Should().Be("Paymob Cash Out - Http request failed with status code (500).");
    }

    [Fact]
    public async Task should_get_budget_with_bearer_token_and_return_typed_budget()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        const string budgetJson = """{"current_budget":"Your current budget is 888.25 LE"}""";

        fixture
            .Server.Given(
                Request.Create().WithPath("/budget/inquire/").UsingGet().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithBody(budgetJson));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var result = await broker.GetBudgetAsync(AbortToken);

        // then
        result.CurrentBudget.Should().Be("Your current budget is 888.25 LE");
    }

    [Fact]
    public async Task should_throw_paymob_cash_out_exception_with_status_and_body_when_budget_fails()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();

        fixture
            .Server.Given(Request.Create().WithPath("/budget/inquire/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests).WithBody("throttled"));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var act = () => broker.GetBudgetAsync(AbortToken);

        // then
        var assertion = await act.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        assertion.Which.Body.Should().Be("throttled");
    }

    [Fact]
    public async Task should_get_transactions_with_page_query_and_serialized_request_body()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        string[] ids = ["tx-1", "tx-2"];
        var expectedBody = JsonSerializer.Serialize(
            new CashOutGetTransactionsRequest { TransactionsIds = ids, IsBankTransactions = true },
            _WireOptions
        );
        const string responseBody = """
            {
                "count": 2,
                "next": null,
                "previous": null,
                "results": [
                    {
                        "transaction_id": "tx-1",
                        "issuer": "vodafone",
                        "amount": 100.0,
                        "disbursement_status": "successful",
                        "status_code": "200"
                    },
                    {
                        "transaction_id": "tx-2",
                        "issuer": "vodafone",
                        "amount": 55.5,
                        "disbursement_status": "failed",
                        "status_code": "400"
                    }
                ]
            }
            """;

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/transaction/inquire/")
                    .UsingGet()
                    .WithParam("page", "3")
                    .WithHeader("Authorization", $"Bearer {token}")
                    .WithBody(expectedBody)
            )
            .RespondWith(Response.Create().WithBody(responseBody));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);
        var result = await broker.GetTransactionsAsync(ids, isBankTransactions: true, page: 3, AbortToken);

        // then
        result.Count.Should().Be(2);
        result.Results.Should().HaveCount(2);
        result.Results[0].TransactionId.Should().Be("tx-1");
        result.Results[0].IsSuccess().Should().BeTrue();
        result.Results[1].TransactionId.Should().Be("tx-2");
    }

    [Fact]
    public async Task should_throw_argument_exception_without_calling_api_when_transactions_ids_empty()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);

        // when
        var act = () => broker.GetTransactionsAsync([], isBankTransactions: false, page: 1, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _ = await authenticator.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_throw_argument_out_of_range_exception_when_page_not_positive()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator);

        // when
        var act = () => broker.GetTransactionsAsync(["tx-1"], isBankTransactions: false, page: 0, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _ = await authenticator.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    private (IPaymobCashOutAuthenticator authenticator, string token) _SetupAuthenticator()
    {
        var token = fixture.AutoFixture.Create<string>();
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(token);

        return (authenticator, token);
    }
}
