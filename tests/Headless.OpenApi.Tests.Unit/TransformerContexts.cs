// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Builds transformer contexts by hand, so a test can drive one transformer through a branch a generated document
/// never reaches.
/// </summary>
internal static class TransformerContexts
{
    // Only the options infrastructure every host registers, so transformers read default options.
    private static readonly IServiceProvider _EmptyServices = new ServiceCollection()
        .AddOptions()
        .BuildServiceProvider();

    public static ApiDescription Description(string? httpMethod, string? relativePath, params object[] metadata)
    {
        return new ApiDescription
        {
            HttpMethod = httpMethod,
            RelativePath = relativePath,
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [.. metadata] },
        };
    }

    public static OpenApiOperationTransformerContext Operation(ApiDescription description)
    {
        return new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = description,
            ApplicationServices = _EmptyServices,
        };
    }

    public static OpenApiDocumentTransformerContext Document()
    {
        return new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = _EmptyServices,
        };
    }

    public static OpenApiSchemaTransformerContext Schema(JsonTypeInfo typeInfo, JsonPropertyInfo? property = null)
    {
        return new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = typeInfo,
            JsonPropertyInfo = property,
            ParameterDescription = null,
            ApplicationServices = _EmptyServices,
        };
    }
}
