// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Threading.RateLimiting;
using Headless.Api.Resources;
using Headless.Api.ServiceDefaults;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessRateLimitingTests : TestBase
{
    [Fact]
    public async Task should_reject_over_limit_request_with_problem_details_retry_after_and_no_store()
    {
        // given
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);
        using var admitted = await client.GetAsync("/data", AbortToken);

        // when
        using var rejected = await client.GetAsync("/data", AbortToken);

        // then
        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        rejected.Headers.CacheControl!.NoStore.Should().BeTrue();
        var retryAfter = rejected.Headers.RetryAfter!.Delta!.Value;
        retryAfter
            .Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(AbortToken));
        body.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status429TooManyRequests);
        body.RootElement.GetProperty("retryAfter").GetInt32().Should().Be((int)retryAfter.TotalSeconds);
        body.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(GeneralErrorCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task should_never_rate_limit_health_and_alive_endpoints()
    {
        // given
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        // when
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            using var health = await client.GetAsync("/health", AbortToken);
            using var alive = await client.GetAsync("/alive", AbortToken);
            statuses.Add(health.StatusCode);
            statuses.Add(alive.StatusCode);
        }

        // then
        statuses.Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.OK));
    }

    // One permit per minute for every request, so the second request to any limited endpoint is rejected.
    private async Task<WebApplication> _CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
            new KeyValuePair<string, string?>("Headless:LookupHasher:DefaultSalt", "TestSalt"),
        ]);
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        builder.Services.AddAuthentication();
        builder.Services.AddRateLimiter(options =>
        {
            options.UseHeadlessProblemDetails();
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "all",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }
                )
            );
        });

        var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
        });
        app.UseRateLimiter();
        app.MapHeadlessEndpoints();
        app.MapGet("/data", () => Results.Ok(new { Value = "test" }));

        await app.StartAsync(AbortToken);
        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }
}
