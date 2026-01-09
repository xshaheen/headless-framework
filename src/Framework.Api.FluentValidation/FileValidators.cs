// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation.Resources;
using Microsoft.AspNetCore.Http;

namespace FluentValidation;

[PublicAPI]
public static class FileValidators
{
    /*
     * A virus/malware scanner API MUST be used on the file before making the file available to users or other systems.
     */

    extension<T>(IRuleBuilder<T, IFormFile?> builder)
    {
        public IRuleBuilderOptions<T, IFormFile?> FileNotEmpty()
        {
            return builder
                .Must((_, file) => file is null || file.Length > 0)
                .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileNotEmpty());
        }

        public IRuleBuilderOptions<T, IFormFile?> GreaterThanOrEqualTo(int minBytes)
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

        public IRuleBuilderOptions<T, IFormFile?> LessThanOrEqualTo(int maxBytes)
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
    }

    extension<T, TIFormFile>(IRuleBuilder<T, TIFormFile?> builder) where TIFormFile : IFormFile
    {
        public IRuleBuilderOptions<T, TIFormFile?> ContentTypes(
            IReadOnlyList<string> contentTypes
        )
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

        public IRuleBuilderOptions<T, TIFormFile?> HaveSignatures(
            IFileFormatInspector inspector,
            Func<FileFormat?, bool> predicate
        )
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
    }
}
