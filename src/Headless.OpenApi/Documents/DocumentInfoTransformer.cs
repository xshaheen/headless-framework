// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Names the document after the entry assembly's product (<c>My.Product</c> becomes <c>My Product API</c>) and
/// describes the framework's response conventions.
/// </summary>
/// <remarks>
/// The native default title is <c>{ApplicationName} | {documentName}</c>. Surface documents and transformers added
/// after the Headless ones override the title.
/// </remarks>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        document.Info ??= new OpenApiInfo();

        if (AssemblyInformation.Entry?.Product?.Replace('.', ' ') is { Length: > 0 } productName)
        {
            document.Info.Title = productName.EnsureEndsWith(" API");
        }

        document.Info.Description ??= SwaggerInformation.ResponsesDescription;

        return Task.CompletedTask;
    }
}
