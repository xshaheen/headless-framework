// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using FluentAssertions.Extensions;
using Framework.Constants;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class MvcProblemDetailsTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task mvc_bad_request()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/mvc/malformed-syntax");
        await _VerifyMalformedSyntax(response);
    }

    [Fact]
    public async Task minimal_api_bad_request()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/minimal/malformed-syntax");
        await _VerifyMalformedSyntax(response);
    }

    private static async Task _VerifyMalformedSyntax(HttpResponseMessage response)
    {
        /*
         * {
         *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.5.1",
         *   "title" : "bad-request",
         *   "status" : 400,
         *   "detail" : "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.",
         *   "instance" : "/minimal/malformed-syntax",
         *   "traceId" : "00-1402a1325f82be1e9277e334c617d122-5a8c02fbde4b5d93-00",
         *   "buildNumber" : "2.16.1.109",
         *   "commitNumber" : "2.16.1.109",
         *   "timestamp" : "2025-01-28T15:20:52.9121154+00:00"
         * }
         */

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        const string details =
            "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.";

        jsonElement.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.1");
        jsonElement.GetProperty("title").GetString().Should().Be("bad-request");
        jsonElement.GetProperty("status").GetInt32().Should().Be(400);
        jsonElement.GetProperty("detail").GetString().Should().Be(details);
        jsonElement.GetProperty("instance").GetString().Should().Be(response.RequestMessage!.RequestUri!.PathAndQuery);
        jsonElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("buildNumber").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("commitNumber").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, 1.Seconds());
        jsonElement.EnumerateObject().Count().Should().Be(9);
    }

    private async Task<WebApplicationFactory<Program>> _CreateDefaultFactory(
        Action<WebHostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IWebHostBuilder>? configureHost = null
    )
    {
        await using var factory = new WebApplicationFactory<Program>();

        factory.ClientOptions.AllowAutoRedirect = false;

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(EnvironmentNames.Test);
            builder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders().AddProvider(LoggerProvider));
            configureHost?.Invoke(builder);

            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }
}
