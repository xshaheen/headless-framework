// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using Headless.Api;
using Headless.Context;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Validation;

/// <summary>
/// Pins that an MVC action argument with a registered FluentValidation validator is validated before the action runs,
/// answering one 422 with every failure, as a Minimal API endpoint under ValidateArguments() does.
/// </summary>
public sealed class ArgumentValidationActionFilterTests : TestBase
{
    [Fact]
    public async Task should_answer_422_with_every_failure_when_an_argument_is_invalid()
    {
        await using var app = await _StartAsync();

        using var response = await app.GetTestClient()
            .PostAsJsonAsync("/orders", new { name = "", quantity = 0 }, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var errors = body.RootElement.GetProperty("errors").EnumerateObject().Select(x => x.Name.ToUpperInvariant());
        errors.Should().BeEquivalentTo(["NAME", "QUANTITY"]);
    }

    [Fact]
    public async Task should_run_the_action_when_the_argument_is_valid()
    {
        await using var app = await _StartAsync();

        using var response = await app.GetTestClient()
            .PostAsJsonAsync("/orders", new { name = "Lamp", quantity = 2 }, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("Lamp x2");
    }

    [Fact]
    public async Task should_run_an_action_whose_arguments_have_no_validator()
    {
        await using var app = await _StartAsync();

        var echoed = await app.GetTestClient().GetStringAsync("/orders/echo?text=hi", AbortToken);

        echoed.Should().Be("hi");
    }

    [Fact]
    public async Task should_answer_the_binding_failure_before_any_validator_runs()
    {
        await using var app = await _StartAsync();

        using var response = await app.GetTestClient()
            .PostAsJsonAsync("/orders", new { name = "Lamp", quantity = "many" }, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_not_validate_a_request_whose_binding_failed_on_a_plain_controller()
    {
        await using var app = await _StartAsync();

        // Without [ApiController] the binding failure reaches the action; validating the defaults it left would answer
        // a 422 about values the client never sent.
        using var response = await app.GetTestClient()
            .PostAsJsonAsync("/plain-orders", new { name = "", quantity = "many" }, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("model state invalid");
    }

    [Fact]
    public async Task should_give_each_host_its_own_filter()
    {
        await using var first = await _StartAsync();
        await using var second = await _StartAsync();

        static object filter(WebApplication app) =>
            app
                .Services.GetRequiredService<IOptions<MvcOptions>>()
                .Value.Filters.Single(x =>
                    string.Equals(x.GetType().Name, "ArgumentValidationActionFilter", StringComparison.Ordinal)
                );

        filter(first).Should().NotBeSameAs(filter(second));
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
        builder.Services.AddSingleton<IValidator<CreateOrder>, CreateOrderValidator>();
        builder
            .Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.FeatureProviders.Add(new OrdersControllerFeature());
            });
        builder.Services.ConfigureHeadlessMvc();

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(AbortToken);

        return app;
    }

    private sealed class OrdersControllerFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Add(typeof(OrdersController).GetTypeInfo());
            feature.Controllers.Add(typeof(PlainOrdersController).GetTypeInfo());
        }
    }
}

public sealed record CreateOrder(string Name, int Quantity);

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

[NonController]
[ApiController]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost]
    public string Create(CreateOrder order) => $"{order.Name} x{order.Quantity}";

    [HttpGet("echo")]
    public string Echo([FromQuery] string text) => text;
}

#pragma warning disable AV0014 // The fix would defeat the test: this controller has no [ApiController] on purpose.
[NonController]
[Route("plain-orders")]
public sealed class PlainOrdersController : ControllerBase
{
    [HttpPost]
    public string Create([FromBody] CreateOrder order) => ModelState.IsValid ? order.Name : "model state invalid";
}
#pragma warning restore AV0014
