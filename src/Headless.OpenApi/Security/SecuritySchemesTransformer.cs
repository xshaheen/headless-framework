// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Publishes every security scheme <see cref="SecuritySchemeCatalog"/> finds under <c>components.securitySchemes</c>, so clients and documentation
/// UIs can offer them.
/// </summary>
/// <remarks>
/// It registers the schemes no operation references too, and it is the only place any scheme is registered:
/// <see cref="SecurityRequirementTransformer"/> emits references only. Components are keyed by name, so the shared
/// scheme instances can be added to every document.
/// </remarks>
internal sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var (name, scheme) in await SecuritySchemeCatalog.GetAsync(context.ApplicationServices))
        {
            document.AddComponent(name, scheme);
        }
    }
}
