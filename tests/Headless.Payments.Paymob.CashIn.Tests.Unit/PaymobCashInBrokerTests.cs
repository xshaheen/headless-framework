// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests(PaymobCashInFixture fixture) : IClassFixture<PaymobCashInFixture>
{
    private static readonly Faker _Faker = new();

    private (IPaymobCashInAuthenticator authenticator, string token) _SetupGentAuthenticationToken()
    {
        var token = fixture.AutoFixture.Create<string>();
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator.GetAuthenticationTokenAsync(Arg.Any<CancellationToken>()).Returns(token);
        return (authenticator, token);
    }

    private static async Task _ShouldThrowPaymobRequestExceptionAsync<T>(
        Func<Task<T>> invocation,
        HttpStatusCode statusCode,
        string? body
    )
    {
        var assertions = await invocation.Should().ThrowAsync<PaymobCashInException>();
        assertions.Which.StatusCode.Should().Be(statusCode);
        assertions
            .Which.Message.Should()
            .Be($"Paymob Cash In - Http request failed with status code ({(int)statusCode}).");
        assertions.Which.Body.Should().Be(body);
    }
}
