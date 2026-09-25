// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using Headless.Api;
using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests;

public sealed class AuthorizationDenialAuditTests : TestBase
{
    [Fact]
    public async Task should_audit_forbidden_request_with_route_template_method_and_policies()
    {
        // given
        var writer = new CapturingAuditLogWriter();
        await using var app = await _CreateAppAsync(writer);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        using var response = await _SendAsync(client, HttpMethod.Delete, "/orders/42?secret=value", user: "alice");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var request = writer.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be("authorization.forbidden");
        request.Success.Should().BeFalse();
        request.Data.Should().NotBeNull();
        request.Data!["method"].Should().Be("DELETE");
        request.Data["route"].Should().Be("/orders/{id}");
        request.Data["policies"].Should().BeEquivalentTo(new[] { "Denied" });
    }

    [Fact]
    public async Task should_audit_challenged_request_when_caller_is_unauthenticated()
    {
        // given
        var writer = new CapturingAuditLogWriter();
        await using var app = await _CreateAppAsync(writer);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        using var response = await _SendAsync(client, HttpMethod.Get, "/authenticated");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var request = writer.Requests.Should().ContainSingle().Subject;
        request.Action.Should().Be("authorization.challenged");
        request.Data!["route"].Should().Be("/authenticated");
        request.Data["policies"].Should().BeEquivalentTo(Array.Empty<string>());
    }

    [Fact]
    public async Task should_not_audit_authorized_request()
    {
        // given
        var writer = new CapturingAuditLogWriter();
        await using var app = await _CreateAppAsync(writer);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        using var response = await _SendAsync(client, HttpMethod.Get, "/authenticated", user: "alice");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        writer.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_denial_response_and_log_error_when_audit_write_fails()
    {
        // given
        var writer = new CapturingAuditLogWriter { Failure = new InvalidOperationException("store down") };
        using var logs = new CapturingLoggerProvider();
        await using var app = await _CreateAppAsync(writer, logs);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        using var response = await _SendAsync(client, HttpMethod.Delete, "/orders/42", user: "alice");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        logs.Entries.Should()
            .ContainSingle(entry => entry.EventId.Name == "AuthorizationDenialAuditWriteFailed")
            .Which.Level.Should()
            .Be(LogLevel.Error);
    }

    [Fact]
    public async Task should_wrap_custom_result_handler_registered_before_it_and_register_once()
    {
        // given
        var writer = new CapturingAuditLogWriter();
        var inner = new CountingResultHandler();
        await using var app = await _CreateAppAsync(
            writer,
            configureServices: services =>
            {
                services.AddSingleton<IAuthorizationMiddlewareResultHandler>(inner);
                services.AddHeadlessAuthorizationDenialAudit<object>();
            }
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        using var response = await _SendAsync(client, HttpMethod.Delete, "/orders/42", user: "alice");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inner.Calls.Should().Be(1);
        writer.Requests.Should().ContainSingle();
    }

    private static async Task<WebApplication> _CreateAppAsync(
        CapturingAuditLogWriter writer,
        CapturingLoggerProvider? logs = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddSingleton<IAuditLogWriter<object>>(writer);
        builder.Services.AddTestAuthentication(registerForbidScheme: true);
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("Denied", policy => policy.RequireAuthenticatedUser().RequireClaim("never-issued"));
        configureServices?.Invoke(builder.Services);
        builder.Services.AddHeadlessAuthorizationDenialAudit<object>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDelete("/orders/{id}", (string id) => Results.Ok(id)).RequireAuthorization("Denied");
        app.MapGet("/authenticated", () => Results.Ok()).RequireAuthorization();

        await app.StartAsync(AbortToken);

        return app;
    }

    private static async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? user = null
    )
    {
        using var request = HttpTenancyTestHarness.CreateRequest(method, path, user);

        return await client.SendAsync(request, AbortToken);
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter<object>
    {
        private readonly ConcurrentQueue<AuditLogWriteRequest> _requests = new();

        public Exception? Failure { get; init; }

        public IReadOnlyCollection<AuditLogWriteRequest> Requests => _requests.ToArray();

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            _requests.Enqueue(request);

            return Task.CompletedTask;
        }
    }

    private sealed class CountingResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult
        )
        {
            Interlocked.Increment(ref _calls);

            return _default.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
