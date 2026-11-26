// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Headless;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class DataGridRequestBindingTests : TestBase
{
    [Fact]
    public async Task should_bind_the_page_when_a_minimal_api_reads_a_data_grid_request_from_the_body()
    {
        // given
        await using var app = await _CreateAppAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var body = new StringContent("""{"page":{"index":1,"size":5}}""", Encoding.UTF8, "application/json");

        // when
        using var response = await client.PostAsync("/grid", body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PageResponse>(AbortToken);
        page.Should().Be(new PageResponse(1, 5));
    }

    [Fact]
    public async Task should_return_bad_request_when_the_body_page_omits_its_size()
    {
        // given
        await using var app = await _CreateAppAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var body = new StringContent("""{"page":{"index":1}}""", Encoding.UTF8, "application/json");

        // when
        using var response = await client.PostAsync("/grid", body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_bind_the_page_when_an_mvc_action_reads_a_data_grid_request_from_the_query_string()
    {
        // given
        await using var app = await _CreateAppAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        // when
        using var response = await client.GetAsync("/grid-mvc?page.index=1&page.size=5", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PageResponse>(AbortToken);
        page.Should().Be(new PageResponse(1, 5));
    }

    private async Task<WebApplication> _CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });
        builder.Services.AddAuthentication();
        builder.Services.AddControllers().AddApplicationPart(typeof(DataGridRequestController).Assembly);

        var app = builder.Build();
        app.UseHeadless(options =>
        {
            options.UseHttpsRedirection = false;
            options.UseHsts = false;
        });
        app.MapHeadlessEndpoints();
        app.MapPost("/grid", (DataGridBindingRequest request) => Results.Ok(PageResponse.From(request)));
        app.MapControllers();

        await app.StartAsync(AbortToken);
        return app;
    }
}

public sealed class DataGridBindingRequest : DataGridRequest;

public sealed record PageResponse(int Index, int Size)
{
    public static PageResponse? From(DataGridRequest request)
    {
        return request.Page is null ? null : new PageResponse(request.Page.Index, request.Page.Size);
    }
}

[ApiController]
[Route("grid-mvc")]
public sealed class DataGridRequestController : ControllerBase
{
    [HttpGet]
    public ActionResult<PageResponse?> Get([FromQuery] DataGridBindingRequest request)
    {
        return PageResponse.From(request);
    }
}
