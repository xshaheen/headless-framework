// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds a server per tenant host template when tenant catalog resolution reads the tenant from the request host, so a
/// client can target one tenant's host by filling in the <c>tenant</c> variable.
/// </summary>
/// <remarks>
/// A template becomes <c>https://</c> plus the template, with <c>{tenant}</c> as the variable. A template with a
/// <c>?</c> or <c>*</c> wildcard label matches hosts no single URL describes, so it adds no server. The servers follow
/// any the document already lists, which keep their order.
/// </remarks>
internal sealed class TenantServersTransformer : IOpenApiDocumentTransformer
{
    private const string _TenantVariable = "tenant";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (TenantCatalogResolution.Get(context.ApplicationServices) is not { HostTemplates.Count: > 0 } resolution)
        {
            return Task.CompletedTask;
        }

        foreach (var template in resolution.HostTemplates)
        {
            if (template.Split('.').Any(label => label is "?" or "*"))
            {
                continue;
            }

            var url = "https://" + template;
            var servers = document.Servers ??= [];

            if (servers.Any(server => string.Equals(server.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            servers.Add(
                new OpenApiServer
                {
                    Url = url,
                    Description = "A tenant's host. The tenant catalog resolves the tenant from the host name.",
                    Variables = new Dictionary<string, OpenApiServerVariable>(StringComparer.Ordinal)
                    {
                        [_TenantVariable] = new()
                        {
                            Default = _TenantVariable,
                            Description = "The identifier of the tenant.",
                        },
                    },
                }
            );
        }

        return Task.CompletedTask;
    }
}
