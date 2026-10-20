// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using Headless.FluentValidation;
using Microsoft.AspNetCore.Http;

namespace FluentValidation;

/// <summary>
/// FluentValidation rules for an <see cref="IFormFile"/> property on an ASP.NET Core request model.
/// </summary>
/// <remarks>
/// <para>
/// These are the rules of <see cref="HeadlessFileValidators"/>, read from an <see cref="IFormFile"/>: they emit the same
/// error codes and messages and expose the same bounds to API documentation. A command or message that should not
/// depend on ASP.NET Core takes an <c>IUploadedFile</c> instead, built at the edge with
/// <c>IFormFile.ToUploadedFile()</c>, and validates it with <see cref="HeadlessFileValidators"/>.
/// </para>
/// <para>
/// Every rule treats a <see langword="null"/> file as passing (the rule is skipped). Combine with FluentValidation's
/// built-in <c>.NotNull()</c> if the file is required.
/// </para>
/// <para>
/// <b>Security note:</b> these rules check size, declared content type, and binary file signatures, but they do not
/// scan for malware. A virus/malware scanner API <b>MUST</b> be invoked before the file is made available to users or
/// other systems.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessFormFileValidators
{
    extension<T, TFormFile>(IRuleBuilder<T, TFormFile?> builder)
        where TFormFile : class, IFormFile
    {
        /// <summary>Validates that the uploaded file is not empty (has a length greater than zero).</summary>
        /// <returns>Rule builder options for further chaining.</returns>
        public IRuleBuilderOptions<T, TFormFile?> FileNotEmpty()
        {
            return FileRules.NotEmpty(builder, static file => file.Length);
        }

        /// <summary>Validates that the uploaded file size is at least <paramref name="minBytes"/> bytes.</summary>
        /// <param name="minBytes">The minimum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MinSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
        public IRuleBuilderOptions<T, TFormFile?> GreaterThanOrEqualTo(long minBytes)
        {
            return FileRules.MinSize(builder, static file => file.Length, minBytes);
        }

        /// <summary>Validates that the uploaded file size does not exceed <paramref name="maxBytes"/> bytes.</summary>
        /// <param name="maxBytes">The maximum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MaxSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
        public IRuleBuilderOptions<T, TFormFile?> LessThanOrEqualTo(long maxBytes)
        {
            return FileRules.MaxSize(builder, static file => file.Length, maxBytes);
        }

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
        /// This rule validates only the <em>declared</em> content type sent by the client and can be spoofed. Use
        /// <c>HaveSignatures</c> in addition to verify the actual binary signature. The validation error message
        /// includes a <c>{ContentTypes}</c> placeholder listing the allowed types.
        /// </remarks>
        public IRuleBuilderOptions<T, TFormFile?> ContentTypes(IReadOnlyList<string> contentTypes)
        {
            return FileRules.ContentTypes(builder, static file => file.ContentType, contentTypes);
        }

        /// <summary>
        /// Validates the uploaded file's binary signature (magic bytes) using the provided <paramref name="inspector"/>
        /// and <paramref name="predicate"/>.
        /// </summary>
        /// <param name="inspector">
        /// The <see cref="IFileFormatInspector"/> used to determine the actual file format by inspecting the file's
        /// binary content.
        /// </param>
        /// <param name="predicate">
        /// A delegate that receives the detected <see cref="FileFormat"/> (or <see langword="null"/> if the format
        /// could not be identified) and returns <see langword="true"/> when the format is acceptable.
        /// </param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The rule opens its own stream with <see cref="IFormFile.OpenReadStream"/> and disposes it after inspection,
        /// so the code that stores the file can still read the whole content.
        /// </remarks>
        public IRuleBuilderOptions<T, TFormFile?> HaveSignatures(
            IFileFormatInspector inspector,
            Func<FileFormat?, bool> predicate
        )
        {
            return FileRules.Signatures(builder, static file => file.OpenReadStream(), inspector, predicate);
        }
    }
}
