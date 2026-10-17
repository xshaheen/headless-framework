// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Testing.Tests;

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
}
