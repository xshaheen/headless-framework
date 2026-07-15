// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using AutoFixture;
using Headless.Payments.Paymob.CashOut;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests;

public sealed class PaymobCashOutAuthenticatorTests(PaymobCashOutFixture fixture)
    : TestBase,
        IClassFixture<PaymobCashOutFixture>
{
    [Fact]
    public async Task should_post_password_grant_with_basic_auth_when_no_cached_token()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var accessToken = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/o/token/")
                    .UsingPost()
                    .WithHeader("Authorization", _ExpectedBasicHeader(options))
                    .WithBody(_PasswordGrantBody(options))
            )
            .RespondWith(Response.Create().WithBody(_AuthJson(accessToken)));

        // when
        using var authenticator = _CreateAuthenticator(monitor);
        var result = await authenticator.GetAccessTokenAsync(AbortToken);

        // then
        result.Should().Be(accessToken);
    }

    [Fact]
    public async Task should_return_cached_token_without_new_request_when_token_still_valid()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var accessToken = fixture.AutoFixture.Create<string>();
        var callCount = 0;

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(
                Response
                    .Create()
                    .WithBody(_ =>
                    {
                        Interlocked.Increment(ref callCount);
                        return _AuthJson(accessToken);
                    })
            );

        // when - second call still inside the refresh buffer (default 10 minutes)
        using var authenticator = _CreateAuthenticator(monitor, timeProvider);
        var first = await authenticator.GetAccessTokenAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(9));
        var second = await authenticator.GetAccessTokenAsync(AbortToken);

        // then
        callCount.Should().Be(1);
        first.Should().Be(accessToken);
        second.Should().Be(accessToken);
    }

    [Fact]
    public async Task should_request_new_token_when_cached_token_expired()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var callCount = 0;

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(Response.Create().WithBody(_ => _AuthJson($"token-{Interlocked.Increment(ref callCount)}")));

        // when - second call after the refresh buffer elapsed
        using var authenticator = _CreateAuthenticator(monitor, timeProvider);
        var first = await authenticator.GetAccessTokenAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var second = await authenticator.GetAccessTokenAsync(AbortToken);

        // then
        callCount.Should().Be(2);
        first.Should().Be("token-1");
        second.Should().Be("token-2");
    }

    [Fact]
    public async Task should_make_single_token_request_when_concurrent_callers_race_on_empty_cache()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var accessToken = fixture.AutoFixture.Create<string>();
        var callCount = 0;

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(
                Response
                    .Create()
                    .WithBody(_ =>
                    {
                        Interlocked.Increment(ref callCount);
                        Thread.Sleep(50); // Simulate network latency so callers overlap
                        return _AuthJson(accessToken);
                    })
            );

        // when
        using var authenticator = _CreateAuthenticator(monitor);
        var tasks = Enumerable.Range(0, 10).Select(_ => authenticator.GetAccessTokenAsync(AbortToken).AsTask());
        var results = await Task.WhenAll(tasks);

        // then - double-checked lock allows exactly one refresh call
        callCount.Should().Be(1);
        results.Should().AllBe(accessToken);
    }

    [Fact]
    public async Task should_discard_cached_token_when_options_change()
    {
        // given
        var (options, _) = _CreateOptions();
        var monitor = new ChangeableOptionsMonitor(options);
        var callCount = 0;

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(Response.Create().WithBody(_ => _AuthJson($"token-{Interlocked.Increment(ref callCount)}")));

        // when - options change (e.g. credential rotation) invalidates the cached token
        using var authenticator = _CreateAuthenticator(monitor);
        var first = await authenticator.GetAccessTokenAsync(AbortToken);
        monitor.Change(options);
        var second = await authenticator.GetAccessTokenAsync(AbortToken);

        // then
        callCount.Should().Be(2);
        first.Should().Be("token-1");
        second.Should().Be("token-2");
    }

    [Fact]
    public async Task should_throw_paymob_cash_out_exception_with_status_and_body_when_token_request_fails()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var body = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Unauthorized).WithBody(body));

        // when
        using var authenticator = _CreateAuthenticator(monitor);
        var act = () => authenticator.GetAccessTokenAsync(AbortToken).AsTask();

        // then
        var assertion = await act.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        assertion.Which.Body.Should().Be(body);
        assertion.Which.Message.Should().Be("Paymob Cash Out - Http request failed with status code (401).");
    }

    [Fact]
    public async Task should_cache_new_access_token_when_refresh_token_exchanged()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var refreshToken = fixture.AutoFixture.Create<string>();
        var accessToken = fixture.AutoFixture.Create<string>();
        var newRefreshToken = fixture.AutoFixture.Create<string>();
        var passwordGrantCalls = 0;

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/o/token")
                    .UsingPost()
                    .WithHeader("Authorization", _ExpectedBasicHeader(options))
                    .WithBody($"grant_type=refresh_token&refresh_token={refreshToken}")
            )
            .RespondWith(Response.Create().WithBody(_AuthJson(accessToken, newRefreshToken)));

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(
                Response
                    .Create()
                    .WithBody(_ =>
                    {
                        Interlocked.Increment(ref passwordGrantCalls);
                        return _AuthJson("unexpected");
                    })
            );

        // when
        using var authenticator = _CreateAuthenticator(monitor);
        var response = await authenticator.RefreshTokenAsync(refreshToken, AbortToken);
        var cached = await authenticator.GetAccessTokenAsync(AbortToken);

        // then - the refreshed token is cached, so no password-grant request is made
        response.AccessToken.Should().Be(accessToken);
        response.RefreshToken.Should().Be(newRefreshToken);
        cached.Should().Be(accessToken);
        passwordGrantCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_paymob_cash_out_exception_when_refresh_token_request_fails()
    {
        // given
        var (options, monitor) = _CreateOptions();
        var refreshToken = fixture.AutoFixture.Create<string>();
        var body = fixture.AutoFixture.Create<string>();

        fixture
            .Server.Given(
                Request
                    .Create()
                    .WithPath("/o/token")
                    .UsingPost()
                    .WithBody($"grant_type=refresh_token&refresh_token={refreshToken}")
            )
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.BadRequest).WithBody(body));

        // when
        using var authenticator = _CreateAuthenticator(monitor);
        var act = () => authenticator.RefreshTokenAsync(refreshToken, AbortToken);

        // then
        var assertion = await act.Should().ThrowAsync<PaymobCashOutException>();
        assertion.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        assertion.Which.Body.Should().Be(body);
    }

    [Fact]
    public async Task should_cancel_token_request_when_cancellation_requested()
    {
        // given
        var (options, monitor) = _CreateOptions();

        fixture
            .Server.Given(Request.Create().WithPath("/o/token/").UsingPost().WithBody(_PasswordGrantBody(options)))
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(5)).WithBody(_AuthJson("late")));

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var authenticator = _CreateAuthenticator(monitor);
        var act = () => authenticator.GetAccessTokenAsync(cts.Token).AsTask();

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_object_disposed_exception_when_used_after_dispose()
    {
        // given
        var (_, monitor) = _CreateOptions();
        var authenticator = _CreateAuthenticator(monitor);

        // when
        authenticator.Dispose();

        // then - empty cache forces the semaphore path, which is disposed
        var act = () => authenticator.GetAccessTokenAsync(AbortToken).AsTask();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private (PaymobCashOutOptions options, IOptionsMonitor<PaymobCashOutOptions> monitor) _CreateOptions()
    {
        var options = new PaymobCashOutOptions
        {
            ApiBaseUrl = fixture.Server.Urls[0],
            UserName = fixture.AutoFixture.Create<string>(),
            Password = fixture.AutoFixture.Create<string>(),
            ClientId = fixture.AutoFixture.Create<string>(),
            ClientSecret = fixture.AutoFixture.Create<string>(),
        };

        var monitor = Substitute.For<IOptionsMonitor<PaymobCashOutOptions>>();
        monitor.CurrentValue.Returns(options);

        return (options, monitor);
    }

    private PaymobCashOutAuthenticator _CreateAuthenticator(
        IOptionsMonitor<PaymobCashOutOptions> monitor,
        TimeProvider? timeProvider = null
    )
    {
        return new PaymobCashOutAuthenticator(
            fixture.HttpClientFactory,
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            monitor
        );
    }

    private static string _PasswordGrantBody(PaymobCashOutOptions options)
    {
        return $"grant_type=password&username={options.UserName}&password={options.Password}";
    }

    private static string _ExpectedBasicHeader(PaymobCashOutOptions options)
    {
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
    }

    private static string _AuthJson(string accessToken, string refreshToken = "refresh-token")
    {
        var response = new CashOutAuthenticationResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Scope = "read write",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        };

        return JsonSerializer.Serialize(response);
    }

    private sealed class ChangeableOptionsMonitor(PaymobCashOutOptions initial) : IOptionsMonitor<PaymobCashOutOptions>
    {
        private readonly List<Action<PaymobCashOutOptions, string?>> _listeners = [];

        public PaymobCashOutOptions CurrentValue { get; private set; } = initial;

        public PaymobCashOutOptions Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<PaymobCashOutOptions, string?> listener)
        {
            _listeners.Add(listener);

            return null;
        }

        public void Change(PaymobCashOutOptions value)
        {
            CurrentValue = value;

            foreach (var listener in _listeners)
            {
                listener(value, null);
            }
        }
    }
}
