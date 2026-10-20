// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Pins the 4XX and 5XX range responses that type every status an operation does not list.</summary>
public sealed class ProblemResponseRangeDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync(Action<HeadlessOpenApiOptions>? configure = null) =>
        OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();

                if (configure is not null)
                {
                    builder.Services.ConfigureHeadlessOpenApi(configure);
                }
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

    [Fact]
    public async Task should_document_both_ranges_as_headless_problems_on_every_operation()
    {
        var document = await _GetDocumentAsync();

        var responses = _Operations(document).Select(operation => operation["responses"]!).ToList();

        responses.Should().NotBeEmpty();
        responses
            .Should()
            .AllSatisfy(response =>
            {
                _ProblemSchemaRef(response["4XX"]!).Should().Be("#/components/schemas/HeadlessProblemDetails");
                _ProblemSchemaRef(response["5XX"]!).Should().Be("#/components/schemas/HeadlessProblemDetails");
            });
    }

    [Fact]
    public async Task should_keep_the_ranges_when_the_standard_problems_are_off()
    {
        // given the 404/409/422 baseline turned off for the whole API
        var document = await _GetDocumentAsync(options => options.StandardProblemResponses = false);

        // then the ranges still cover every operation, because every error the framework writes is a problem body
        _Operations(document)
            .Should()
            .AllSatisfy(operation =>
            {
                operation["responses"]!.AsObject().Select(x => x.Key).Should().Contain(["4XX", "5XX"]);
            });
    }

    [Fact]
    public async Task should_stay_a_valid_openapi_document()
    {
        var document = await _GetDocumentAsync();

        document.ShouldBeValidOpenApi("a document with range responses");
    }

    private static IEnumerable<JsonNode> _Operations(JsonNode document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject()).Select(operation => operation.Value!);

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}
