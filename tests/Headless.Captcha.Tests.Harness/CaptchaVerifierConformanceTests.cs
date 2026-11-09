// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using Headless.Captcha;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// The cross-provider verifier conformance contract. Every captcha provider derives this base with its own
/// <see cref="ICaptchaVerifierFixture"/> and inherits these scenarios — round-trip success, well-formed rejection,
/// HTTP failure, null-body deserialization failure, cancellation, and conditional <c>remoteip</c> encoding.
/// </summary>
/// <typeparam name="TFixture">The provider's conformance fixture.</typeparam>
public abstract class CaptchaVerifierConformanceTests<TFixture>(TFixture fixture) : TestBase, IClassFixture<TFixture>
    where TFixture : class, ICaptchaVerifierFixture
{
    [Fact]
    public async Task valid_token_returns_success_with_common_fields()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, fixture.SuccessResponseBody);
        var verifier = fixture.CreateVerifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "valid-token" }, AbortToken);

        result.Success.Should().BeTrue();
        result.HostName.Should().NotBeNull();
        result.ChallengeTimestamp.Should().NotBeNull();
    }

    [Fact]
    public async Task rejected_token_returns_failure_with_error_codes()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, fixture.RejectedResponseBody);
        var verifier = fixture.CreateVerifier(stub);

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "rejected-token" }, AbortToken);

        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task non_success_http_status_throws_http_request_exception()
    {
        // 400 is not retried by the standard resilience handler, so the failure surfaces immediately.
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.BadRequest, "{}");
        var verifier = fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData(null, false)]
    public async Task http_failure_logs_the_response_body_only_in_development(string? environment, bool logsBody)
    {
        // given: a provider error body that must not reach production logs
        const string body = "provider-detail-8f3a";
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.BadRequest, body);
        using var logs = new CapturingLoggerProvider();
        var verifier = fixture.CreateVerifierWithServices(
            stub,
            services =>
            {
                services.AddLogging(logging => logging.AddProvider(logs));

                if (environment is not null)
                {
                    services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(environment));
                }
            }
        );

        // when
        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        // then: the failure is logged either way; the body only in Development
        await act.Should().ThrowAsync<HttpRequestException>();
        var entry = logs.Messages.Should().ContainSingle().Subject;
        entry.Contains(body, StringComparison.Ordinal).Should().Be(logsBody);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        // Only the verifier's own entries: the HTTP client logs the same status under System.Net.Http.
        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Headless.Captcha", StringComparison.Ordinal)
                ? new Logger(_messages)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => messages.Enqueue(formatter(state, exception));
        }
    }

    [Fact]
    public async Task null_json_body_throws_invalid_operation()
    {
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, "null");
        var verifier = fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task already_cancelled_token_throws_and_makes_no_http_call()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, fixture.SuccessResponseBody);
        var verifier = fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stub.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task malformed_json_body_on_http_200_throws_invalid_operation()
    {
        // A non-JSON HTTP 200 (e.g. an HTML error page) must surface as InvalidOperationException, not a raw
        // JsonException — callers only need to handle the documented exception contract.
        using var stubSiteVerifyHandler = new StubSiteVerifyHandler();
        var stub = stubSiteVerifyHandler.EnqueueJson(HttpStatusCode.OK, "<html>oops</html>");
        var verifier = fixture.CreateVerifier(stub);

        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task remote_ip_included_only_when_supplied()
    {
        using var stub1 = new StubSiteVerifyHandler();
        var withIpStub = stub1.EnqueueJson(HttpStatusCode.OK, fixture.SuccessResponseBody);
        var withIpVerifier = fixture.CreateVerifier(withIpStub);
        await withIpVerifier.VerifyAsync(
            new CaptchaVerifyRequest { Response = "token", RemoteIp = "203.0.113.5" },
            AbortToken
        );

        withIpStub.LastRequestBody.Should().Contain("remoteip");
        withIpStub.LastRequestBody.Should().Contain("203.0.113.5");

        using var stub2 = new StubSiteVerifyHandler();
        var withoutIpStub = stub2.EnqueueJson(HttpStatusCode.OK, fixture.SuccessResponseBody);
        var withoutIpVerifier = fixture.CreateVerifier(withoutIpStub);
        await withoutIpVerifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        withoutIpStub.LastRequestBody.Should().NotContain("remoteip");
    }
}
