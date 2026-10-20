// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.OpenApi;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Pins the 404/409/422 problem responses every operation documents, whatever its handler returns.</summary>
public sealed class StandardProblemResponseDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync(
        Action<WebApplication> mapEndpoints,
        Action<HeadlessOpenApiOptions>? configure = null,
        bool controllersOff = false
    )
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                    {
                        manager.FeatureProviders.Add(
                            new SingleControllerFeatureProvider(typeof(PlainResultController))
                        );
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(OptOutController)));
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(CompareController)));
                    });
                builder.Services.AddHeadlessOpenApi();

                if (configure is not null)
                {
                    builder.Services.ConfigureHeadlessOpenApi(configure);
                }
            },
            app =>
            {
                var controllers = app.MapControllers();

                if (controllersOff)
                {
                    controllers.WithoutStandardProblemResponses();
                }

                mapEndpoints(app);
            },
            "v1",
            AbortToken
        );
    }

    [Fact]
    public async Task should_document_the_standard_problems_on_plain_result_endpoints()
    {
        // given a Minimal API endpoint and an MVC action that return a plain value and fail by throwing
        var document = await _GetDocumentAsync(app =>
            app.MapGet("/minimal/plain/{id:int}", (int id) => TypedResults.Ok(id)).AllowAnonymous()
        );

        // then both document the statuses the exception handler writes for not-found, conflict, and validation failures
        foreach (var (path, method) in new[] { ("/minimal/plain/{id}", "get"), ("/mvc/plain/{id}", "get") })
        {
            var responses = document.Operation(path, method)["responses"]!;

            _ProblemSchemaRef(responses["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
            _ProblemSchemaRef(responses["409"]!).Should().Be("#/components/schemas/ConflictProblemDetails");
            _ProblemSchemaRef(responses["422"]!).Should().Be("#/components/schemas/UnprocessableEntityProblemDetails");
        }
    }

    [Fact]
    public async Task should_not_document_the_standard_problems_when_turned_off_api_wide()
    {
        // given the baseline turned off for the whole API
        var document = await _GetDocumentAsync(
            app => app.MapGet("/minimal/plain/{id:int}", (int id) => TypedResults.Ok(id)).AllowAnonymous(),
            options => options.StandardProblemResponses = false
        );

        // then neither endpoint lists them, and the statuses its route facts produce remain
        foreach (var (path, method) in new[] { ("/minimal/plain/{id}", "get"), ("/mvc/plain/{id}", "get") })
        {
            var codes = _ResponseCodes(document.Operation(path, method));

            codes.Should().NotContain(["404", "409", "422"]);
            codes.Should().Contain("400");
        }
    }

    [Fact]
    public async Task should_keep_a_status_the_endpoint_already_documents()
    {
        // given an endpoint that declares its own 404 body
        var document = await _GetDocumentAsync(app =>
            app.MapGet("/minimal/lookup/{id:int}", (int id) => TypedResults.Ok(id))
                .Produces<string>(StatusCodes.Status404NotFound, "text/plain")
                .AllowAnonymous()
        );

        // then its own body survives beside the problem body, and its description is not replaced
        var notFound = document.Operation("/minimal/lookup/{id}", "get")["responses"]!["404"]!;
        ((string?)notFound["content"]!["text/plain"]!["schema"]!["type"]).Should().Be("string");
        ((string?)notFound["description"]).Should().Be("Not Found");
    }

    [Fact]
    public async Task should_document_a_plain_result_action_and_an_api_result_endpoint_alike()
    {
        // given an authenticated MVC action that binds a body and returns a plain value, and the same route on Minimal
        // API returning an ApiResult
        var document = await _GetDocumentAsync(app =>
            app.MapPost(
                    "/minimal/compare/{id:int}",
                    (int id, ComparePayload payload) => ApiResult<int>.Ok(id + payload.Count).ToHttpResult()
                )
                .RequireAuthorization()
        );

        // then both document the same responses
        var minimal = _ResponseCodes(document.Operation("/minimal/compare/{id}", "post"));
        var mvc = _ResponseCodes(document.Operation("/mvc/compare/{id}", "post"));

        minimal.Should().BeEquivalentTo(mvc);
        minimal.Should().BeEquivalentTo(["200", "400", "401", "404", "409", "413", "422"]);
    }

    [Fact]
    public async Task should_let_an_endpoint_turn_the_standard_problems_back_on_inside_a_group_that_turns_them_off()
    {
        var document = await _GetDocumentAsync(app =>
        {
            var group = app.MapGroup("/ops").WithoutStandardProblemResponses().AllowAnonymous();
            group.MapGet("/inherited", () => "ok");
            group.MapGet("/restored", () => "ok").WithStandardProblemResponses();
        });

        _ResponseCodes(document.Operation("/ops/inherited", "get")).Should().NotContain(["404", "409", "422"]);
        _ResponseCodes(document.Operation("/ops/restored", "get")).Should().Contain(["404", "409", "422"]);
    }

    [Fact]
    public async Task should_leave_the_ranges_on_an_endpoint_that_turns_the_standard_problems_off()
    {
        // given the API-wide default and one health check marked off
        var document = await _GetDocumentAsync(app =>
            app.MapGet("/health", () => "ok").WithoutStandardProblemResponses().AllowAnonymous()
        );

        var codes = document.Operation("/health", "get")["responses"]!.AsObject().Select(x => x.Key).ToArray();

        codes.Should().NotContain(["404", "409", "422"]);
        codes.Should().Contain(["4XX", "5XX"]);
    }

    [Fact]
    public async Task should_let_an_action_override_its_controller()
    {
        var document = await _GetDocumentAsync(_ => { });

        _ResponseCodes(document.Operation("/mvc/opt-out/inherited", "get")).Should().NotContain(["404", "409", "422"]);
        _ResponseCodes(document.Operation("/mvc/opt-out/restored", "get")).Should().Contain(["404", "409", "422"]);
    }

    [Fact]
    public async Task should_let_an_action_marker_win_over_a_controller_convention()
    {
        // given MapControllers() turning the standard problems off for every action
        var document = await _GetDocumentAsync(_ => { }, controllersOff: true);

        // then an unmarked action follows the convention, and an action marked on keeps them
        _ResponseCodes(document.Operation("/mvc/plain/{id}", "get")).Should().NotContain(["404", "409", "422"]);
        _ResponseCodes(document.Operation("/mvc/opt-out/restored", "get")).Should().Contain(["404", "409", "422"]);
    }

    [Fact]
    public async Task should_follow_the_surface_setting_unless_an_endpoint_overrides_it()
    {
        var document = await _GetDocumentAsync(app =>
        {
            var off = app.MapGroup("/internal").WithMetadata(_Surface("internal", standardProblemResponses: false));
            off.MapGet("/inherited", () => "ok").AllowAnonymous();
            off.MapGet("/restored", () => "ok").WithStandardProblemResponses().AllowAnonymous();
        });

        _ResponseCodes(document.Operation("/internal/inherited", "get")).Should().NotContain(["404", "409", "422"]);
        _ResponseCodes(document.Operation("/internal/restored", "get")).Should().Contain(["404", "409", "422"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(true, true)]
    public async Task should_let_a_surface_inherit_or_override_the_api_wide_setting(
        bool? surfaceSetting,
        bool documented
    )
    {
        // given the API-wide default turned off
        var document = await _GetDocumentAsync(
            app =>
                app.MapGroup("/portal")
                    .WithMetadata(_Surface("portal", surfaceSetting))
                    .MapGet("/item", () => "ok")
                    .AllowAnonymous(),
            options => options.StandardProblemResponses = false
        );

        var codes = _ResponseCodes(document.Operation("/portal/item", "get"));

        if (documented)
        {
            codes.Should().Contain(["404", "409", "422"]);
        }
        else
        {
            codes.Should().NotContain(["404", "409", "422"]);
        }
    }

    [Fact]
    public async Task should_apply_a_registered_surface_setting_to_minimal_api_and_mvc_endpoints()
    {
        // given a surface registered with the standard problems off, reached through MapApiSurface and [ApiSurface]
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddHeadlessApiSurface("ops", ops => ops.OpenApi.StandardProblemResponses = false);
                builder.Services.AddHeadlessMvcApiSurfaces();
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(OpsSurfaceController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapControllers();
                app.MapApiSurface("ops", ops => ops.MapGet("/ops/minimal", () => "ok").AllowAnonymous());
            },
            "v1",
            AbortToken
        );

        // then both operations drop 404/409/422 and keep the ranges
        foreach (var path in new[] { "/ops/minimal", "/mvc/ops" })
        {
            var responses = document.Operation(path, "get")["responses"]!;
            responses.ExactStatusCodes().Should().NotContain(["404", "409", "422"], path);
            responses.AsObject().Select(x => x.Key).Should().Contain(["4XX", "5XX"], path);
        }
    }

    private static ApiSurfaceDescriptor _Surface(string name, bool? standardProblemResponses) =>
        new(
            name,
            routePrefix: null,
            defaultAuthorizationPolicy: null,
            ApiSurfaceTenancyMode.Unspecified,
            new ApiSurfaceOpenApiDescriptor(name, $"{name} API") { StandardProblemResponses = standardProblemResponses }
        );

    private static string[] _ResponseCodes(JsonNode operation) => operation["responses"]!.ExactStatusCodes();

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}

[NonController]
[ApiController]
[Route("mvc/plain")]
public sealed class PlainResultController : ControllerBase
{
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public ActionResult<int> Get(int id) => id;
}

[NonController]
[ApiController]
[ApiSurface("ops")]
[Route("mvc/ops")]
public sealed class OpsSurfaceController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ActionResult<int> Get() => 1;
}

[NonController]
[ApiController]
[Route("mvc/opt-out")]
[StandardProblemResponses(false)]
public sealed class OptOutController : ControllerBase
{
    [HttpGet("inherited")]
    [AllowAnonymous]
    public string Inherited() => "ok";

    [HttpGet("restored")]
    [AllowAnonymous]
    [StandardProblemResponses]
    public string Restored() => "ok";
}

public sealed record ComparePayload(int Count);

[NonController]
[ApiController]
[Route("mvc/compare")]
public sealed class CompareController : ControllerBase
{
    [HttpPost("{id:int}")]
    [Authorize]
    public ActionResult<int> Post(int id, [FromBody] ComparePayload payload) => id + payload.Count;
}
