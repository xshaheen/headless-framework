// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation;
using Framework.FluentValidation.Resources;
using Microsoft.AspNetCore.Http;

namespace Framework.FluentValidation;

public static class FileValidators
{
    public static IRuleBuilderOptions<T, IFormFile?> FileNotEmpty<T>(this IRuleBuilder<T, IFormFile?> builder)
    {
        return builder
            .Must((_, file) => file is null || file.Length > 0)
            .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileNotEmpty());
    }

    public static IRuleBuilderOptions<T, IFormFile?> GreaterThanOrEqualTo<T>(
        this IRuleBuilder<T, IFormFile?> builder,
        int minBytes
    )
    {
        return builder
            .Must(
                (_, file, context) =>
                {
                    if (file is null || file.Length >= minBytes)
                    {
                        return true;
                    }

                    context
                        .MessageFormatter.AppendArgument(
                            "MinSize",
                            (minBytes / 1048576).ToString("N1", CultureInfo.CurrentCulture)
                        )
                        .AppendArgument(
                            "TotalLength",
                            (file.Length / 1048576).ToString("N1", CultureInfo.CurrentCulture)
                        );

                    return false;
                }
            )
            .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileGreaterThanOrEqualToValidator());
    }

    public static IRuleBuilderOptions<T, IFormFile?> LessThanOrEqualTo<T>(
        this IRuleBuilder<T, IFormFile?> builder,
        int maxBytes
    )
    {
        return builder
            .Must(
                (_, file, context) =>
                {
                    if (file is null || file.Length <= maxBytes)
                    {
                        return true;
                    }

                    context
                        .MessageFormatter.AppendArgument(
                            "MaxSize",
                            (maxBytes / 1048576).ToString("N1", CultureInfo.CurrentCulture)
                        )
                        .AppendArgument(
                            "TotalLength",
                            (file.Length / 1048576).ToString("N1", CultureInfo.CurrentCulture)
                        );

                    return false;
                }
            )
            .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileLessThanOrEqualToValidator());
    }

    public static IRuleBuilderOptions<T, TIFormFile?> ContentTypes<T, TIFormFile>(
        this IRuleBuilder<T, TIFormFile?> builder,
        IReadOnlyList<string> contentTypes
    )
        where TIFormFile : IFormFile
    {
        return builder
            .Must(
                (_, file, context) =>
                {
                    if (
                        file is null
                        || contentTypes.Any(contentType =>
                            file.ContentType.Equals(contentType, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        return true;
                    }

                    context.MessageFormatter.AppendArgument(
                        "ContentTypes",
                        contentTypes.Aggregate((p, c) => $"'{p}', '{c}'")
                    );

                    return false;
                }
            )
            .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileContentTypeValidator());
    }

    public static IRuleBuilderOptions<T, TIFormFile?> HaveSignatures<T, TIFormFile>(
        this IRuleBuilder<T, TIFormFile?> builder,
        IFileFormatInspector inspector,
        Func<FileFormat?, bool> predicate
    )
        where TIFormFile : IFormFile
    {
        return builder
            .MustAsync(
                async (file, _) =>
                {
                    if (file is null)
                    {
                        return true;
                    }

                    await using var stream = file.OpenReadStream();
                    var format = inspector.DetermineFileFormat(stream);

                    return predicate(format);
                }
            )
            .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileSignatureValidator());
    }

    /*
     * A virus/malware scanner API MUST be used on the file before making the file available to users or other systems.
     */
}
