// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds a security requirement per accepted scheme to every operation that requires an authenticated user.
/// </summary>
/// <remarks>
/// Each scheme is a separate requirement, so a client may satisfy any one of them. The accepted schemes are those
/// <see cref="HeadlessOpenApiOptions.SecuritySchemeSelector"/> returns, else every registered scheme. The roles named
/// by <c>[Authorize(Roles = ...)]</c> become the requirement's scopes, which OpenAPI 3.1 allows for any scheme type.
/// </remarks>
internal sealed class SecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var description = context.Description;

        if (!EndpointAuthorization.RequiresAuthentication(description))
        {
            return Task.CompletedTask;
        }

        var options = context.ApplicationServices.GetRequiredService<IOptions<HeadlessOpenApiOptions>>().Value;
        var schemes = options.GetSecuritySchemes();
        var accepted = options.SecuritySchemeSelector?.Invoke(description) ?? schemes.Keys;
        var roles = EndpointAuthorization.GetRoles(description);
        var document = context.Document;

        foreach (var name in accepted.Distinct(StringComparer.Ordinal))
        {
            if (!schemes.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"The security scheme selector returned '{name}' for '{description.RelativePath}', but no security scheme with that name is registered."
                );
            }

            // Only the reference: SecuritySchemesTransformer registers every scheme before the document is written.
            (operation.Security ??= []).Add(
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(name, document)] = [.. roles] }
            );
        }

        return Task.CompletedTask;
    }
}
