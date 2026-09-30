// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

/// <summary>
/// Pins the resilience contract of the registered CashOut client: a transient failure on the
/// disbursement POST (the money-moving call) must not be retried automatically, because Paymob's
/// <c>/disburse</c> endpoint has no idempotency key and a retry can pay out twice.
/// </summary>
public sealed class PaymobCashOutResilienceTests(PaymobCashOutFixture fixture)
    : TestBase,
        IClassFixture<PaymobCashOutFixture>
{
    [Fact]
    public async Task should_not_retry_disburse_post_on_transient_failure()
    {
        // given - every /disburse POST answers 503; a retrying pipeline would hit it more than once.
        var request = CashOutDisburseRequest.Vodafone(amount: 100m, phoneNumber: "01012345678");

        fixture
            .Server.Given(
                Request.Create().WithPath("/disburse").UsingPost().WithHeader("Authorization", "Bearer token")
            )
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("transient"));

        await using var provider = _BuildProvider();

        // when
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();
        var act = async () => await broker.DisburseAsync(request, AbortToken);

        // then - the first 503 surfaces and exactly one POST reached the wire.
        var assertion = await act.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var attempts = fixture.Server.FindLogEntries(Request.Create().WithPath("/disburse").UsingPost()).ToList();

        attempts.Should().ContainSingle("a timed-out or failed payout POST must not be retried automatically");
    }

    [Fact]
    public async Task should_retry_safe_budget_get_on_transient_failure()
    {
        // given - budget inquiry is a read; retrying it is safe and expected.
        fixture
            .Server.Given(Request.Create().WithPath("/budget/inquire/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable).WithBody("transient"));

        await using var provider = _BuildProvider();

        // when
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();
        var act = async () => await broker.GetBudgetAsync(AbortToken);

        // then - still failing after retries, but the wire saw more than one attempt.
        await act.Should().ThrowAsync<PaymobCashOutException>();

        var attempts = fixture.Server.FindLogEntries(Request.Create().WithPath("/budget/inquire/").UsingGet()).ToList();

        attempts.Should().HaveCountGreaterThan(1, "reads are safe to retry");
    }

    private ServiceProvider _BuildProvider()
    {
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

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
        services.AddSingleton(authenticator);

        return services.BuildServiceProvider();
    }
}
