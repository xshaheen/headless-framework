// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net;
using Framework.Payments.Paymob.CashIn;

namespace Tests;

public partial class PaymobCashInBrokerTests : IClassFixture<PaymobCashInFixture>
{
    private readonly PaymobCashInFixture _fixture;
    private static readonly Faker _Faker = new();

    public PaymobCashInBrokerTests(PaymobCashInFixture fixture) => _fixture = fixture;

    private (IPaymobCashInAuthenticator authenticator, string token) _SetupGentAuthenticationToken()
    {
        var token = _fixture.AutoFixture.Create<string>();
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator.GetAuthenticationTokenAsync().Returns(token);
        return (authenticator, token);
    }

    private static async Task _ShouldThrowPaymobRequestException<T>(
        Func<Task<T>> invocation,
        HttpStatusCode statusCode,
        string? body
    )
    {
        var assertions = await invocation.Should().ThrowAsync<PaymobRequestException>();
        assertions.Which.StatusCode.Should().Be(statusCode);
        assertions
            .Which.Message.Should()
            .Be($"Paymob Cash In - Http request failed with status code ({(int)statusCode}).");
        assertions.Which.Body.Should().Be(body);
    }
}
