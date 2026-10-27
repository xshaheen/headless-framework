// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Publishes a form request body that carries a file as <c>multipart/form-data</c> only.
/// </summary>
/// <remarks>
/// The native generator picks multipart only for an <see cref="IFormFile"/> parameter. MVC describes each property of
/// a <c>[FromForm]</c> model as its own parameter and reports no request format, so the generator falls back to
/// <c>application/x-www-form-urlencoded</c>, and a client generated from it sends the file as a text field. A
/// url-encoded body cannot carry a file, so that media type is dropped wherever a file part is bound.
/// </remarks>
internal sealed class FormFileRequestBodyTransformer : IOpenApiOperationTransformer
{
    private const string _MultipartFormData = "multipart/form-data";
    private const string _FormUrlEncoded = "application/x-www-form-urlencoded";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            operation.RequestBody?.Content is not { } content
            || !content.TryGetValue(_FormUrlEncoded, out var form)
            || !context.Description.ParameterDescriptions.Any(_BindsFile)
        )
        {
            return Task.CompletedTask;
        }

        content.Remove(_FormUrlEncoded);
        content.TryAdd(_MultipartFormData, form);

        return Task.CompletedTask;
    }

    private static bool _BindsFile(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiParameterDescription parameter)
    {
        if (parameter.Source == BindingSource.FormFile)
        {
            return true;
        }

        // An MVC form model's file property reports the Form source; its type is what marks it as a file part.
        var type = parameter.ModelMetadata?.ModelType ?? parameter.Type;

        return type is not null
            && (type.IsAssignableTo(typeof(IFormFile)) || type.IsAssignableTo(typeof(IEnumerable<IFormFile>)));
    }
}
