// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using Headless.FluentValidation.Resources;
using Microsoft.AspNetCore.Http;

#pragma warning disable IDE0130 // FluentValidation integrations intentionally live in the integrated namespace.

namespace FluentValidation;

/// <summary>
/// FluentValidation extension methods for validating <see cref="IFormFile"/> properties on ASP.NET Core request models.
/// </summary>
/// <remarks>
/// <para>
/// All validators treat a <see langword="null"/> file as passing (the rule is skipped). Combine with
/// FluentValidation's built-in <c>.NotNull()</c> or <c>.NotEmpty()</c> if the file is required.
/// </para>
/// <para>
/// <b>Security note:</b> these validators check size, declared content type, and binary file signatures,
/// but they do not scan for malware. A virus/malware scanner API <b>MUST</b> be invoked before the file
/// is made available to users or other systems.
/// </para>
/// </remarks>
[PublicAPI]
public static class FileValidators
{
    /*
     * A virus/malware scanner API MUST be used on the file before making the file available to users or other systems.
     */

    extension<T>(IRuleBuilder<T, IFormFile?> builder)
    {
        /// <summary>Validates that the uploaded file is not empty (has a length greater than zero).</summary>
        /// <returns>Rule builder options for further chaining.</returns>
        public IRuleBuilderOptions<T, IFormFile?> FileNotEmpty()
        {
            return builder
                .Must((_, file) => file is null || file.Length > 0)
                .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileNotEmpty());
        }

        /// <summary>Validates that the uploaded file size is at least <paramref name="minBytes"/> bytes.</summary>
        /// <param name="minBytes">The minimum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MinSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
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
                                (minBytes / 1048576d).ToString("N1", CultureInfo.CurrentCulture)
                            )
                            .AppendArgument(
                                "TotalLength",
                                (file.Length / 1048576d).ToString("N1", CultureInfo.CurrentCulture)
                            );

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileGreaterThanOrEqualToValidator());
        }

        /// <summary>Validates that the uploaded file size does not exceed <paramref name="maxBytes"/> bytes.</summary>
        /// <param name="maxBytes">The maximum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MaxSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
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
                                (maxBytes / 1048576d).ToString("N1", CultureInfo.CurrentCulture)
                            )
                            .AppendArgument(
                                "TotalLength",
                                (file.Length / 1048576d).ToString("N1", CultureInfo.CurrentCulture)
                            );

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileLessThanOrEqualToValidator());
        }
    }

    extension<T, TIFormFile>(IRuleBuilder<T, TIFormFile?> builder)
        where TIFormFile : IFormFile
    {
        /// <summary>
        /// Validates that the uploaded file's declared <c>Content-Type</c> is one of the allowed
        /// <paramref name="contentTypes"/>.
        /// </summary>
        /// <param name="contentTypes">
        /// The list of permitted MIME type strings (e.g. <c>"image/png"</c>, <c>"application/pdf"</c>).
        /// Comparison is case-insensitive.
        /// </param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// This rule validates only the <em>declared</em> content type sent by the client and can be
        /// spoofed. Use <c>HaveSignatures</c> in addition to verify the actual binary signature.
        /// The validation error message includes a <c>{ContentTypes}</c> placeholder listing the allowed types.
        /// </remarks>
        public IRuleBuilderOptions<T, TIFormFile?> ContentTypes(IReadOnlyList<string> contentTypes)
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
                            string.Join(", ", contentTypes.Select(c => $"'{c}'"))
                        );

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorFormFileErrorDescriber.FileContentTypeValidator());
        }

        /// <summary>
        /// Validates the uploaded file's binary signature (magic bytes) using the provided
        /// <paramref name="inspector"/> and <paramref name="predicate"/>.
        /// </summary>
        /// <param name="inspector">
        /// The <see cref="IFileFormatInspector"/> used to determine the actual file format by
        /// inspecting the file's binary content.
        /// </param>
        /// <param name="predicate">
        /// A delegate that receives the detected <see cref="FileFormat"/> (or <see langword="null"/>
        /// if the format could not be identified) and returns <see langword="true"/> when the format is acceptable.
        /// </param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The file stream is opened and read asynchronously; the stream is disposed after inspection.
        /// Because the validation reads from the file stream, it may not be rewindable after this rule
        /// runs — place this rule last if other rules also consume the stream.
        /// </remarks>
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
