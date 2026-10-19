// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;

namespace Headless.OpenApi;

/// <summary>
/// Gives each MVC action's API description the metadata of the endpoint that serves it, so the transformers see what
/// the request pipeline sees.
/// </summary>
/// <remarks>
/// MVC's API Explorer describes action descriptors, whose metadata holds only the action's and controller's
/// attributes. A convention on <c>MapControllers()</c>, such as <c>WithMetadata(...)</c>,
/// <c>RequireAuthorization(...)</c>, or <c>WithIdempotency(...)</c>, runs when the endpoint is built and adds to the
/// endpoint alone. This finds the route endpoint that carries the description's action descriptor and keeps its metadata
/// in <see cref="ApiDescription.Properties"/>, which <see cref="ApiDescriptionEndpointMetadata.GetEndpointMetadata"/>
/// reads. A Minimal API description already copies its endpoint's metadata, so it is left alone.
/// </remarks>
internal sealed class EndpointMetadataDescriptionProvider(EndpointDataSource endpoints) : IApiDescriptionProvider
{
    // After the MVC and Minimal API providers (-1000 and -1100), which fill the results in OnProvidersExecuting.
    public int Order => 0;

    public void OnProvidersExecuting(ApiDescriptionProviderContext context) { }

    public void OnProvidersExecuted(ApiDescriptionProviderContext context)
    {
        Dictionary<ActionDescriptor, EndpointMetadataCollection>? metadataByAction = null;

        foreach (var description in context.Results)
        {
            metadataByAction ??= _MetadataByAction();

            if (metadataByAction.TryGetValue(description.ActionDescriptor, out var metadata))
            {
                description.Properties[ApiDescriptionEndpointMetadata.PropertyKey] = metadata;
            }
        }
    }

    private Dictionary<ActionDescriptor, EndpointMetadataCollection> _MetadataByAction()
    {
        var metadataByAction = new Dictionary<ActionDescriptor, EndpointMetadataCollection>(
            ReferenceEqualityComparer.Instance
        );

        foreach (var endpoint in endpoints.Endpoints)
        {
            // An action reached by several conventional routes keeps its first endpoint; API Explorer describes only
            // attribute-routed actions, which have one endpoint each.
            if (endpoint.Metadata.GetMetadata<ActionDescriptor>() is { } action)
            {
                metadataByAction.TryAdd(action, endpoint.Metadata);
            }
        }

        return metadataByAction;
    }
}
