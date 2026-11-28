// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Encodings.Web;
using Headless.Payments.Paymob.CashOut;
using Headless.Testing.Tests;
using Polly.CircuitBreaker;
using Polly.Timeout;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed class PaymobCashOutBrokerTests(PaymobCashOutFixture fixture)
    : TestBase,
        IClassFixture<PaymobCashOutFixture>
{
    protected override Faker Faker { get; set; } = CashOutTestData.CreateFaker();

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
        var transactionId = CashOutTestData.Text(Faker);
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
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Succeeded);
        result.Error.Should().BeNull();
        result.Value!.TransactionId.Should().Be(transactionId);
        result.Value.Amount.Should().Be(150.25m);
        result.Value.IsSuccess().Should().BeTrue();
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
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then - a "failed" disbursement is a definite rejection that still carries Paymob's answer
        result.Outcome.Should().Be(PaymobCashOutOutcome.Rejected);
        result.Error!.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Value!.TransactionId.Should().Be("tx-quirks");
        result.Value.Amount.Should().Be(75.5m);
        result.Value.IsRequestValidationError().Should().BeTrue();
        result.Value.StatusDescription.Should().NotBeNull();
        result.Value.ExtensionData.Should().ContainKey("unknown_field");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task should_return_rejected_with_status_and_body_when_disburse_answers_4xx(HttpStatusCode status)
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");
        var errorBody = CashOutTestData.Text(Faker);

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithStatusCode(status).WithBody(errorBody));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Rejected);
        result.Error!.StatusCode.Should().Be(status);
        result.Error.Body.Should().Be(errorBody);
        result.Error.Message.Should().NotContain(errorBody, "the body may carry recipient data and stays out of logs");
        result.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task should_return_unknown_with_status_and_body_when_disburse_answers_5xx(HttpStatusCode status)
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");
        var errorBody = CashOutTestData.Text(Faker);

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithStatusCode(status).WithBody(errorBody));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then - Paymob may have paid before failing, so the payout is unresolved, not rejected
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.IsRejected.Should().BeFalse();
        result.Error!.StatusCode.Should().Be(status);
        result.Error.Body.Should().Be(errorBody);
    }

    [Theory]
    [MemberData(nameof(FaultsAfterSend))]
    public async Task should_return_unknown_when_disburse_times_out_or_loses_the_response(TransportFault kind)
    {
        // given
        var fault = _Fault(kind);
        var (authenticator, _) = _SetupAuthenticator();
        using var handler = new ThrowingHandler(fault);
        using var httpClient = new HttpClient(handler);
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");

        // when
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.Error!.StatusCode.Should().BeNull();
        result.Error.Exception.Should().BeSameAs(fault);
    }

    public enum TransportFault
    {
        HttpClientTimeout,
        PipelineTimeout,
        ConnectionReset,
        ResponseStreamClosed,
    }

    public static readonly TheoryData<TransportFault> FaultsAfterSend =
    [
        TransportFault.HttpClientTimeout,
        TransportFault.PipelineTimeout,
        TransportFault.ConnectionReset,
        TransportFault.ResponseStreamClosed,
    ];

    private static Exception _Fault(TransportFault kind)
    {
        return kind switch
        {
            // HttpClient.Timeout surfaces as a TaskCanceledException wrapping a TimeoutException.
            TransportFault.HttpClientTimeout => new TaskCanceledException("timed out", new TimeoutException()),
            TransportFault.PipelineTimeout => new TimeoutRejectedException("resilience pipeline timeout"),
            TransportFault.ConnectionReset => new HttpRequestException("connection reset"),
            TransportFault.ResponseStreamClosed => new IOException("response stream closed"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Fact]
    public async Task should_return_rejected_when_the_circuit_breaker_refuses_to_send_the_disbursement()
    {
        // given - an open circuit rejects before the request reaches the network
        var (authenticator, _) = _SetupAuthenticator();
        var fault = new BrokenCircuitException("circuit open");
        using var handler = new ThrowingHandler(fault);
        using var httpClient = new HttpClient(handler);
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");

        // when
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Rejected);
        result.Error!.Exception.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task should_return_unknown_when_disburse_reports_an_unrecognized_status()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");
        const string responseJson =
            """{"transaction_id":"tx-odd","issuer":"vodafone","amount":10,"disbursement_status":"processing","status_code":"7000"}""";

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithBody(responseJson));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.Value!.TransactionId.Should().Be("tx-odd");
    }

    [Fact]
    public async Task should_return_unknown_when_disburse_answers_2xx_with_an_unreadable_body()
    {
        // given
        var (authenticator, token) = _SetupAuthenticator();
        var request = CashOutDisburseRequest.Vodafone(amount: 10m, phoneNumber: "01099999999");

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", $"Bearer {token}")
            )
            .RespondWith(Response.Create().WithBody("<html>gateway</html>"));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(request, AbortToken);

        // then
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.Error!.Exception.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task should_return_rejected_without_sending_when_authentication_fails()
    {
        // given
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator
            .GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashOutResult.Unknown<string>(
                    new PaymobCashOutResultError("token timeout", HttpStatusCode.GatewayTimeout)
                )
            );
        using var handler = new ThrowingHandler(new InvalidOperationException("must not be sent"));
        using var httpClient = new HttpClient(handler);

        // when
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.DisburseAsync(CashOutDisburseRequest.Vodafone(10m, "01099999999"), AbortToken);

        // then - no payout request left, so the payout is a definite rejection even though the token call was unknown
        result.Outcome.Should().Be(PaymobCashOutOutcome.Rejected);
        result.Error!.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        result.Error.Message.Should().Contain("not sent");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_operation_canceled_without_sending_when_canceled_before_disburse()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        using var handler = new ThrowingHandler(new InvalidOperationException("must not be sent"));
        using var httpClient = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var act = () => broker.DisburseAsync(CashOutDisburseRequest.Vodafone(10m, "01099999999"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_argument_null_exception_when_disburse_request_is_null()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);

        // when
        var act = () => broker.DisburseAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
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
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetBudgetAsync(AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        result.Value!.CurrentBudget.Should().Be("Your current budget is 888.25 LE");
    }

    [Fact]
    public async Task should_return_rejected_with_status_and_body_when_budget_answers_4xx()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();

        fixture
            .Server.Given(Request.Create().WithPath("/budget/inquire/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests).WithBody("throttled"));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetBudgetAsync(AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        result.Error.Body.Should().Be("throttled");
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
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetTransactionsAsync(ids, isBankTransactions: true, page: 3, AbortToken);

        // then - an inquiry succeeds as a call even when it reports a failed payout
        result.IsSucceeded.Should().BeTrue();
        result.Value!.Count.Should().Be(2);
        result.Value.Results.Should().HaveCount(2);
        result.Value.Results[0].TransactionId.Should().Be("tx-1");
        result.Value.Results[0].IsSuccess().Should().BeTrue();
        result.Value.Results[1].TransactionId.Should().Be("tx-2");
    }

    [Fact]
    public async Task should_throw_argument_exception_without_calling_api_when_transactions_ids_empty()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);

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
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);

        // when
        var act = () => broker.GetTransactionsAsync(["tx-1"], isBankTransactions: false, page: 0, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _ = await authenticator.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_unknown_when_transactions_inquiry_answers_5xx()
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();

        fixture
            .Server.Given(Request.Create().WithPath("/transaction/inquire/").UsingGet().WithParam("page", "9"))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.BadGateway));

        // when
        var broker = new PaymobCashOutBroker(fixture.HttpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetTransactionsAsync(["tx-1"], isBankTransactions: false, page: 9, AbortToken);

        // then
        result.IsUnknown.Should().BeTrue();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    private (IPaymobCashOutAuthenticator authenticator, string token) _SetupAuthenticator()
    {
        var token = CashOutTestData.Text(Faker);
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(PaymobCashOutResult.Succeeded(token));

        return (authenticator, token);
    }

    [Fact]
    public async Task should_return_rejected_without_sending_reads_when_authentication_fails()
    {
        // given
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator
            .GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashOutResult.Rejected<string>(
                    new PaymobCashOutResultError("bad credentials", HttpStatusCode.Unauthorized)
                )
            );
        using var handler = new ThrowingHandler(new InvalidOperationException("must not be sent"));
        using var httpClient = new HttpClient(handler);
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);

        // when
        var budget = await broker.GetBudgetAsync(AbortToken);
        var transactions = await broker.GetTransactionsAsync(
            ["1"],
            isBankTransactions: false,
            cancellationToken: AbortToken
        );

        // then
        budget.IsRejected.Should().BeTrue();
        budget.Error!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        transactions.IsRejected.Should().BeTrue();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task should_return_unknown_when_the_disburse_response_stream_breaks(HttpStatusCode status)
    {
        // given
        var (authenticator, _) = _SetupAuthenticator();
        using var handler = new BrokenBodyHandler(status);
        using var httpClient = new HttpClient(handler);
        var broker = new PaymobCashOutBroker(httpClient, authenticator, fixture.OptionsAccessor);

        // when
        var result = await broker.DisburseAsync(CashOutDisburseRequest.Vodafone(10m, "01099999999"), AbortToken);

        // then - the payout reached Paymob, so a lost answer cannot be read as a rejection
        // HttpClient buffers the body before it returns, so the break surfaces as a fault of the send itself.
        result.Outcome.Should().Be(PaymobCashOutOutcome.Unknown);
        result.Error!.Exception.Should().NotBeNull();
    }

    private sealed class BrokenBodyHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new HttpResponseMessage(status) { Content = new BrokenContent() });
        }
    }

    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return Task.FromException(new IOException("response stream closed"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }
    }

    private sealed class ThrowingHandler(Exception fault) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;

            return Task.FromException<HttpResponseMessage>(fault);
        }
    }
}
