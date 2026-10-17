// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Asp.Versioning;
using FluentValidation;
using Headless.Api;
using Headless.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A small Minimal API and MVC surface exercising every Headless enrichment.</summary>
internal static class SampleApi
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder
            .Services.AddControllers(options => options.Conventions.Add(new FilterSecuredConvention()))
            .AddApplicationPart(typeof(ProductsController).Assembly);
        builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();
        builder.Services.AddScoped<IValidator<OrderQuery>, OrderQueryValidator>();
        builder.Services.AddScoped<IValidator<ProductQuery>, ProductQueryValidator>();
        builder.Services.AddRateLimiter(options => options.UseHeadlessProblemDetails());
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

        app.MapGet("/tenant/plain", () => TypedResults.Ok("plain")).RequireTenant().RequireAuthorization();

        app.MapGet("/tenant/optional", () => TypedResults.Ok("optional"))
            .WithMetadata(new RequireTenantAttribute(), new AllowMissingTenantAttribute())
            .RequireAuthorization("orders.read");

        app.MapGet("/unnamed/items/{id}", (string id) => TypedResults.Ok(id));
        app.MapGet("/v{version}/things", (string version) => TypedResults.Ok(version));

        app.MapGet("/legacy", () => TypedResults.Ok("legacy")).WithMetadata(new ObsoleteAttribute());

        app.MapPost("/register", (RegisterRequest request) => TypedResults.Ok(request.UserName)).AllowAnonymous();

        // Policies built inline reach the endpoint as AuthorizationPolicy metadata, with no named policy or roles.
        app.MapGet("/policies/claims", () => TypedResults.Ok("claims"))
            .RequireAuthorization(policy => policy.RequireClaim("scope", "orders.write"));
        app.MapGet("/policies/name", () => TypedResults.Ok("name"))
            .RequireAuthorization(policy => policy.RequireUserName("admin"));
        app.MapGet("/policies/assertion", () => TypedResults.Ok("assertion"))
            .RequireAuthorization(policy => policy.RequireAssertion(_ => true));
        app.MapGet("/policies/authenticated", () => TypedResults.Ok("authenticated"))
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser());
        // The authorization middleware also enforces a policy attached as bare metadata, with no IAuthorizeData beside it.
        app.MapGet("/policies/metadata-only", () => TypedResults.Ok("metadata-only"))
            .WithMetadata(new AuthorizationPolicyBuilder().RequireClaim("scope", "orders.audit").Build());

        var v1 = new ApiVersion(1, 0);
        var v2 = new ApiVersion(2, 0);
        app.MapGet("/versioned/old", () => TypedResults.Ok("old"))
            .WithMetadata(new ApiVersionMetadata(ApiVersionModel.Empty, new ApiVersionModel([v2], [v1])));
        app.MapGet("/versioned/current", () => TypedResults.Ok("current"))
            .WithMetadata(new ApiVersionMetadata(ApiVersionModel.Empty, new ApiVersionModel([v2], [])));

        app.MapGet("/limited", () => TypedResults.Ok("limited")).RequireRateLimiting("fixed").AllowAnonymous();
        app.MapGet("/limited/disabled", () => TypedResults.Ok("disabled"))
            .RequireRateLimiting("fixed")
            .DisableRateLimiting()
            .AllowAnonymous();

        app.MapGet("/errors", () => TypedResults.Ok("errors"))
            .ProducesProblem(StatusCodes.Status408RequestTimeout)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        app.MapPost("/validated", (RegisterRequest request) => TypedResults.Ok(request.UserName))
            .ProducesValidationProblem()
            .AllowAnonymous();

        app.MapGet("/accounts/{account}", (AccountId account, [FromQuery] Month month) => TypedResults.Ok(account))
            .AllowAnonymous();

        app.MapGet("/envelopes/nullable", () => TypedResults.Ok(new DataEnvelope<string?>(null)));
        app.MapGet("/envelopes/plain", () => TypedResults.Ok(new DataEnvelope<string>("x")));
    }
}

public enum OrderStatus
{
    Pending = 0,
    Shipped = 1,
}

public sealed class OrderView : IHasEntityTag
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

    public Address? ShippingAddress { get; init; }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long ExternalId { get; init; }

    public EntityTag GetEntityTag() => EntityTag.FromUInt32((uint)Count);
}

public sealed class Address
{
    public required string Street { get; init; }
}

public sealed class OrderQuery
{
    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public string? SortBy { get; init; }

    public string[] Tags { get; init; } = [];
}

public sealed class OrderQueryValidator : AbstractValidator<OrderQuery>
{
    public OrderQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Tags).NotEmpty();
        RuleForEach(x => x.Tags).MaximumLength(20);
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

public sealed class RegisterRequest
{
    public string UserName { get; init; } = "";

    public string? Nickname { get; init; }

    public bool HasNickname { get; init; }

    public string Pin { get; init; } = "";

    public string Password { get; init; } = "";

    public string Code { get; init; } = "";
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        Include(new RegisterAccountValidator());
        When(x => x.HasNickname, () => Include(new RegisterNicknameValidator()));
        RuleFor(x => x.Pin).Length(4);
        RuleFor(x => x.Password).MinimumLength(8);
        RuleFor(x => x.Code).NotEmpty().MinimumLength(4);
    }
}

public sealed class RegisterAccountValidator : AbstractValidator<RegisterRequest>
{
    public RegisterAccountValidator()
    {
        RuleFor(x => x.UserName).MaximumLength(30);
    }
}

public sealed class RegisterNicknameValidator : AbstractValidator<RegisterRequest>
{
    public RegisterNicknameValidator()
    {
        RuleFor(x => x.Nickname).MaximumLength(15);
    }
}

public sealed class CatalogSearch
{
    [Display(Description = "Text to search for.")]
    public string? Term { get; init; }

    [BindRequired]
    public int Page { get; init; }
}

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController : ControllerBase
{
    [HttpGet("legacy")]
    public Ok<string> Find2(string legacy) => TypedResults.Ok(legacy);

    [HttpGet("by-name")]
    public Ok<string> Find(string name) => TypedResults.Ok(name);

    [HttpGet("by-code")]
    [ActionName("Find")]
    public Ok<string> FindByCode(string code) => TypedResults.Ok(code);

    [HttpGet("search")]
    public Ok<int> Search([FromQuery] CatalogSearch search) => TypedResults.Ok(search.Page);
}

/// <summary>Protected only by MVC authorization filters, which never reach the endpoint metadata.</summary>
[ApiController]
[Route("api/filter-secured")]
public sealed class FilterSecuredController : ControllerBase
{
    [HttpGet("status")]
    public Ok<string> Status() => TypedResults.Ok("status");

    [HttpGet("manage")]
    public Ok<string> Manage() => TypedResults.Ok("manage");

    [HttpGet("ping")]
    [AllowAnonymous]
    public Ok<string> Ping() => TypedResults.Ok("ping");
}

internal sealed class FilterSecuredConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType != typeof(FilterSecuredController))
        {
            return;
        }

        controller.Filters.Add(
            new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        );

        foreach (
            var action in controller.Actions.Where(action =>
                string.Equals(action.ActionName, "Manage", StringComparison.Ordinal)
            )
        )
        {
            action.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireRole("ops").Build()));
        }
    }
}
