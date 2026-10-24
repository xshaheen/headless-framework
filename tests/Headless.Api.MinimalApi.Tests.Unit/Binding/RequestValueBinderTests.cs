// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace Tests.Binding;

/// <summary>
/// Pins that a Minimal API endpoint binds an enum outside the body by the names the JSON body accepts, and answers a
/// value it cannot bind with 400 naming it, as an MVC action does under ConfigureHeadlessMvc().
/// </summary>
public sealed class RequestValueBinderTests : TestBase
{
    [Theory]
    [InlineData("active")]
    [InlineData("Active")]
    [InlineData("ACTIVE")]
    public async Task should_bind_an_enum_query_value_by_name_in_any_casing(string value)
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none"),
            request => request.QueryString = new QueryString($"?status={value}")
        );

        (await _ReadTextAsync(context)).Should().Be("Active");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("bogus")]
    // The body rejects a member name its [JsonStringEnumMemberName] alias replaces, so the query does too.
    [InlineData("OnHold")]
    public async Task should_answer_400_naming_the_parameter_when_an_enum_query_value_is_not_a_name(string value)
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none"),
            request => request.QueryString = new QueryString($"?status={value}")
        );

        await _ShouldBeInvalidParameterAsync(context, "status");
    }

    [Fact]
    public async Task should_bind_a_json_enum_member_name_and_a_flags_combination_from_the_query()
    {
        var alias = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus status) => status.ToString()),
            request => request.QueryString = new QueryString("?status=on-hold")
        );
        var flags = await _InvokeAsync(
            group => group.MapGet("/access", (Access access) => access.ToString()),
            request => request.QueryString = new QueryString("?access=read,Write")
        );

        (await _ReadTextAsync(alias)).Should().Be("OnHold");
        (await _ReadTextAsync(flags)).Should().Be("Read, Write");
    }

    [Fact]
    public async Task should_bind_every_enum_of_a_repeated_query_value()
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/list", ([FromQuery] MemberStatus[] status) => string.Join('|', status)),
            request => request.QueryString = new QueryString("?status=active&status=INACTIVE")
        );

        (await _ReadTextAsync(context)).Should().Be("Active|Inactive");
    }

    [Fact]
    public async Task should_bind_an_enum_header_value_by_name_and_reject_its_number()
    {
        static void map(RouteGroupBuilder group) =>
            group.MapGet(
                "/header",
                ([FromHeader(Name = "X-Status")] MemberStatus? status) => status?.ToString() ?? "none"
            );

        var named = await _InvokeAsync(map, request => request.Headers["X-Status"] = "inactive");
        var numbered = await _InvokeAsync(map, request => request.Headers["X-Status"] = "2");

        (await _ReadTextAsync(named)).Should().Be("Inactive");
        await _ShouldBeInvalidParameterAsync(numbered, "X-Status");
    }

    [Fact]
    public async Task should_bind_an_enum_route_value_by_name()
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/route/{status}", (MemberStatus status) => status.ToString()),
            request => request.RouteValues["status"] = "on-hold"
        );

        (await _ReadTextAsync(context)).Should().Be("OnHold");
    }

    [Fact]
    public async Task should_bind_an_enum_form_field_by_name_and_reject_its_number()
    {
        static void map(RouteGroupBuilder group) =>
            group
                .MapPost("/form", ([FromForm] StatusForm form) => form.Status?.ToString() ?? "none")
                .DisableAntiforgery();

        var named = await _InvokeAsync(map, request => _SetForm(request, "Status", "inactive"));
        var numbered = await _InvokeAsync(map, request => _SetForm(request, "Status", "2"));

        (await _ReadTextAsync(named)).Should().Be("Inactive");
        await _ShouldBeInvalidParameterAsync(numbered, "Status");
    }

    [Fact]
    public async Task should_bind_a_simple_enum_form_value_by_name()
    {
        var context = await _InvokeAsync(
            group => group.MapPost("/form", ([FromForm] MemberStatus status) => status.ToString()).DisableAntiforgery(),
            request => _SetForm(request, "status", "ACTIVE")
        );

        (await _ReadTextAsync(context)).Should().Be("Active");
    }

    [Fact]
    public async Task should_answer_400_naming_the_parameter_when_a_number_query_value_is_not_a_number()
    {
        static void map(RouteGroupBuilder group) =>
            group.MapGet("/page", (int page) => page.ToString(CultureInfo.InvariantCulture));

        var valid = await _InvokeAsync(map, request => request.QueryString = new QueryString("?page=2"));
        var invalid = await _InvokeAsync(map, request => request.QueryString = new QueryString("?page=abc"));

        (await _ReadTextAsync(valid)).Should().Be("2");
        await _ShouldBeInvalidParameterAsync(invalid, "page");
    }

    [Fact]
    public async Task should_check_the_members_of_an_as_parameters_argument()
    {
        static void map(RouteGroupBuilder group) =>
            group.MapGet("/filter", ([AsParameters] StatusFilter filter) => $"{filter.Status}:{filter.Page}");

        var valid = await _InvokeAsync(
            map,
            request => request.QueryString = new QueryString("?status=inactive&page=3")
        );
        var invalid = await _InvokeAsync(map, request => request.QueryString = new QueryString("?status=3"));

        (await _ReadTextAsync(valid)).Should().Be("Inactive:3");
        await _ShouldBeInvalidParameterAsync(invalid, "Status");
    }

    [Fact]
    public async Task should_apply_the_rules_to_every_endpoint_of_an_api_surface()
    {
        await using var services = _CreateServices(s => s.AddHeadlessApiSurface("portal"));
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapApiSurface("portal")
            .MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none")
            // A second application must not wrap the endpoint twice.
            .BindRequestValues();
        var context = MinimalApiTestHost.CreateHttpContext(services);
        context.Request.QueryString = new QueryString("?status=inactive");

        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(context);

        (await _ReadTextAsync(context)).Should().Be("Inactive");
    }

    [Fact]
    public async Task should_leave_an_endpoint_without_the_convention_on_the_host_parsing()
    {
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none");
        var context = MinimalApiTestHost.CreateHttpContext(services);
        context.Request.QueryString = new QueryString("?status=active");

        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static async Task<DefaultHttpContext> _InvokeAsync(
        Action<RouteGroupBuilder> map,
        Action<HttpRequest> arrange
    )
    {
        // The response is read after the call, which needs no service, so the container ends with it.
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        map(endpoints.MapGroup("").BindRequestValues());
        var context = MinimalApiTestHost.CreateHttpContext(services);
        arrange(context.Request);

        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(context);

        return context;
    }

    private static ServiceProvider _CreateServices(Action<IServiceCollection>? configure = null)
    {
        return MinimalApiTestHost.CreateServices(services =>
        {
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Production);
            services.AddSingleton(environment);
            services.ConfigureHeadlessMinimalApi();
            configure?.Invoke(services);
        });
    }

    private static void _SetForm(HttpRequest request, string key, string value)
    {
        request.Method = HttpMethods.Post;
        request.ContentType = "application/x-www-form-urlencoded";
        request.Form = new FormCollection(new Dictionary<string, StringValues> { [key] = value });
    }

    private static async Task<string> _ReadTextAsync(HttpContext context)
    {
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);

        return await reader.ReadToEndAsync(AbortToken);
    }

    private static async Task _ShouldBeInvalidParameterAsync(HttpContext context, string parameter)
    {
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await MinimalApiTestHost.ReadJsonAsync(context, AbortToken);
        var error = body.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("g:invalid_parameter_value");
        error.GetProperty("params").GetProperty("parameter").GetString().Should().Be(parameter);
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
    public MemberStatus? Status { get; set; }
}

public sealed record StatusFilter(MemberStatus? Status, int Page = 1);
