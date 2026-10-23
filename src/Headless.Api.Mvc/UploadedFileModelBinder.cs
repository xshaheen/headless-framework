// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Headless.Api.Options;

/// <summary>
/// Binds an <see cref="IUploadedFile"/> property or parameter from the multipart form, the way MVC binds an
/// <see cref="IFormFile"/>, so a command that must not depend on ASP.NET Core can still bind straight from the request.
/// </summary>
/// <remarks>
/// The bound value reads through to the request's form file, so it is valid only while the request is in flight. A
/// missing or empty part stays unbound, as for <see cref="IFormFile"/>: the property keeps <see langword="null"/> and
/// the validator decides whether the file is required.
/// </remarks>
internal sealed class UploadedFileModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var request = bindingContext.HttpContext.Request;

        if (!request.HasFormContentType)
        {
            return;
        }

        var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted).ConfigureAwait(false);

        // A client names the part after the property, with or without the parameter prefix; FormFileModelBinder
        // accepts both, and so does this.
        var file =
            form.Files.GetFile(bindingContext.ModelName)
            ?? (
                string.Equals(bindingContext.ModelName, bindingContext.FieldName, StringComparison.Ordinal)
                    ? null
                    : form.Files.GetFile(bindingContext.FieldName)
            );

        if (file is null || (file.Length == 0 && string.IsNullOrEmpty(file.FileName)))
        {
            return;
        }

        bindingContext.Result = ModelBindingResult.Success(file.ToUploadedFile());
    }
}

/// <summary>Selects <see cref="UploadedFileModelBinder"/> for an <see cref="IUploadedFile"/> model.</summary>
internal sealed class UploadedFileModelBinderProvider : IModelBinderProvider
{
    private static readonly UploadedFileModelBinder _Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(IUploadedFile) ? _Binder : null;
}
