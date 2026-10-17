// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.OpenApi;

namespace Tests;

/// <summary>The document transformers driven directly, for documents the native generator does not produce.</summary>
public sealed class DocumentTransformerTests : TestBase
{
    [Fact]
    public async Task should_keep_a_description_an_earlier_transformer_wrote()
    {
        var document = new OpenApiDocument { Info = new OpenApiInfo { Description = "Mine" } };

        await new DocumentInfoTransformer().TransformAsync(document, TransformerContexts.Document(), AbortToken);

        document.Info.Description.Should().Be("Mine");
    }

    [Fact]
    public async Task should_describe_the_response_conventions_when_no_description_is_set()
    {
        var document = new OpenApiDocument { Info = null! };

        await new DocumentInfoTransformer().TransformAsync(document, TransformerContexts.Document(), AbortToken);

        document.Info.Description.Should().Contain("## Success Response");
    }

    [Fact]
    public async Task should_number_every_repeat_of_an_operation_id_uniquely()
    {
        var operations = new[] { "Find", "Find", "Find", "Other" }
            .Select(id => new OpenApiOperation { OperationId = id })
            .ToArray();
        var document = new OpenApiDocument { Paths = [] };

        for (var i = 0; i < operations.Length; i++)
        {
            document.Paths[$"/path{i}"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operations[i] },
            };
        }

        await new OperationIdDeduplicationTransformer().TransformAsync(
            document,
            TransformerContexts.Document(),
            AbortToken
        );

        operations.Select(operation => operation.OperationId).Should().Equal("Find", "Find2", "Find3", "Other");
    }

    [Fact]
    public async Task should_accept_a_document_without_paths()
    {
        var document = new OpenApiDocument { Paths = null! };

        var act = () =>
            new OperationIdDeduplicationTransformer().TransformAsync(
                document,
                TransformerContexts.Document(),
                AbortToken
            );

        await act.Should().NotThrowAsync();
    }
}
