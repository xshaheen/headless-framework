// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;
using Headless.Payments.Paymob.CashIn.Models.Auth;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests;

public sealed partial class PaymobCashInAuthenticatorTests : TestBase
{
    [Fact]
    public async Task should_make_single_api_call_when_concurrent_requests_with_expired_token()
    {
        // given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var callCount = 0;
        var apiKey = fixture.AutoFixture.Create<string>();
        var token = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(
                Response
                    .Create()
                    .WithBody(_ =>
                    {
                        Interlocked.Increment(ref callCount);
                        Thread.Sleep(50); // Simulate network latency
                        var response = new CashInAuthenticationTokenResponse { Token = token };
                        return JsonSerializer.Serialize(response);
                    })
            );

        // when
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            timeProvider,
            fixture.OptionsAccessor
        );
        // ReSharper disable once AccessToDisposedClosure
        var tasks = Enumerable.Range(0, 10).Select(_ => authenticator.GetAuthenticationTokenAsync().AsTask());
        var results = await Task.WhenAll(tasks);

        // then
        callCount.Should().Be(1);
        results.Should().AllBe(token);
    }

    [Fact]
    public async Task should_return_cached_token_when_valid_during_concurrent_requests()
    {
        // given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var callCount = 0;
        var apiKey = fixture.AutoFixture.Create<string>();
        var token = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(
                Response
                    .Create()
                    .WithBody(_ =>
                    {
                        // ReSharper disable once AccessToModifiedClosure
                        Interlocked.Increment(ref callCount);
                        var response = new CashInAuthenticationTokenResponse { Token = token };
                        return JsonSerializer.Serialize(response);
                    })
            );

        // when - first call to populate cache
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            timeProvider,
            fixture.OptionsAccessor
        );

        await authenticator.GetAuthenticationTokenAsync(AbortToken);

        // reset counter and make concurrent requests
        callCount = 0;
        // ReSharper disable once AccessToDisposedClosure
        var tasks = Enumerable.Range(0, 10).Select(_ => authenticator.GetAuthenticationTokenAsync().AsTask());
        var results = await Task.WhenAll(tasks);

        // then - no additional API calls should be made
        callCount.Should().Be(0);
        results.Should().AllBe(token);
    }

    [Fact]
    public async Task should_cancel_waiting_requests_when_cancellation_requested()
    {
        // given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var apiKey = fixture.AutoFixture.Create<string>();
        var config = fixture.CashInOptions with { ApiKey = apiKey };
        fixture.OptionsAccessor.CurrentValue.Returns(config);
        var request = new CashInAuthenticationTokenRequest { ApiKey = apiKey };
        var requestJson = JsonSerializer.Serialize(request);

        fixture
            .Server.Given(Request.Create().WithPath("/auth/tokens").UsingPost().WithBody(requestJson))
            .RespondWith(
                Response
                    .Create()
                    .WithDelay(TimeSpan.FromSeconds(5))
                    .WithBody(_ =>
                    {
                        var response = fixture.AutoFixture.Create<CashInAuthenticationTokenResponse>();
                        return JsonSerializer.Serialize(response);
                    })
            );

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            timeProvider,
            fixture.OptionsAccessor
        );

        var token = cts.Token;

        // then
        // ReSharper disable once AccessToDisposedClosure
        var act = () => authenticator.GetAuthenticationTokenAsync(token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_dispose_semaphore_when_disposed()
    {
        // given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var authenticator = new PaymobCashInAuthenticator(
            fixture.HttpClientFactory,
            timeProvider,
            fixture.OptionsAccessor
        );

        // when
        authenticator.Dispose();

        // then - subsequent calls should throw ObjectDisposedException
        var act = () => authenticator.GetAuthenticationTokenAsync().AsTask();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
