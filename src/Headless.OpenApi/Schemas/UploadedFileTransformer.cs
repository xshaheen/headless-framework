// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Describes an <see cref="IUploadedFile"/> as a binary file, the way the native generator describes an
/// <c>IFormFile</c>, and publishes a form body that carries one as <c>multipart/form-data</c>.
/// </summary>
/// <remarks>
/// A command that must not depend on ASP.NET Core takes an <see cref="IUploadedFile"/>, and Headless.Api.Mvc binds it
/// from the form. The generator knows neither: it would describe the interface's members as a JSON object, and it picks
/// multipart only for an <c>IFormFile</c>, so the body would read as url-encoded and a generated client could not send
/// the file.
/// </remarks>
internal sealed class UploadedFileTransformer : IOpenApiSchemaTransformer, IOpenApiOperationTransformer
{
    private const string _MultipartFormData = "multipart/form-data";
    private const string _FormUrlEncoded = "application/x-www-form-urlencoded";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.JsonTypeInfo.Type == typeof(IUploadedFile))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "binary";
            schema.Properties = null;
            schema.Required = null;
            // The interface's own documentation describes a .NET contract, not what a client sends.
            schema.Description = null;
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            operation.RequestBody?.Content is { } content
            && !content.ContainsKey(_MultipartFormData)
            && context.Description.ParameterDescriptions.Any(parameter => parameter.Type == typeof(IUploadedFile))
            && content.Remove(_FormUrlEncoded, out var form)
        )
        {
            content[_MultipartFormData] = form;
        }

        return Task.CompletedTask;
    }
}
