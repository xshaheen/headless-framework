// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning;
using FluentValidation;
using Headless.Api;
using Headless.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A small Minimal API and MVC surface exercising every Headless enrichment.</summary>
internal static class SampleApi
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddApplicationPart(typeof(ProductsController).Assembly);
        builder.Services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();
        builder.Services.AddScoped<IValidator<OrderQuery>, OrderQueryValidator>();
        builder.Services.AddScoped<IValidator<ProductQuery>, ProductQueryValidator>();
    }

    public static void Map(WebApplication app)
    {
        app.MapControllers();

        app.MapGet("/orders/{id:int}", (int id) => TypedResults.Ok(new OrderView()))
            .WithName("GetOrder")
            .RequireAuthorization("orders.read");

        app.MapGet("/orders", ([AsParameters] OrderQuery query) => TypedResults.Ok(query.PageIndex)).AllowAnonymous();

        app.MapPost("/orders", (CreateOrderRequest request) => TypedResults.Ok(request.Name))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();

        app.MapPut("/orders/{id:int}", (int id) => TypedResults.NoContent())
            .WithMetadata(new RequireIfMatchAttribute())
            .RequireAuthorization();

        app.MapPut(
                "/orders/{id:int}/bound",
                (int id, [FromHeader(Name = "If-Match")] string? ifMatch) =>
                    TypedResults.Ok(ifMatch ?? id.ToString(CultureInfo.InvariantCulture))
            )
            .WithMetadata(new RequireIfMatchAttribute());

        app.MapGet("/tenant/orders", () => TypedResults.Ok("tenant"))
            .WithMetadata(new RequireTenantAttribute())
            .RequireAuthorization("orders.read");

        app.MapGet("/tenant/optional", () => TypedResults.Ok("optional"))
            .WithMetadata(new RequireTenantAttribute(), new AllowMissingTenantAttribute())
            .RequireAuthorization("orders.read");

        app.MapGet("/unnamed/items/{id}", (string id) => TypedResults.Ok(id));

        app.MapGet("/legacy", () => TypedResults.Ok("legacy")).WithMetadata(new ObsoleteAttribute());

        var v1 = new ApiVersion(1, 0);
        var v2 = new ApiVersion(2, 0);
        app.MapGet("/versioned/old", () => TypedResults.Ok("old"))
            .WithMetadata(new ApiVersionMetadata(ApiVersionModel.Empty, new ApiVersionModel([v2], [v1])));
        app.MapGet("/versioned/current", () => TypedResults.Ok("current"))
            .WithMetadata(new ApiVersionMetadata(ApiVersionModel.Empty, new ApiVersionModel([v2], [])));

        app.MapGet("/envelopes/nullable", () => TypedResults.Ok(new DataEnvelope<string?>(null)));
        app.MapGet("/envelopes/plain", () => TypedResults.Ok(new DataEnvelope<string>("x")));
    }
}

public enum OrderStatus
{
    Pending = 0,
    Shipped = 1,
}

public sealed class OrderView
{
    public int Count { get; init; }

    public int? OptionalCount { get; init; }

    public decimal Total { get; init; }

    public string Plain { get; init; } = "";

    public string? Note { get; init; }

    public MoneyAmount Price { get; init; }

    public MoneyAmount? Discount { get; init; }

    public Month Month { get; init; }

    public AccountId Account { get; init; } = null!;

    public UserId? Owner { get; init; }

    public OrderStatus Status { get; init; }

    public List<int> Items { get; init; } = [];
}

public sealed class OrderQuery
{
    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public string? SortBy { get; init; }
}

public sealed class OrderQueryValidator : AbstractValidator<OrderQuery>
{
    public OrderQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class CreateOrderRequest
{
    public string? Name { get; init; }

    public string? Code { get; init; }

    public int Age { get; init; }

    public decimal Score { get; init; }

    public string Email { get; init; } = "";

    public List<int> Scores { get; init; } = [];

    public string? Note { get; init; }

    public string? Comment { get; init; }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Name).NotNull().Length(2, 10).Matches("^[a-z]+$");
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Age).GreaterThanOrEqualTo(18).LessThan(100);
        RuleFor(x => x.Score).ExclusiveBetween(1m, 10m);
        RuleFor(x => x.Email).EmailAddress();
        RuleForEach(x => x.Scores).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(50).When(x => x.Age > 20);
    }
}

public sealed record DataEnvelope<T>(T Data);

public sealed class ProductQuery
{
    public int PageIndex { get; init; }

    public int PageSize { get; init; }
}

public sealed class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    public ProductQueryValidator()
    {
        RuleFor(x => x.PageSize).LessThanOrEqualTo(50);
    }
}

public sealed class ProductView
{
    public required string Name { get; init; }

    public string? Description { get; init; }
}

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProductView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetAsync(int id) =>
        Task.FromResult<IActionResult>(Ok(new ProductView { Name = id.ToString(CultureInfo.InvariantCulture) }));

    [HttpGet(Name = "ListProducts")]
    public Ok<int> List([FromQuery] ProductQuery query) => TypedResults.Ok(query.PageSize);

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public Results<NoContent, NotFound> Delete(int id) => id > 0 ? TypedResults.NoContent() : TypedResults.NotFound();

    [HttpGet("find/by-name")]
    public Ok<string> Find(string name) => TypedResults.Ok(name);

    [HttpGet("find/by-code")]
    [ActionName("Find")]
    public Ok<string> FindByCode(string code) => TypedResults.Ok(code);
}
