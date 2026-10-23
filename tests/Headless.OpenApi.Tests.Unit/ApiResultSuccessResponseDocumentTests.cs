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

/// <summary>
/// Pins the success response the 201 and 202 <see cref="ApiResult"/> conversions publish: the declared status replaces
/// the default 200, a valued result documents its schema, and a unit result documents no body.
/// </summary>
public sealed class ApiResultSuccessResponseDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync()
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(JobsController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapControllers();

                var jobs = app.MapGroup("/minimal/jobs").AllowAnonymous();
                jobs.MapPost("/created", () => ApiResult<JobView>.Ok(new JobView("j1")).ToCreatedHttpResult(j => j.Id));
                jobs.MapPost("/valued", () => ApiResult<JobView>.Ok(new JobView("j1")).ToAcceptedHttpResult());
                jobs.MapPost(
                    "/valued-location",
                    () => ApiResult<JobView>.Ok(new JobView("j1")).ToAcceptedHttpResult(j => $"/jobs/{j.Id}")
                );
                jobs.MapPost("/unit", () => ApiResult.Ok().ToAcceptedHttpResult());
                jobs.MapPost(
                    "/dispatched",
                    (StartJob request, [FromServices] Mediator.ISender sender, CancellationToken cancellationToken) =>
                        sender.DispatchAcceptedAsync(request, cancellationToken)
                );
                jobs.MapPost(
                    "/dispatched-unit",
                    (StopJob request, [FromServices] Mediator.ISender sender, CancellationToken cancellationToken) =>
                        sender.DispatchAcceptedAsync(request, "/jobs", cancellationToken)
                );
            },
            "v1",
            AbortToken
        );
    }

    [Theory]
    [InlineData("/minimal/jobs/created", "201")]
    [InlineData("/minimal/jobs/valued", "202")]
    [InlineData("/minimal/jobs/valued-location", "202")]
    [InlineData("/minimal/jobs/dispatched", "202")]
    [InlineData("/mvc/jobs", "201")]
    [InlineData("/mvc/jobs/start", "202")]
    [InlineData("/mvc/jobs/start-tracked", "202")]
    public async Task should_document_the_valued_success_status_with_its_schema(string path, string status)
    {
        var document = await _GetDocumentAsync();

        var responses = document.Operation(path, "post")["responses"]!;

        responses.ExactStatusCodes().Should().Contain(status).And.NotContain(["200", "204"]);
        ((string?)responses[status]!["content"]!["application/json"]!["schema"]!["$ref"])
            .Should()
            .Be("#/components/schemas/JobView");
        _ProblemSchemaRef(responses["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
        document.ShouldBeValidOpenApi(path);
    }

    [Theory]
    [InlineData("/minimal/jobs/unit")]
    [InlineData("/minimal/jobs/dispatched-unit")]
    [InlineData("/mvc/jobs/stop")]
    [InlineData("/mvc/jobs/stop-tracked")]
    public async Task should_document_the_unit_accepted_status_without_a_body(string path)
    {
        var document = await _GetDocumentAsync();

        var responses = document.Operation(path, "post")["responses"]!;

        responses.ExactStatusCodes().Should().Contain("202").And.NotContain(["200", "204"]);
        responses["202"]!["content"].Should().BeNull();
        _ProblemSchemaRef(responses["404"]!).Should().Be("#/components/schemas/EntityNotFoundProblemDetails");
    }

    [Fact]
    public async Task should_not_leak_the_result_wrappers_as_schemas()
    {
        var document = await _GetDocumentAsync();

        document["components"]!["schemas"]!
            .AsObject()
            .Select(x => x.Key)
            .Should()
            .NotContain(key => key.StartsWith("ApiResult", StringComparison.Ordinal))
            .And.NotContain(key => key.StartsWith("ApiCreated", StringComparison.Ordinal))
            .And.NotContain(key => key.StartsWith("ApiAccepted", StringComparison.Ordinal));
    }

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}

public sealed record JobView(string Id);

public sealed record StartJob(string Name) : Mediator.IRequest<ApiResult<JobView>>;

public sealed record StopJob(string Id) : Mediator.IRequest<ApiResult>;

[NonController]
[ApiController]
[AllowAnonymous]
[Route("mvc/jobs")]
public sealed class JobsController(IProblemDetailsCreator problems) : ControllerBase
{
    [HttpPost]
    public ApiCreatedActionResult<JobView> Create() =>
        ApiResult<JobView>.Ok(new JobView("j1")).ToCreatedActionResult(this, problems, j => $"/mvc/jobs/{j.Id}");

    [HttpPost("start")]
    public ApiAcceptedActionResult<JobView> Start() =>
        ApiResult<JobView>.Ok(new JobView("j1")).ToAcceptedActionResult(this, problems);

    [HttpPost("start-tracked")]
    public Task<ApiAcceptedActionResult<JobView>> StartTrackedAsync() =>
        Task.FromResult(
            ApiResult<JobView>.Ok(new JobView("j1")).ToAcceptedActionResult(this, problems, j => $"/mvc/jobs/{j.Id}")
        );

    [HttpPost("stop")]
    public ApiAcceptedActionResult Stop() => ApiResult.Ok().ToAcceptedActionResult(this, problems);

    [HttpPost("stop-tracked")]
    public ApiAcceptedActionResult StopTracked() => ApiResult.Ok().ToAcceptedActionResult(this, problems, "/mvc/jobs");
}
