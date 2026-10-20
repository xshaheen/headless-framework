// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;

namespace Headless.FluentValidation;

/// <summary>
/// Limits a file's declared content type to a list, compared case-insensitively. A <see langword="null"/> file passes.
/// A typed validator rather than a predicate, so API documentation can read the list through
/// <see cref="IFileContentTypeValidator"/>.
/// </summary>
/// <remarks>
/// <paramref name="getContentType"/> reads the declared type, so one implementation serves every file type a rule
/// accepts. A failure adds the <c>{ContentTypes}</c> message argument: the accepted types, quoted and comma-separated.
/// </remarks>
internal sealed class FileContentTypeValidator<T, TFile>(
    Func<TFile, string> getContentType,
    IReadOnlyList<string> contentTypes
) : PropertyValidator<T, TFile?>, IFileContentTypeValidator
    where TFile : class
{
    public override string Name => "FileContentTypeValidator";

    // Copied so a caller that later changes its list cannot change what the rule accepts or documents.
    public IReadOnlyList<string> ContentTypes { get; } = [.. contentTypes];

    public override bool IsValid(ValidationContext<T> context, TFile? value)
    {
        if (value is null)
        {
            return true;
        }

        var declared = getContentType(value);

        if (ContentTypes.Any(contentType => declared.Equals(contentType, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        context.MessageFormatter.AppendArgument("ContentTypes", string.Join(", ", ContentTypes.Select(c => $"'{c}'")));

        return false;
    }
}
