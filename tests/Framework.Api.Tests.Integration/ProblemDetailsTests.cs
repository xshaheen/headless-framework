// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions.Extensions;
using Framework.Constants;
using Framework.Http;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ProblemDetailsTests : TestBase
{
    #region Endpoint Not Found (Middleware rewriter)

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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task endpoint_not_found(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/12345678", stringContent, AbortToken);
        await _VerifyEndpointNotFound(response);
    }

    private static async Task _VerifyEndpointNotFound(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.EndpointNotFound,
            ProblemDetailsConstants.Titles.EndpointNotFound,
            StatusCodes.Status404NotFound,
            ProblemDetailsConstants.Details.EndpointNotFound("/12345678")
        );

        jsonElement.EnumerateObject().Should().HaveCount(9);
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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_bad_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/mvc/malformed-syntax", AbortToken);
        await _VerifyMalformedSyntax(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_api_bad_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/minimal/malformed-syntax", AbortToken);
        await _VerifyMalformedSyntax(response);
    }

    private static async Task _VerifyMalformedSyntax(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.BadRequest,
            ProblemDetailsConstants.Titles.BadRequest,
            StatusCodes.Status400BadRequest,
            ProblemDetailsConstants.Details.BadRequest
        );

        jsonElement.EnumerateObject().Should().HaveCount(9);
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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_api_entity_not_found(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/entity-not-found", stringContent, AbortToken);
        await _VerifyEntityNotFound(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_entity_not_found(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/entity-not-found", stringContent, AbortToken);
        await _VerifyEntityNotFound(response);
    }

    private static async Task _VerifyEntityNotFound(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.EntityNotFound,
            ProblemDetailsConstants.Titles.EntityNotFound,
            StatusCodes.Status404NotFound,
            ProblemDetailsConstants.Details.EntityNotFound("Entity", "Key")
        );

        jsonElement.GetProperty("params").GetProperty("entity").GetString().Should().Be("Entity");
        jsonElement.GetProperty("params").GetProperty("key").GetString().Should().Be("Key");

        jsonElement.EnumerateObject().Should().HaveCount(10);
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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_api_conflict(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/conflict", stringContent, AbortToken);
        await _VerifyConflict(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_conflict(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/conflict", stringContent, AbortToken);
        await _VerifyConflict(response);
    }

    private static async Task _VerifyConflict(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.Conflict,
            ProblemDetailsConstants.Titles.Conflict,
            StatusCodes.Status409Conflict,
            ProblemDetailsConstants.Details.Conflict
        );

        var errors = jsonElement.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().ContainSingle();
        _ValidateErrorDescriptor(errors[0]);
        jsonElement.EnumerateObject().Should().HaveCount(10);
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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_api_unprocessable(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/minimal/unprocessable", stringContent, AbortToken);
        await _VerifyUnprocessable(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_unprocessable(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var stringContent = new StringContent(string.Empty);
        using var response = await client.PostAsync("/mvc/unprocessable-entity", stringContent, AbortToken);
        await _VerifyUnprocessable(response);
    }

    private static async Task _VerifyUnprocessable(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.UnprocessableEntity,
            ProblemDetailsConstants.Titles.UnprocessableEntity,
            StatusCodes.Status422UnprocessableEntity,
            ProblemDetailsConstants.Details.UnprocessableEntity
        );

        var errorsObject = jsonElement.GetProperty("errors").EnumerateObject().ToList();
        errorsObject.Should().ContainSingle();
        errorsObject[0].Name.Should().Be("property");
        var propertyErrorsArray = errorsObject[0].Value.EnumerateArray().ToList();
        propertyErrorsArray.Should().ContainSingle();
        var error = propertyErrorsArray[0];
        _ValidateErrorDescriptor(error);
        jsonElement.EnumerateObject().Should().HaveCount(10);
    }

    #endregion

    #region Internal Error (UseExceptionHandler)

    // UseExceptionHandler (Production)

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

    // UseDeveloperExceptionHandler (Development)

    /*
     * {
     *   "type" : "https://tools.ietf.org/html/rfc9110#section-15.6.1",
     *   "title" : "unhandled-exception",
     *   "status" : 500,
     *   "detail" : "This is a test exception.",
     *   "instance" : "/minimal/internal-error",
     *   "exception" : {
     *     "details" : "System.InvalidOperationException: This is a test exception.\r\n   at Framework.Api.Demo.Endpoints.ProblemsEndpoints.<>c.<MapProblemsEndpoints>b__0_2() in D:\\Dev\\headless-framework\\demo\\Framework.Api.Demo\\Endpoints\\ProblemsEndpoints.cs:line 32\r\n   at lambda_method48(Closure, EndpointFilterInvocationContext)\r\n   at Microsoft.AspNetCore.Builder.MinimalApiExceptionFilter.InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) in D:\\Dev\\headless-framework\\src\\Framework.Api.MinimalApi\\Filters\\MinimalApiExceptionFilter.cs:line 43\r\n   at Microsoft.AspNetCore.Http.RequestDelegateFactory.<ExecuteValueTaskOfObject>g__ExecuteAwaited|128_0(ValueTask`1 valueTask, HttpContext httpContext, JsonTypeInfo`1 jsonTypeInfo)\r\n   at NSwag.AspNetCore.Middlewares.SwaggerUiIndexMiddleware.Invoke(HttpContext context)\r\n   at NSwag.AspNetCore.Middlewares.RedirectToIndexMiddleware.Invoke(HttpContext context)\r\n   at NSwag.AspNetCore.Middlewares.OpenApiDocumentMiddleware.Invoke(HttpContext context)\r\n   at Framework.Api.Middlewares.StatusCodesRewriterMiddleware.InvokeAsync(HttpContext context, RequestDelegate next) in D:\\Dev\\headless-framework\\src\\Framework.Api\\Middlewares\\StatusCodesRewriterMiddleware.cs:line 13\r\n   at Microsoft.AspNetCore.Builder.UseMiddlewareExtensions.InterfaceMiddlewareBinder.<>c__DisplayClass2_0.<<CreateMiddleware>b__0>d.MoveNext()\r\n--- End of stack trace from previous location ---\r\n   at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.Invoke(HttpContext context)",
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

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_api_internal_error(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/minimal/internal-error");
        request.Headers.Add("Accept", ContentTypes.Applications.Json);
        request.Content = new StringContent(string.Empty);
        using var response = await client.SendAsync(request, AbortToken);
        await _VerifyInternalServerError(response, environment);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_internal_error(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mvc/internal-error");
        request.Headers.Add("Accept", ContentTypes.Applications.Json);
        request.Content = new StringContent(string.Empty);
        using var response = await client.SendAsync(request, AbortToken);
        await _VerifyInternalServerError(response, environment);
    }

    private async Task _VerifyInternalServerError(HttpResponseMessage response, string environment)
    {
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        var details =
            environment is EnvironmentNames.Development
                ? "This is a test exception."
                : ProblemDetailsConstants.Details.InternalError;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.InternalError,
            ProblemDetailsConstants.Titles.InternalError,
            StatusCodes.Status500InternalServerError,
            details
        );

        if (environment is EnvironmentNames.Development)
        {
            jsonElement.GetProperty("exception").EnumerateObject().Should().HaveCountGreaterThan(0);
        }
        jsonElement.EnumerateObject().Should().HaveCount(environment is EnvironmentNames.Development ? 10 : 9); // dev has "exception" property
    }

    #endregion

    #region Unauthorized

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_unauthorized_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/minimal/authorized");
        request.Headers.Add("Accept", ContentTypes.Applications.Json);
        using var response = await client.SendAsync(request, AbortToken);
        await _ValidateUnauthorized(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_unauthorized_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/minimal/authorized", AbortToken);
        await _ValidateUnauthorized(response);
    }

    private static async Task _ValidateUnauthorized(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.Unauthorized,
            ProblemDetailsConstants.Titles.Unauthorized,
            StatusCodes.Status401Unauthorized,
            ProblemDetailsConstants.Details.Unauthorized
        );

        jsonElement.EnumerateObject().Should().HaveCount(9);
    }

    #endregion

    #region Forbidden

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_forbidden_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mvc/policy-authorized");
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic("test", "p@ssw0rd");
        request.Headers.Add("Accept", ContentTypes.Applications.Json);
        using var response = await client.SendAsync(request, AbortToken);
        await _ValidateForbidden(response);
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_forbidden_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/minimal/policy-authorized");
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic("test", "p@ssw0rd");
        request.Headers.Add("Accept", ContentTypes.Applications.Json);
        using var response = await client.SendAsync(request, AbortToken);
        await _ValidateForbidden(response);
    }

    private static async Task _ValidateForbidden(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        _ValidateCoreProblemDetails(
            jsonElement,
            response,
            ProblemDetailsConstants.Types.Forbidden,
            ProblemDetailsConstants.Titles.Forbidden,
            StatusCodes.Status403Forbidden,
            ProblemDetailsConstants.Details.Forbidden
        );

        jsonElement.EnumerateObject().Should().HaveCount(9);
    }

    #endregion

    #region Method Not Allowed

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task mvc_method_not_allowed_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/mvc/malformed-syntax", "{}", AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var content = await response.Content.ReadAsStringAsync(AbortToken);
        content.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EnvironmentNames.Development)]
    [InlineData(EnvironmentNames.Staging)]
    [InlineData(EnvironmentNames.Test)]
    [InlineData(EnvironmentNames.Production)]
    public async Task minimal_method_not_allowed_request(string environment)
    {
        await using var factory = _CreateDefaultFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/minimal/malformed-syntax", "{}", AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var content = await response.Content.ReadAsStringAsync(AbortToken);
        content.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static void _ValidateErrorDescriptor(JsonElement error)
    {
        error.GetProperty("code").GetString().Should().Be("error-code");
        error.GetProperty("description").GetString().Should().Be("Error message");
        error.GetProperty("severity").GetString().Should().Be("information");
        error.GetProperty("params").ValueKind.Should().Be(JsonValueKind.Null);
        error.EnumerateObject().Should().HaveCount(4);
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

    private WebApplicationFactory<Program> _CreateDefaultFactory(string environment)
    {
        return new CustomWebApplicationFactory(LoggerProvider, environment: environment);
    }

    private sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly ILoggerProvider? _loggerProvider;
        private readonly Action<WebHostBuilderContext, IServiceCollection>? _configureServices;
        private readonly Action<IWebHostBuilder>? _configureHost;
        private readonly string _environment;

        public CustomWebApplicationFactory(
            ILoggerProvider? loggerProvider = null,
            Action<WebHostBuilderContext, IServiceCollection>? configureServices = null,
            Action<IWebHostBuilder>? configureHost = null,
            string environment = EnvironmentNames.Production
        )
        {
            _loggerProvider = loggerProvider;
            _configureServices = configureServices;
            _configureHost = configureHost;
            _environment = environment;
            ClientOptions.AllowAutoRedirect = false;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment(_environment);

            builder.ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();

                if (_loggerProvider is not null)
                {
                    loggingBuilder.AddProvider(_loggerProvider);
                }
            });

            builder.ConfigureServices((ctx, services) => _configureServices?.Invoke(ctx, services));

            _configureHost?.Invoke(builder);
        }
    }

    #endregion
}
