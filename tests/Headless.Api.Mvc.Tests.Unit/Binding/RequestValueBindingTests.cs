// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Headless.Api;
using Headless.Context;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Binding;

/// <summary>
/// Pins how an MVC action binds an enum outside the body (by name in any casing, never by number) and what a value that
/// cannot be bound answers (400 naming it, instead of running the action with the default).
/// </summary>
public sealed class RequestValueBindingTests : TestBase
{
    [Theory]
    [InlineData("active")]
    [InlineData("Active")]
    [InlineData("ACTIVE")]
    public async Task should_bind_an_enum_query_value_by_name_in_any_casing(string value)
    {
        await using var app = await _StartAsync();

        var status = await app.GetTestClient().GetStringAsync($"/bindings/query?status={value}", AbortToken);

        status.Should().Be("Active");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("bogus")]
    // The body rejects a member name its [JsonStringEnumMemberName] alias replaces, so the query does too.
    [InlineData("OnHold")]
    public async Task should_answer_400_naming_the_parameter_when_an_enum_query_value_is_not_a_name(string value)
    {
        await using var app = await _StartAsync();

        using var response = await app.GetTestClient().GetAsync($"/bindings/query?status={value}", AbortToken);

        await _ShouldBeInvalidParameterAsync(response, "status");
    }

    [Fact]
    public async Task should_leave_an_omitted_enum_query_value_null()
    {
        await using var app = await _StartAsync();

        var status = await app.GetTestClient().GetStringAsync("/bindings/query", AbortToken);

        status.Should().Be("none");
    }

    [Fact]
    public async Task should_bind_a_json_enum_member_name_and_a_flags_combination()
    {
        await using var app = await _StartAsync();
        var client = app.GetTestClient();

        (await client.GetStringAsync("/bindings/query?status=on-hold", AbortToken)).Should().Be("OnHold");
        (await client.GetStringAsync("/bindings/access?access=read,Write", AbortToken)).Should().Be("Read, Write");
    }

    [Fact]
    public async Task should_bind_an_enum_form_field_by_name_and_reject_its_number()
    {
        await using var app = await _StartAsync();
        var client = app.GetTestClient();

        using var nameForm = new FormUrlEncodedContent([new("Status", "inactive")]);
        using var byName = await client.PostAsync("/bindings/form", nameForm, AbortToken);
        (await byName.Content.ReadAsStringAsync(AbortToken)).Should().Be("Inactive");

        using var numberForm = new FormUrlEncodedContent([new("Status", "2")]);
        using var byNumber = await client.PostAsync("/bindings/form", numberForm, AbortToken);
        await _ShouldBeInvalidParameterAsync(byNumber, "Status");
    }

    [Fact]
    public async Task should_answer_400_when_a_number_query_value_is_not_a_number()
    {
        await using var app = await _StartAsync();

        using var response = await app.GetTestClient().GetAsync("/bindings/page?index=abc", AbortToken);

        await _ShouldBeInvalidParameterAsync(response, "index");
    }

    [Fact]
    public async Task should_bind_a_json_body_enum_by_name_and_answer_the_bad_request_problem_for_its_number()
    {
        await using var app = await _StartAsync();
        var client = app.GetTestClient();

        using var byName = await client.PostAsJsonAsync("/bindings/body", new { status = "active" }, AbortToken);
        (await byName.Content.ReadAsStringAsync(AbortToken)).Should().Be("Active");

        // A body that does not read is not a parameter value; a Minimal API endpoint answers the same problem.
        using var byNumber = await client.PostAsJsonAsync("/bindings/body", new { status = 1 }, AbortToken);
        byNumber.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await byNumber.Content.ReadAsStringAsync(AbortToken));
        body.RootElement.GetProperty("title")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        body.RootElement.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_bind_an_enum_header_value_by_name_and_reject_its_number()
    {
        await using var app = await _StartAsync();
        var client = app.GetTestClient();

        using var byName = new HttpRequestMessage(HttpMethod.Get, "/bindings/header");
        byName.Headers.Add("X-Status", "inactive");
        using var named = await client.SendAsync(byName, AbortToken);
        (await named.Content.ReadAsStringAsync(AbortToken)).Should().Be("Inactive");

        using var byNumber = new HttpRequestMessage(HttpMethod.Get, "/bindings/header");
        byNumber.Headers.Add("X-Status", "2");
        using var numbered = await client.SendAsync(byNumber, AbortToken);
        await _ShouldBeInvalidParameterAsync(numbered, "X-Status");
    }

    [Fact]
    public async Task should_bind_an_enum_route_value_by_name_and_reject_its_number()
    {
        await using var app = await _StartAsync();
        var client = app.GetTestClient();

        (await client.GetStringAsync("/bindings/route/on-hold", AbortToken)).Should().Be("OnHold");

        using var byNumber = await client.GetAsync("/bindings/route/3", AbortToken);
        await _ShouldBeInvalidParameterAsync(byNumber, "status");
    }

    [Fact]
    public async Task should_use_an_explicit_model_binder_over_the_enum_binder()
    {
        await using var app = await _StartAsync();

        var status = await app.GetTestClient().GetStringAsync("/bindings/custom?status=active", AbortToken);

        status.Should().Be("OnHold");
    }

    private static async Task _ShouldBeInvalidParameterAsync(HttpResponseMessage response, string parameter)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var error = body.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("g:invalid_parameter_value");
        error.GetProperty("params").GetProperty("parameter").GetString().Should().Be(parameter);
    }

    private static async Task<WebApplication> _StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHeadlessProblemDetails();
        builder
            .Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.FeatureProviders.Add(new BindingsControllerFeature());
            });
        builder.Services.ConfigureHeadlessMvc();

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(AbortToken);

        return app;
    }

    private sealed class BindingsControllerFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(BindingsController).GetTypeInfo());
    }
}

#pragma warning disable CA1008 // Deliberate: the case under test is an enum with no zero member, as many application enums start at 1.
public enum MemberStatus
{
    Active = 1,
    Inactive = 2,

    [JsonStringEnumMemberName("on-hold")]
    OnHold = 3,
}
#pragma warning restore CA1008

[Flags]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed class StatusForm
{
    public MemberStatus? Status { get; init; }
}

public sealed record StatusBody(MemberStatus Status);

[NonController]
[ApiController]
[Route("bindings")]
public sealed class BindingsController : ControllerBase
{
    [HttpGet("query")]
    public string Query([FromQuery] MemberStatus? status) => status?.ToString() ?? "none";

    [HttpGet("access")]
    public string Flags([FromQuery] Access access) => access.ToString();

    [HttpGet("page")]
    public string Page([FromQuery] int index) => index.ToString(CultureInfo.InvariantCulture);

    [HttpPost("form")]
    public string Form([FromForm] StatusForm form) => form.Status?.ToString() ?? "none";

    [HttpPost("body")]
    public string Body(StatusBody body) => body.Status.ToString();

    [HttpGet("header")]
    public string Header([FromHeader(Name = "X-Status")] MemberStatus? status) => status?.ToString() ?? "none";

    [HttpGet("route/{status}")]
    public string Route([FromRoute] MemberStatus status) => status.ToString();

    [HttpGet("custom")]
    public string Custom([FromQuery, ModelBinder(typeof(OnHoldBinder))] MemberStatus status) => status.ToString();
}

/// <summary>Binds every value as <see cref="MemberStatus.OnHold"/>, so a test can tell it ran.</summary>
public sealed class OnHoldBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        bindingContext.Result = ModelBindingResult.Success(MemberStatus.OnHold);

        return Task.CompletedTask;
    }
}
