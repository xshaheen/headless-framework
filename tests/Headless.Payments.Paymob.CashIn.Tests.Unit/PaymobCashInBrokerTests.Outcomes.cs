// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json.Nodes;
using Headless.Payments.Paymob.CashIn;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Tests;

// Pins how every money-moving call reports what Paymob did. A definite refusal is Rejected; anything that leaves the
// request possibly applied (a 5xx, a timeout, a lost response) is Unknown, so a caller cannot retry it into a double
// charge, refund, or void.
public sealed partial class PaymobCashInBrokerTests
{
    public enum MoneyCall
    {
        CreateOrder,
        RequestPaymentKey,
        CreateIntention,
        WalletPay,
        KioskPay,
        CashCollectionPay,
        SavedTokenPay,
        Refund,
        Void,
    }

    public static readonly TheoryData<MoneyCall> MoneyCalls =
    [
        MoneyCall.CreateOrder,
        MoneyCall.RequestPaymentKey,
        MoneyCall.CreateIntention,
        MoneyCall.WalletPay,
        MoneyCall.KioskPay,
        MoneyCall.CashCollectionPay,
        MoneyCall.SavedTokenPay,
        MoneyCall.Refund,
        MoneyCall.Void,
    ];

    public static readonly TheoryData<MoneyCall, HttpStatusCode> MoneyCallsWith4xx = _Cross(
        HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.NotFound,
        HttpStatusCode.UnprocessableEntity
    );

    public static readonly TheoryData<MoneyCall, HttpStatusCode> MoneyCallsWith5xx = _Cross(
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    );

    [Theory]
    [MemberData(nameof(MoneyCallsWith4xx))]
    public async Task should_return_rejected_with_status_and_body_when_money_call_answers_4xx(
        MoneyCall call,
        HttpStatusCode status
    )
    {
        // given
        var body = CashInTestData.Text(Faker);
        using var handler = new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Rejected);
        error!.StatusCode.Should().Be(status);
        error.Body.Should().Be(body);
        error.Message.Should().NotContain(body, "the body may carry customer data and stays out of logs");
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(MoneyCallsWith5xx))]
    public async Task should_return_unknown_with_status_and_body_when_money_call_answers_5xx(
        MoneyCall call,
        HttpStatusCode status
    )
    {
        // given
        var body = CashInTestData.Text(Faker);
        using var handler = new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then - Paymob may have applied the request before failing
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.StatusCode.Should().Be(status);
        error.Body.Should().Be(body);
        handler.Calls.Should().Be(1, "a POST is never retried");
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_return_unknown_when_money_call_times_out(MoneyCall call)
    {
        // given - HttpClient.Timeout surfaces as a TaskCanceledException wrapping a TimeoutException
        var fault = new TaskCanceledException("timed out", new TimeoutException());
        using var handler = new StubHandler(_ => throw fault);

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.StatusCode.Should().BeNull();
        error.Exception.Should().BeSameAs(fault);
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_return_unknown_when_the_resilience_pipeline_times_out_a_money_call(MoneyCall call)
    {
        // given
        using var handler = new StubHandler(_ => throw new TimeoutRejectedException("pipeline timeout"));

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.Exception.Should().BeOfType<TimeoutRejectedException>();
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_return_unknown_when_the_connection_drops_during_a_money_call(MoneyCall call)
    {
        // given
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.Exception.Should().BeOfType<HttpRequestException>();
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_return_rejected_when_the_circuit_breaker_refuses_to_send_a_money_call(MoneyCall call)
    {
        // given - an open circuit rejects before the request reaches the network
        using var handler = new StubHandler(_ => throw new BrokenCircuitException("circuit open"));

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Rejected);
        error!.Exception.Should().BeOfType<BrokenCircuitException>();
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_return_unknown_when_a_money_call_answers_2xx_with_an_unreadable_body(MoneyCall call)
    {
        // given
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>gateway</html>"),
        });

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.Exception.Should().BeOfType<JsonException>();
    }

    [Theory]
    [MemberData(nameof(MoneyCalls))]
    public async Task should_throw_operation_canceled_without_sending_when_canceled_before_a_money_call(MoneyCall call)
    {
        // given
        using var handler = new StubHandler(_ => throw new InvalidOperationException("must not be sent"));
        var canceled = new CancellationToken(canceled: true);

        // when
#pragma warning disable CA2025 // False positive: the assertion below awaits the task before the handler is disposed.
        var act = () => _InvokeAsync(call, handler, canceled);
#pragma warning restore CA2025

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(MoneyCall.CreateOrder)]
    [InlineData(MoneyCall.RequestPaymentKey)]
    public async Task should_return_rejected_without_sending_when_authentication_fails(MoneyCall call)
    {
        // given - the token call itself was unknown, but the money call never left
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator
            .GetAuthenticationTokenAsync(Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Unknown<string>(
                    new PaymobCashInResultError("token timeout", HttpStatusCode.GatewayTimeout)
                )
            );
        using var handler = new StubHandler(_ => throw new InvalidOperationException("must not be sent"));

        // when
        var (outcome, error, _) = await _InvokeAsync(call, handler, AbortToken, authenticator);

        // then
        outcome.Should().Be(PaymobCashInOutcome.Rejected);
        error!.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        error.Message.Should().Contain("not sent");
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(MoneyCall.WalletPay, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.WalletPay, false, true, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.WalletPay, true, false, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.KioskPay, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.KioskPay, false, true, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.CashCollectionPay, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.CashCollectionPay, false, true, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.SavedTokenPay, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.SavedTokenPay, false, true, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.SavedTokenPay, true, false, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.Refund, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.Refund, true, false, PaymobCashInOutcome.Succeeded)]
    [InlineData(MoneyCall.Void, false, false, PaymobCashInOutcome.Rejected)]
    [InlineData(MoneyCall.Void, false, true, PaymobCashInOutcome.Succeeded)]
    public async Task should_classify_the_transaction_flags_paymob_answers_with_on_2xx(
        MoneyCall call,
        bool success,
        bool pending,
        PaymobCashInOutcome expected
    )
    {
        // given - Paymob reports a declined charge, refund, or void as 200 with both flags false
        var body = _TransactionBody(call, success, pending);
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        });

        // when
        var (outcome, error, hasValue) = await _InvokeAsync(call, handler);

        // then
        outcome.Should().Be(expected);
        hasValue.Should().BeTrue("the parsed answer is kept on every outcome");
        (error is null).Should().Be(expected is PaymobCashInOutcome.Succeeded);
    }

    [Fact]
    public async Task should_return_rejected_with_404_when_get_order_finds_no_order()
    {
        // given
        var (authenticator, _) = _SetupGentAuthenticationToken();
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);

        // when
        var broker = new PaymobCashInBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetOrderAsync("404", AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, PaymobCashInOutcome.Succeeded)]
    [InlineData(HttpStatusCode.Forbidden, PaymobCashInOutcome.Rejected)]
    [InlineData(HttpStatusCode.BadGateway, PaymobCashInOutcome.Unknown)]
    public async Task should_map_the_status_of_a_transactions_page_read(
        HttpStatusCode status,
        PaymobCashInOutcome expected
    )
    {
        // given
        var (authenticator, _) = _SetupGentAuthenticationToken();
        using var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"count":0,"next":null,"previous":null,"results":[]}"""),
        });
        using var httpClient = new HttpClient(handler);

        // when
        var broker = new PaymobCashInBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetTransactionsPageAsync(cancellationToken: AbortToken);

        // then
        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task should_return_rejected_without_sending_a_read_when_authentication_fails()
    {
        // given
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator
            .GetAuthenticationTokenAsync(Arg.Any<CancellationToken>())
            .Returns(
                PaymobCashInResult.Rejected<string>(new PaymobCashInResultError("bad key", HttpStatusCode.Forbidden))
            );
        using var handler = new StubHandler(_ => throw new InvalidOperationException("must not be sent"));
        using var httpClient = new HttpClient(handler);

        // when
        var broker = new PaymobCashInBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var result = await broker.GetTransactionAsync("1", AbortToken);

        // then
        result.IsRejected.Should().BeTrue();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task should_return_unknown_when_the_response_stream_breaks(HttpStatusCode status)
    {
        // given
        using var handler = new StubHandler(_ => new HttpResponseMessage(status) { Content = new BrokenContent() });

        // when
        var (outcome, error, _) = await _InvokeAsync(MoneyCall.Refund, handler);

        // then
        // HttpClient buffers the body before it returns, so the break surfaces as a fault of the send itself.
        outcome.Should().Be(PaymobCashInOutcome.Unknown);
        error!.Exception.Should().NotBeNull();
    }

    private async Task<(PaymobCashInOutcome Outcome, PaymobCashInResultError? Error, bool HasValue)> _InvokeAsync(
        MoneyCall call,
        StubHandler handler,
        CancellationToken? cancellationToken = null,
        IPaymobCashInAuthenticator? authenticator = null
    )
    {
        authenticator ??= _SetupGentAuthenticationToken().authenticator;
        var token = cancellationToken ?? AbortToken;
        using var httpClient = new HttpClient(handler);
        var broker = new PaymobCashInBroker(httpClient, authenticator, fixture.OptionsAccessor);
        var paymentKey = CashInTestData.Text(Faker);

        return call switch
        {
            MoneyCall.CreateOrder => _Unpack(
                await broker.CreateOrderAsync(CashInCreateOrderRequest.CreateOrder(1000), token)
            ),
            MoneyCall.RequestPaymentKey => _Unpack(
                await broker.RequestPaymentKeyAsync(CashInTestData.PaymentKeyRequest(Faker, 3600), token)
            ),
            MoneyCall.CreateIntention => _Unpack(await broker.CreateIntentionAsync(_IntentionRequest(), token)),
            MoneyCall.WalletPay => _Unpack(await broker.CreateWalletPayAsync(paymentKey, "01012345678", token)),
            MoneyCall.KioskPay => _Unpack(await broker.CreateKioskPayAsync(paymentKey, token)),
            MoneyCall.CashCollectionPay => _Unpack(await broker.CreateCashCollectionPayAsync(paymentKey, token)),
            MoneyCall.SavedTokenPay => _Unpack(await broker.CreateSavedTokenPayAsync(paymentKey, "card-token", token)),
            MoneyCall.Refund => _Unpack(await broker.RefundTransactionAsync(new("192", "1000"), token)),
            MoneyCall.Void => _Unpack(await broker.VoidTransactionAsync(new("192"), token)),
            _ => throw new ArgumentOutOfRangeException(nameof(call)),
        };
    }

    private static (PaymobCashInOutcome, PaymobCashInResultError?, bool) _Unpack<T>(PaymobCashInResult<T> result)
    {
        return (result.Outcome, result.Error, result.Value is not null);
    }

    private static CashInCreateIntentionRequest _IntentionRequest()
    {
        return new CashInCreateIntentionRequest
        {
            Amount = 1000,
            Currency = "EGP",
            PaymentMethods = [4534567],
            Items = [],
            BillingData = new CashInCreateIntentionRequestBillingData
            {
                FirstName = "First",
                LastName = "Last",
                Email = "first@example.com",
                PhoneNumber = "01012345678",
            },
        };
    }

    private string _TransactionBody(MoneyCall call, bool success, bool pending)
    {
        JsonNode node = call switch
        {
            MoneyCall.WalletPay => JsonSerializer.SerializeToNode(CashInTestData.WalletPayResponse(Faker))!,
            MoneyCall.KioskPay => JsonSerializer.SerializeToNode(CashInTestData.KioskPayResponse(Faker))!,
            MoneyCall.CashCollectionPay => JsonSerializer.SerializeToNode(
                CashInTestData.CashCollectionPayResponse(Faker)
            )!,
            MoneyCall.SavedTokenPay => JsonSerializer.SerializeToNode(CashInTestData.SavedTokenPayResponse(Faker))!,
            MoneyCall.Refund or MoneyCall.Void => JsonNode.Parse(CashInTestData.CallbackTransactionJson)!,
            _ => throw new ArgumentOutOfRangeException(nameof(call)),
        };

        // The cash-collection and saved-token responses carry their flags as strings.
        var asText = call is MoneyCall.CashCollectionPay or MoneyCall.SavedTokenPay;
        node["success"] = asText ? JsonValue.Create(success ? "true" : "false") : JsonValue.Create(success);
        node["pending"] = asText ? JsonValue.Create(pending ? "true" : "false") : JsonValue.Create(pending);

        return node.ToJsonString();
    }

    private static TheoryData<MoneyCall, HttpStatusCode> _Cross(params HttpStatusCode[] statuses)
    {
        var data = new TheoryData<MoneyCall, HttpStatusCode>();
        var calls = Enum.GetValues<MoneyCall>();

        // Each call meets every status once across the set without multiplying the rows.
        for (var i = 0; i < calls.Length; i++)
        {
            data.Add(calls[i], statuses[i % statuses.Length]);
        }

        return data;
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;

            try
            {
                return Task.FromResult(respond(request));
            }
            catch (Exception e)
            {
                return Task.FromException<HttpResponseMessage>(e);
            }
        }
    }
}
