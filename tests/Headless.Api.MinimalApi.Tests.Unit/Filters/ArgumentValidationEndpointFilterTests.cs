// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Api;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Filters;

public sealed class ArgumentValidationEndpointFilterTests : TestBase
{
    [Fact]
    public async Task should_answer_422_without_running_the_handler_when_an_argument_is_invalid()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        var handlerCalls = 0;
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapPost(
                "/users",
                (ValidatorFilterTestRequest request) =>
                {
                    handlerCalls++;
                    return TypedResults.Ok(request.Name);
                }
            );
        var httpContext = MinimalApiTestHost.CreatePostContext(services, """{"name":"","email":"not-an-email"}""");

        // when
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        handlerCalls.Should().Be(0);
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        body.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_validate_an_argument_bound_with_as_parameters()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapGet("/users", ([AsParameters] ValidatorFilterTestRequest request) => TypedResults.Ok(request.Name));
        var httpContext = MinimalApiTestHost.CreateHttpContext(services);
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.QueryString = new QueryString("?name=&email=not-an-email");

        // when
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task should_validate_through_the_typed_validator_contract_and_report_its_failures()
    {
        // given: a validator that answers only the typed IValidator<T>.ValidateAsync, as a hand-written one may
        var validator = Substitute.For<IValidator<ValidatorFilterTestRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<ValidatorFilterTestRequest>>(), Arg.Any<CancellationToken>())
            .Returns(
                new FluentValidation.Results.ValidationResult([
                    new FluentValidation.Results.ValidationFailure("name", "Name is taken."),
                ])
            );
        await using var services = MinimalApiTestHost.CreateServices(collection => collection.AddSingleton(validator));
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapPost("/users", (ValidatorFilterTestRequest request) => TypedResults.Ok(request.Name));
        var httpContext = MinimalApiTestHost.CreatePostContext(
            services,
            """{"name":"Ada","email":"ada@example.com"}"""
        );

        // when
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        await validator
            .Received(1)
            .ValidateAsync(
                Arg.Is<ValidationContext<ValidatorFilterTestRequest>>(context =>
                    context.InstanceToValidate.Name == "Ada"
                ),
                Arg.Any<CancellationToken>()
            );
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        var body = await MinimalApiTestHost.ReadJsonAsync(httpContext, AbortToken);
        body.GetProperty("errors")
            .GetProperty("name")[0]
            .GetProperty("description")
            .GetString()
            .Should()
            .Be("Name is taken.");
    }

    [Fact]
    public async Task should_run_the_handler_when_every_argument_is_valid()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapPost("/users", (ValidatorFilterTestRequest request) => TypedResults.Ok(request.Name));
        var httpContext = MinimalApiTestHost.CreatePostContext(
            services,
            """{"name":"Ada","email":"ada@example.com"}"""
        );

        // when
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_skip_an_optional_argument_the_request_left_out()
    {
        // given
        await using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapPost(
                "/users",
                ([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ValidatorFilterTestRequest? request) =>
                    TypedResults.Ok(request is null)
            );
        var httpContext = MinimalApiTestHost.CreatePostContext(services, "");

        // when
        await MinimalApiTestHost.SingleEndpoint(endpoints).RequestDelegate!(httpContext);

        // then
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void should_document_422_only_on_endpoints_with_a_validated_argument()
    {
        // given
        using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        var group = endpoints.MapGroup("/api").ValidateArguments();
        group.MapPost("/users", (ValidatorFilterTestRequest request) => TypedResults.Ok(request.Name));
        group.MapGet("/ping", () => TypedResults.Ok("pong"));

        // when
        var built = endpoints.DataSources.SelectMany(source => source.Endpoints).ToList();

        // then
        _StatusCodes(built.Single(endpoint => endpoint.DisplayName!.Contains("users", StringComparison.Ordinal)))
            .Should()
            .Contain(StatusCodes.Status422UnprocessableEntity);
        _StatusCodes(built.Single(endpoint => endpoint.DisplayName!.Contains("ping", StringComparison.Ordinal)))
            .Should()
            .NotContain(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void should_not_document_422_twice_when_the_result_type_already_does()
    {
        // given
        using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapGroup("/api")
            .ValidateArguments()
            .MapPost("/users", (ValidatorFilterTestRequest _) => ApiResult.Ok().ToHttpResult());

        // when
        var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);

        // then
        _StatusCodes(endpoint).Count(status => status == StatusCodes.Status422UnprocessableEntity).Should().Be(1);
    }

    [Fact]
    public void should_document_422_for_a_per_endpoint_validate()
    {
        // given
        using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints
            .MapPost("/users", (ValidatorFilterTestRequest request) => TypedResults.Ok(request.Name))
            .Validate<ValidatorFilterTestRequest>();

        // when
        var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);

        // then
        var problem = endpoint
            .Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(response => response.StatusCode == StatusCodes.Status422UnprocessableEntity);
        problem.Type.Should().Be<ProblemDetails>();
        problem.ContentTypes.Should().Equal("application/problem+json");
    }

    [Fact]
    public void should_leave_an_endpoint_without_a_handler_method_unfiltered()
    {
        // given
        using var services = _CreateServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints.MapGroup("/api").ValidateArguments().Map("/raw", static _ => Task.CompletedTask);

        // when
        var endpoint = MinimalApiTestHost.SingleEndpoint(endpoints);

        // then
        endpoint.Metadata.GetMetadata<ValidatedEndpointMetadata>().Should().BeNull();
    }

    [Fact]
    public void should_leave_an_endpoint_unfiltered_when_the_container_cannot_answer_service_queries()
    {
        // given
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/users"),
            order: 0
        )
        {
            ApplicationServices = Substitute.For<IServiceProvider>(),
        };
        builder.Metadata.Add(((Func<ValidatorFilterTestRequest, string>)_Handler).Method);

        // when
        ArgumentValidationEndpointFilter.Apply(builder);

        // then
        builder.FilterFactories.Should().BeEmpty();
        builder.Metadata.Should().NotContain(ValidatedEndpointMetadata.Instance);
    }

    private static string _Handler(ValidatorFilterTestRequest request) => request.Name;

    [Fact]
    public void should_reject_a_null_builder()
    {
        // when
        var validate = () => ((RouteGroupBuilder)null!).ValidateArguments();
        var produces = () => ((RouteGroupBuilder)null!).ProducesStandardProblems();
        var validateOne = () => ((RouteHandlerBuilder)null!).Validate<ValidatorFilterTestRequest>();

        // then
        validate.Should().Throw<ArgumentNullException>();
        produces.Should().Throw<ArgumentNullException>();
        validateOne.Should().Throw<ArgumentNullException>();
    }

    private static List<int> _StatusCodes(Endpoint endpoint)
    {
        return [.. endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Select(response => response.StatusCode)];
    }

    private static ServiceProvider _CreateServices()
    {
        return MinimalApiTestHost.CreateServices(services =>
            services.AddScoped<IValidator<ValidatorFilterTestRequest>, ValidatorFilterTestRequestValidator>()
        );
    }
}
