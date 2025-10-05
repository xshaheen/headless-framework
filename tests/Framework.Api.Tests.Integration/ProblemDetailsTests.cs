// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using FluentAssertions.Extensions;
using Framework.Constants;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ProblemDetailsTests : TestBase
{
    #region Entity Not Found (Middleware rewriter)

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.5.5",
     *   "title" : "endpoint-not-found",
     *   "status" : 404,
     *   "detail" : "The requested endpoint '/minimal/12345678' was not found.",
     *   "instance" : "/minimal/12345678",
     *   "traceId" : "00-f67982d0996492c0607b5cb52cc5fd8b-97a0db435cd3152d-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T20:58:22.4696294+00:00"
     * }
     */

    [Fact]
    public async Task endpoint_not_found()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/12345678", stringContent, AbortToken);
        await _VerifyEndpointNotFound(response);
    }

    private static async Task _VerifyEndpointNotFound(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            "endpoint-not-found",
            404,
            "The requested endpoint was not found."
        );

        jsonElement.EnumerateObject().Count().Should().Be(9);
    }

    #endregion

    #region Malformed Syntax

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.5.1",
     *   "title" : "bad-request",
     *   "status" : 400,
     *   "detail" : "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax.",
     *   "instance" : "/minimal/malformed-syntax",
     *   "traceId" : "00-1402a1325f82be1e9277e334c617d122-5a8c02fbde4b5d93-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-28T15:20:52.9121154+00:00"
     * }
     */

    [Fact]
    public async Task mvc_bad_request()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/mvc/malformed-syntax", AbortToken);
        await _VerifyMalformedSyntax(response);
    }

    [Fact]
    public async Task minimal_api_bad_request()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/minimal/malformed-syntax", AbortToken);
        await _VerifyMalformedSyntax(response);
    }

    private static async Task _VerifyMalformedSyntax(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "bad-request",
            400,
            "Failed to parse. The request body is empty or could not be understood by the server due to malformed syntax."
        );

        jsonElement.EnumerateObject().Count().Should().Be(9);
    }

    #endregion

    #region Entity Not Found

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.5.5",
     *   "title" : "entity-not-found",
     *   "status" : 404,
     *   "detail" : "The requested entity does not exist. There is no entity matches 'Entity:Key'.",
     *   "instance" : "/minimal/entity-not-found",
     *   "params" : {
     *     "entity" : "Entity",
     *     "key" : "Key"
     *   },
     *   "traceId" : "00-4782502e77808f3d0eff63531d30c35b-49c15b7ba6e935a6-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T20:20:27.8143466+00:00"
     * }
     */

    [Fact]
    public async Task minimal_api_entity_not_found()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/entity-not-found", stringContent, AbortToken);
        await _VerifyEntityNotFound(response);
    }

    [Fact]
    public async Task mvc_entity_not_found()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/entity-not-found", stringContent, AbortToken);
        await _VerifyEntityNotFound(response);
    }

    private static async Task _VerifyEntityNotFound(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            "entity-not-found",
            404,
            "The requested entity does not exist. There is no entity matches 'Entity:Key'."
        );

        jsonElement.GetProperty("params").GetProperty("entity").GetString().Should().Be("Entity");
        jsonElement.GetProperty("params").GetProperty("key").GetString().Should().Be("Key");

        jsonElement.EnumerateObject().Count().Should().Be(10);
    }

    #endregion

    #region Conflict

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.5.10",
     *   "title" : "conflict-request",
     *   "status" : 409,
     *   "detail" : "Conflict request",
     *   "instance" : "/minimal/conflict",
     *   "errors" : [ {
     *     "code" : "key",
     *     "description" : "value",
     *     "severity" : "information",
     *     "params" : null
     *   } ],
     *   "traceId" : "00-4cb6fa7facd9168e5149073bc3fdea78-ac6a26b9cf09c4fa-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T18:57:39.2293695+00:00"
     * }
     */

    [Fact]
    public async Task minimal_api_conflict()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/conflict", stringContent, AbortToken);
        await _VerifyConflict(response);
    }

    [Fact]
    public async Task mvc_conflict()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/conflict", stringContent, AbortToken);
        await _VerifyConflict(response);
    }

    private static async Task _VerifyConflict(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            "conflict-request",
            409,
            "Conflict request"
        );

        var errors = jsonElement.GetProperty("errors").EnumerateArray().ToList();
        errors.Count.Should().Be(1);
        _ValidateErrorDescriptor(errors[0]);
        jsonElement.EnumerateObject().Count().Should().Be(10);
    }

    #endregion

    #region Unprocessable

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc4918#section-11.2",
     *   "title" : "validation-problem",
     *   "status" : 422,
     *   "detail" : "One or more validation errors occurred.",
     *   "errors" : {
     *     "property" : [ {
     *       "code" : "Error message",
     *       "description" : "Error message",
     *       "severity" : "information",
     *       "params" : null
     *     } ]
     *   },
     *   "traceId" : "00-b9a104ec955015318f70edcdf19819ce-73494e5484f1a67f-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T19:37:06.4937153+00:00"
     * }
     */

    [Fact]
    public async Task minimal_api_unprocessable()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/unprocessable", stringContent, AbortToken);
        await _VerifyUnprocessable(response);
    }

    [Fact]
    public async Task mvc_unprocessable()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/unprocessable-entity", stringContent, AbortToken);
        await _VerifyUnprocessable(response);
    }

    private static async Task _VerifyUnprocessable(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc4918#section-11.2",
            "validation-problem",
            422,
            "One or more validation errors occurred."
        );

        var errorsObject = jsonElement.GetProperty("errors").EnumerateObject().ToList();
        errorsObject.Should().ContainSingle();
        errorsObject[0].Name.Should().Be("property");
        var propertyErrorsArray = errorsObject[0].Value.EnumerateArray().ToList();
        propertyErrorsArray.Count.Should().Be(1);
        var error = propertyErrorsArray[0];
        _ValidateErrorDescriptor(error);
        jsonElement.EnumerateObject().Count().Should().Be(10);
    }

    #endregion

    #region Internal Error (UseExceptionHandler)

    // UseExceptionHandler

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.6.1",
     *   "title" : "unhandled-exception",
     *   "status" : 500,
     *   "detail" : "An error occurred while processing your request.",
     *   "instance" : "/minimal/internal-error",
     *   "traceId" : "00-eb923ba0d504831499a6fa0e0bfc56da-a2246692e80f630e-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T20:50:45.1595209+00:00"
     * }
     */

    // UseDeveloperExceptionHandler

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.6.1",
     *   "title" : "System.InvalidOperationException",
     *   "status" : 500,
     *   "detail" : "This is a test exception.",
     *   "instance" : "/minimal/internal-error",
     *   "exception" : {
     *     "details" : "System.InvalidOperationException: This is a test exception.\r\n   at Framework.Api.Demo.Endpoints.ProblemsEndpoints.<>c.<MapProblemsEndpoints>b__0_2() in D:\\Dev\\framework\\cs-framework\\demo\\Framework.Api.Demo\\Endpoints\\ProblemsEndpoints.cs:line 32\r\n   at lambda_method48(Closure, EndpointFilterInvocationContext)\r\n   at Microsoft.AspNetCore.Builder.MinimalApiExceptionFilter.InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) in D:\\Dev\\framework\\cs-framework\\src\\Framework.Api.MinimalApi\\Filters\\MinimalApiExceptionFilter.cs:line 43\r\n   at Microsoft.AspNetCore.Http.RequestDelegateFactory.<ExecuteValueTaskOfObject>g__ExecuteAwaited|128_0(ValueTask`1 valueTask, HttpContext httpContext, JsonTypeInfo`1 jsonTypeInfo)\r\n   at NSwag.AspNetCore.Middlewares.SwaggerUiIndexMiddleware.Invoke(HttpContext context)\r\n   at NSwag.AspNetCore.Middlewares.RedirectToIndexMiddleware.Invoke(HttpContext context)\r\n   at NSwag.AspNetCore.Middlewares.OpenApiDocumentMiddleware.Invoke(HttpContext context)\r\n   at Framework.Api.Middlewares.StatusCodesRewriterMiddleware.InvokeAsync(HttpContext context, RequestDelegate next) in D:\\Dev\\framework\\cs-framework\\src\\Framework.Api\\Middlewares\\StatusCodesRewriterMiddleware.cs:line 13\r\n   at Microsoft.AspNetCore.Builder.UseMiddlewareExtensions.InterfaceMiddlewareBinder.<>c__DisplayClass2_0.<<CreateMiddleware>b__0>d.MoveNext()\r\n--- End of stack trace from previous location ---\r\n   at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.Invoke(HttpContext context)",
     *     "headers" : {
     *       "content-Type" : [ "text/plain; charset=utf-8" ],
     *       "content-Length" : [ "0" ],
     *       "host" : [ "localhost" ]
     *     },
     *     "path" : "/minimal/internal-error",
     *     "endpoint" : "HTTP: POST minimal/internal-error",
     *     "routeValues" : null
     *   },
     *   "traceId" : "00-988ce453442fc521ec4ed1e5aba288a6-de267659253f8c4d-00",
     *   "buildNumber" : "2.16.1.109",
     *   "commitNumber" : "2.16.1.109",
     *   "timestamp" : "2025-01-29T21:59:34.4677538+00:00"
     * }
     */

    [Fact]
    public async Task minimal_api_internal_error()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/internal-error", stringContent, AbortToken);
        await _VerifyInternalServerError(response);
    }

    [Fact]
    public async Task mvc_internal_error()
    {
        await using var factory = await _CreateDefaultFactory();
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/internal-error", stringContent, AbortToken);
        await _VerifyInternalServerError(response);
    }

    private static async Task _VerifyInternalServerError(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            "System.InvalidOperationException",
            500,
            "This is a test exception."
        );

        jsonElement.GetProperty("exception").EnumerateObject().Should().HaveCountGreaterThan(0);
        jsonElement.EnumerateObject().Count().Should().Be(10);
    }

    #endregion

    #region Helpers

    private static void _ValidateErrorDescriptor(JsonElement error)
    {
        error.GetProperty("code").GetString().Should().Be("error-code");
        error.GetProperty("description").GetString().Should().Be("Error message");
        error.GetProperty("severity").GetString().Should().Be("information");
        error.GetProperty("params").ValueKind.Should().Be(JsonValueKind.Null);
        error.EnumerateObject().Count().Should().Be(4);
    }

    private static void _ValidateCoreProblemDetails(
        JsonElement jsonElement,
        HttpResponseMessage response,
        string type,
        string title,
        int status,
        string detail
    )
    {
        jsonElement.GetProperty("type").GetString().Should().Be(type);
        jsonElement.GetProperty("title").GetString().Should().Be(title);
        jsonElement.GetProperty("status").GetInt32().Should().Be(status);
        jsonElement.GetProperty("detail").GetString().Should().Be(detail);
        jsonElement.GetProperty("instance").GetString().Should().Be(response.RequestMessage!.RequestUri!.PathAndQuery);
        jsonElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("buildNumber").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("commitNumber").GetString().Should().NotBeNullOrWhiteSpace();
        jsonElement.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, 1.Seconds());
    }

    private async Task<WebApplicationFactory<Program>> _CreateDefaultFactory(
        Action<WebHostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IWebHostBuilder>? configureHost = null
    )
    {
        await using var factory = new WebApplicationFactory<Program>();

        factory.ClientOptions.AllowAutoRedirect = false;

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(EnvironmentNames.Development);
            builder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders().AddProvider(LoggerProvider));
            configureHost?.Invoke(builder);

            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }

    #endregion
}
