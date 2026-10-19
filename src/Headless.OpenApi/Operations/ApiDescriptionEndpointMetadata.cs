// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace Headless.OpenApi;

/// <summary>Reads the metadata of the endpoint an API description documents.</summary>
internal static class ApiDescriptionEndpointMetadata
{
    /// <summary>The <see cref="ApiDescription.Properties"/> key of the endpoint metadata.</summary>
    public static readonly object PropertyKey = new();

    /// <summary>
    /// The metadata of the endpoint <paramref name="description"/> documents: the route endpoint's, conventions
    /// included, when <see cref="EndpointMetadataDescriptionProvider"/> found it, else the action descriptor's.
    /// </summary>
    public static IReadOnlyList<object> GetEndpointMetadata(this ApiDescription description)
    {
        if (
            description.Properties.TryGetValue(PropertyKey, out var metadata)
            && metadata is IReadOnlyList<object> endpointMetadata
        )
        {
            return endpointMetadata;
        }

        var actionMetadata = description.ActionDescriptor.EndpointMetadata;

        return actionMetadata as IReadOnlyList<object> ?? [.. actionMetadata];
    }
}
