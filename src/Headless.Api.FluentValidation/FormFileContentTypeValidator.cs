// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.Api;
using Microsoft.AspNetCore.Http;

namespace Headless.FluentValidation;

/// <summary>
/// Limits an uploaded file's declared content type to a list, compared case-insensitively. A <see langword="null"/>
/// file passes. A typed validator rather than a predicate, so API documentation can read the list through
/// <see cref="IFormFileContentTypeValidator"/>.
/// </summary>
/// <remarks>A failure adds the <c>{ContentTypes}</c> message argument: the accepted types, quoted and comma-separated.</remarks>
internal sealed class FormFileContentTypeValidator<T, TFormFile>(IReadOnlyList<string> contentTypes)
    : PropertyValidator<T, TFormFile?>,
        IFormFileContentTypeValidator
    where TFormFile : IFormFile
{
    public override string Name => "FormFileContentTypeValidator";

    // Copied so a caller that later changes its list cannot change what the rule accepts or documents.
    public IReadOnlyList<string> ContentTypes { get; } = [.. contentTypes];

    public override bool IsValid(ValidationContext<T> context, TFormFile? value)
    {
        if (
            value is null
            || ContentTypes.Any(contentType =>
                value.ContentType.Equals(contentType, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        context.MessageFormatter.AppendArgument("ContentTypes", string.Join(", ", ContentTypes.Select(c => $"'{c}'")));

        return false;
    }
}
