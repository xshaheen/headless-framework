// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
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
        Action<HeadlessOpenApiOptions>? configure = null
    )
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(PlainResultController)))
                    );
                builder.Services.AddHeadlessOpenApi();

                if (configure is not null)
                {
                    builder.Services.ConfigureHeadlessOpenApi(configure);
                }
            },
            app =>
            {
                app.MapControllers();
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
