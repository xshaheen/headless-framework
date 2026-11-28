// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Payments.Paymob.CashIn;

namespace Tests;

public sealed partial class PaymobCashInBrokerTests(PaymobCashInFixture fixture) : IClassFixture<PaymobCashInFixture>
{
    protected override Faker Faker { get; set; } = CashInTestData.CreateFaker();

    private (IPaymobCashInAuthenticator authenticator, string token) _SetupGentAuthenticationToken()
    {
        var token = CashInTestData.Text(Faker);
        var authenticator = Substitute.For<IPaymobCashInAuthenticator>();
        authenticator
            .GetAuthenticationTokenAsync(Arg.Any<CancellationToken>())
            .Returns(PaymobCashInResult.Succeeded(token));
        return (authenticator, token);
    }

    private static async Task _ShouldBeUnknownAsync<T>(
        Func<Task<PaymobCashInResult<T>>> invocation,
        HttpStatusCode statusCode,
        string? body
    )
    {
        var result = await invocation();

        result.IsUnknown.Should().BeTrue("Paymob may have applied the request before failing");
        result.Error!.StatusCode.Should().Be(statusCode);
        result
            .Error.Message.Should()
            .Be(
                $"Paymob Cash In - Paymob answered with status code ({(int)statusCode}); the request may have been applied."
            );
        result.Error.Body.Should().Be(body);
    }
}
