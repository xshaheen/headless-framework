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
public sealed class EnumNumberGuardTests : TestBase
{
    [Theory]
    [InlineData("Active")]
    [InlineData(" Active ")]
    public async Task should_bind_an_enum_query_value_by_its_member_name(string value)
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none"),
            request => request.QueryString = new QueryString($"?status={value}")
        );

        (await _ReadTextAsync(context)).Should().Be("Active");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData(" 2")]
    // A [Flags] list may mix a number in among names.
    [InlineData("Active,2")]
    public async Task should_answer_400_naming_the_parameter_when_an_enum_query_value_is_a_number(string value)
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none"),
            request => request.QueryString = new QueryString($"?status={value}")
        );

        await _ShouldBeInvalidParameterAsync(context, "status");
    }

    [Theory]
    [InlineData("bogus")]
    // ASP.NET Core reads the member name case-sensitively and knows no [JsonStringEnumMemberName] alias.
    [InlineData("active")]
    [InlineData("on-hold")]
    public async Task should_leave_a_name_asp_net_core_refuses_to_its_own_400(string value)
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/query", (MemberStatus? status) => status?.ToString() ?? "none"),
            request => request.QueryString = new QueryString($"?status={value}")
        );

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task should_bind_a_flags_combination_from_the_query()
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/access", (Access access) => access.ToString()),
            request => request.QueryString = new QueryString("?access=Read,Write")
        );

        (await _ReadTextAsync(context)).Should().Be("Read, Write");
    }

    [Fact]
    public async Task should_bind_every_enum_of_a_repeated_query_value()
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/list", ([FromQuery] MemberStatus[] status) => string.Join('|', status)),
            request => request.QueryString = new QueryString("?status=Active&status=Inactive")
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

        var named = await _InvokeAsync(map, request => request.Headers["X-Status"] = "Inactive");
        var numbered = await _InvokeAsync(map, request => request.Headers["X-Status"] = "2");

        (await _ReadTextAsync(named)).Should().Be("Inactive");
        await _ShouldBeInvalidParameterAsync(numbered, "X-Status");
    }

    [Fact]
    public async Task should_bind_an_enum_route_value_by_name()
    {
        var context = await _InvokeAsync(
            group => group.MapGet("/route/{status}", (MemberStatus status) => status.ToString()),
            request => request.RouteValues["status"] = "OnHold"
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

        var named = await _InvokeAsync(map, request => _SetForm(request, "Status", "Inactive"));
        var numbered = await _InvokeAsync(map, request => _SetForm(request, "Status", "2"));

        (await _ReadTextAsync(named)).Should().Be("Inactive");
        await _ShouldBeInvalidParameterAsync(numbered, "Status");
    }

    [Fact]
    public async Task should_bind_a_simple_enum_form_value_by_name()
    {
        var context = await _InvokeAsync(
            group => group.MapPost("/form", ([FromForm] MemberStatus status) => status.ToString()).DisableAntiforgery(),
            request => _SetForm(request, "status", "Active")
        );

        (await _ReadTextAsync(context)).Should().Be("Active");
    }

    [Fact]
    public async Task should_leave_a_value_that_is_not_a_number_to_asp_net_core()
    {
        static void map(RouteGroupBuilder group) =>
            group.MapGet("/page", (int page) => page.ToString(CultureInfo.InvariantCulture));

        var valid = await _InvokeAsync(map, request => request.QueryString = new QueryString("?page=2"));
        var invalid = await _InvokeAsync(map, request => request.QueryString = new QueryString("?page=abc"));

        (await _ReadTextAsync(valid)).Should().Be("2");
        invalid.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task should_check_the_members_of_an_as_parameters_argument()
    {
        static void map(RouteGroupBuilder group) =>
            group.MapGet("/filter", ([AsParameters] StatusFilter filter) => $"{filter.Status}:{filter.Page}");

        var valid = await _InvokeAsync(
            map,
            request => request.QueryString = new QueryString("?status=Inactive&page=3")
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
            .RejectEnumNumbers();
        var context = MinimalApiTestHost.CreateHttpContext(services);
        context.Request.QueryString = new QueryString("?status=Inactive");

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
        context.Request.QueryString = new QueryString("?status=1");

        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(context);

        // ASP.NET Core binds a number; the convention is what refuses it.
        (await _ReadTextAsync(context))
            .Should()
            .Be("Active");
    }

    [Theory]
    [InlineData("?status=Active&page=2")]
    [InlineData("?status=Active&status=Inactive&page=2")]
    [InlineData("?status=Active&page=2&tag=x&tag=y&sort=name")]
    public void should_add_no_allocation_to_a_query_spelled_as_the_body_writes_it(string query)
    {
        static void map(IEndpointRouteBuilder builder) =>
            builder.MapGet("/q", ([FromQuery] MemberStatus[] status, int page) => Results.Ok());

        var extra = _ExtraBytesPerRequest(map, request => request.QueryString = new QueryString(query));

        extra.Should().Be(0);
    }

    [Fact]
    public void should_add_no_allocation_to_route_and_header_values_spelled_as_the_body_writes_them()
    {
        static void map(IEndpointRouteBuilder builder) =>
            builder.MapGet(
                "/r/{status}",
                (MemberStatus status, [FromHeader(Name = "X-Access")] Access access) => Results.Ok()
            );

        var extra = _ExtraBytesPerRequest(
            map,
            request =>
            {
                request.RouteValues["status"] = "Inactive";
                request.Headers["X-Access"] = "Read, Write";
            }
        );

        extra.Should().Be(0);
    }

    [Fact]
    public void should_add_no_allocation_to_a_form_spelled_as_the_body_writes_it()
    {
        static void map(IEndpointRouteBuilder builder) =>
            builder.MapPost("/f", ([FromForm] StatusForm form) => Results.Ok()).DisableAntiforgery();

        var extra = _ExtraBytesPerRequest(map, request => _SetForm(request, "Status", "Inactive"));

        extra.Should().Be(0);
    }

    // The bytes one request allocates with the convention, less the bytes the same request allocates without it. Each
    // request gets a fresh context built outside the measured window, and both endpoints are warmed up first. Each side
    // reports its fewest bytes over many requests: a one-off allocation inside the window (a lazy cache, the JIT) can
    // only raise a single request, while a cost the convention adds to every request raises the fewest too.
    private static long _ExtraBytesPerRequest(Action<IEndpointRouteBuilder> map, Action<HttpRequest> arrange)
    {
        using var services = _CreateServices();
        var plain = new TestEndpointRouteBuilder(services);
        map(plain);
        var wrapped = new TestEndpointRouteBuilder(services);
        map(wrapped.MapGroup("").RejectEnumNumbers());

        var withoutConvention = _BytesPerRequest(
            MinimalApiTestHost.SingleEndpoint(plain).RequestDelegate!,
            services,
            arrange
        );
        var withConvention = _BytesPerRequest(
            MinimalApiTestHost.SingleEndpoint(wrapped).RequestDelegate!,
            services,
            arrange
        );

        return withConvention - withoutConvention;
    }

    private static long _BytesPerRequest(
        RequestDelegate endpoint,
        IServiceProvider services,
        Action<HttpRequest> arrange
    )
    {
        const int warmUp = 200;
        const int measured = 500;
        var fewest = long.MaxValue;

        for (var i = 0; i < warmUp + measured; i++)
        {
            var context = MinimalApiTestHost.CreateHttpContext(services);
            arrange(context.Request);
            // The server parses the query before binding either way, so parse it outside the window. DefaultHttpContext
            // creates its request-lifetime feature on first use, where Kestrel's connection already implements it, so
            // touch it outside the window too.
            _ = context.Request.Query.Count;
            _ = context.RequestAborted;

            var before = GC.GetAllocatedBytesForCurrentThread();
            var task = endpoint(context);
            var after = GC.GetAllocatedBytesForCurrentThread();

            task.IsCompletedSuccessfully.Should().BeTrue("the measured endpoints complete synchronously");
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

            if (i >= warmUp)
            {
                fewest = Math.Min(fewest, after - before);
            }
        }

        return fewest;
    }

    private static async Task<DefaultHttpContext> _InvokeAsync(
        Action<RouteGroupBuilder> map,
        Action<HttpRequest> arrange
    )
    {
        // The response is read after the call, which needs no service, so the container ends with it.
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        map(endpoints.MapGroup("").RejectEnumNumbers());
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
