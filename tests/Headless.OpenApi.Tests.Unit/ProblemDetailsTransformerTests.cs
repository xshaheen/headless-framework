// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ProblemDetailsTransformerTests : TestBase
{
    private const string _Diagnostics = """
        "traceId": "<trace-id>",
        "buildNumber": "<version>",
        "commitNumber": "<commit>",
        "timestamp": "2024-01-01T12:00:00+00:00"
        """;

    public static TheoryData<string, string, string, string> Examples =>
        new()
        {
            {
                "/orders",
                "post",
                "400",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                      "title": "bad-request",
                      "status": 400,
                      "detail": "The request could not be read. Its body is empty or malformed.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders/{id}",
                "get",
                "401",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                      "title": "unauthorized",
                      "status": 401,
                      "detail": "You must be authenticated to access this resource.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders/{id}",
                "get",
                "403",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                      "title": "forbidden",
                      "status": 403,
                      "detail": "You do not have permission to access this resource.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/api/products/{id}",
                "get",
                "404",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                      "title": "entity-not-found",
                      "status": 404,
                      "detail": "The requested resource was not found.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders",
                "post",
                "409",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                      "title": "conflict-request",
                      "status": 409,
                      "detail": "The request violates one or more business rules.",
                      "instance": "/public/some-endpoint",
                      "errors": [{ "code": "business_error", "description": "Some business rule failed." }],
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders",
                "post",
                "422",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
                      "title": "validation-problem",
                      "status": 422,
                      "detail": "One or more validation errors occurred.",
                      "instance": "/public/some-endpoint",
                      "errors": {
                        "email": [
                          { "code": "auth:invalid_email_format", "description": "The email address format is invalid." },
                          { "code": "auth:email_already_exists", "description": "The specified email address is already in use." }
                        ],
                        "username": [
                          { "code": "auth:username_too_short", "description": "The username must be at least 6 characters long." }
                        ]
                      },
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders/{id}",
                "put",
                "428",
                $$"""
                    {
                      "type": "about:blank",
                      "title": "Precondition Required",
                      "status": 428,
                      "detail": "A strong If-Match entity tag is required.",
                      "instance": "/public/some-endpoint",
                      "error": { "code": "g:if_match_required", "description": "A strong If-Match entity tag is required." },
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/orders",
                "post",
                "429",
                $$"""
                    {
                      "type": "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
                      "title": "too-many-requests",
                      "status": 429,
                      "detail": "Too many requests. Try again later.",
                      "instance": "/public/some-endpoint",
                      "retryAfter": 60,
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/errors",
                "get",
                "408",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.9",
                      "title": "request-timeout",
                      "status": 408,
                      "detail": "The request timed out.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/errors",
                "get",
                "413",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.14",
                      "title": "payload-too-large",
                      "status": 413,
                      "detail": "The request payload is too large to process.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/errors",
                "get",
                "500",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                      "title": "unhandled-exception",
                      "status": 500,
                      "detail": "An error occurred while processing your request.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/errors",
                "get",
                "501",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.6.2",
                      "title": "not-implemented",
                      "status": 501,
                      "detail": "This functionality is not implemented.",
                      "instance": "/public/some-endpoint",
                      {{_Diagnostics}}
                    }
                    """
            },
            {
                "/errors",
                "get",
                "503",
                $$"""
                    {
                      "type": "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                      "title": "service-unavailable",
                      "status": 503,
                      "detail": "The service is temporarily unavailable. Try again later.",
                      "instance": "/public/some-endpoint",
                      "retryAfter": 30,
                      {{_Diagnostics}}
                    }
                    """
            },
        };

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task should_publish_the_worked_problem_example_for_each_error_status(
        string path,
        string method,
        string status,
        string expectedExample
    )
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

        var example = document.Operation(path, method)["responses"]![status]!["content"]!["application/problem+json"]![
            "example"
        ];

        var expected = JsonNode.Parse(expectedExample);
        JsonNode.DeepEquals(example, expected).Should().BeTrue($"the {status} example is {example?.ToJsonString()}");
    }

    [Fact]
    public async Task should_keep_a_problem_type_the_endpoint_declared_and_type_the_generic_ones()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
                app.MapPost("/reservations", () => TypedResults.NoContent())
                    .Produces<SeatTakenProblem>(StatusCodes.Status409Conflict, "application/problem+json")
                    .ProducesProblem(StatusCodes.Status404NotFound)
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        var responses = document.Operation("/reservations", "post")["responses"]!;

        responses["409"]!
            ["content"]
            .ShouldBeJson(
                """
                {
                  "application/problem+json": {
                    "schema": { "$ref": "#/components/schemas/SeatTakenProblem" }
                  }
                }
                """
            );
        document.Schema("SeatTakenProblem")["properties"]!["seat"].Should().NotBeNull();

        var notFound = responses["404"]!["content"]!["application/problem+json"]!;
        ((string?)notFound["schema"]!["$ref"]).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
        notFound["example"].Should().NotBeNull();
    }
}

/// <summary>A problem body an endpoint writes itself, which the document must not replace.</summary>
public sealed class SeatTakenProblem
{
    public required string Type { get; init; }

    public required string Seat { get; init; }
}
