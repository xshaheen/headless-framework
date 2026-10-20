// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation;
using Headless.FluentValidation.Resources;

namespace Headless.FluentValidation;

/// <summary>
/// Builds the file rules once for every file type: each entry point (<c>IUploadedFile</c> here, ASP.NET Core
/// <c>IFormFile</c> in Headless.Api.FluentValidation) passes how to read the size, the declared content type, and the
/// content, so the checks, error descriptors, and documented bounds cannot drift between them.
/// </summary>
internal static class FileRules
{
    public static IRuleBuilderOptions<T, TFile?> NotEmpty<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, long> getLength
    )
        where TFile : class
    {
        return builder
            .SetValidator(new FileSizeValidator<T, TFile>(getLength, minBytes: 1, maxBytes: null, formatSizes: false))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.NotEmpty);
    }

    public static IRuleBuilderOptions<T, TFile?> MinSize<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, long> getLength,
        long minBytes
    )
        where TFile : class
    {
        return builder
            .SetValidator(new FileSizeValidator<T, TFile>(getLength, minBytes, maxBytes: null, formatSizes: true))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.GreaterThanOrEqualTo);
    }

    public static IRuleBuilderOptions<T, TFile?> MaxSize<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, long> getLength,
        long maxBytes
    )
        where TFile : class
    {
        return builder
            .SetValidator(new FileSizeValidator<T, TFile>(getLength, minBytes: null, maxBytes, formatSizes: true))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.LessThanOrEqualTo);
    }

    public static IRuleBuilderOptions<T, TFile?> ContentTypes<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, string> getContentType,
        IReadOnlyList<string> contentTypes
    )
        where TFile : class
    {
        return builder
            .SetValidator(new FileContentTypeValidator<T, TFile>(getContentType, contentTypes))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.UnexpectedContentType);
    }

    public static IRuleBuilderOptions<T, TFile?> Signatures<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, Stream> openReadStream,
        IFileFormatInspector inspector,
        Func<FileFormat?, bool> predicate
    )
        where TFile : class
    {
        return builder
            .MustAsync(
                async (file, _) =>
                {
                    if (file is null)
                    {
                        return true;
                    }

                    await using var stream = openReadStream(file);
                    var format = inspector.DetermineFileFormat(stream);

                    return predicate(format);
                }
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.UnexpectedSignature);
    }
}
