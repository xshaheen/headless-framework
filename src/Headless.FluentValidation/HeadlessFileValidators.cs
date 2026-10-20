// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.FluentValidation;
using Headless.IO;

namespace FluentValidation;

/// <summary>
/// FluentValidation rules for a file property typed as <see cref="IUploadedFile"/> or one of its implementations, so a
/// command or request handled in process validates a file without depending on ASP.NET Core.
/// </summary>
/// <remarks>
/// <para>
/// An ASP.NET Core request model whose property is an <c>IFormFile</c> uses the same rules from
/// <c>Headless.Api.FluentValidation</c>; both emit the same error codes and messages. Adapt an <c>IFormFile</c> to an
/// <see cref="IUploadedFile"/> at the edge with <c>IFormFile.ToUploadedFile()</c>. The binary signature rule,
/// <c>HaveSignatures</c>, lives in <c>Headless.FluentValidation.FileSignatures</c>, so this package carries no file-format
/// dependency.
/// </para>
/// <para>
/// Every rule treats a <see langword="null"/> file as passing (the rule is skipped). Combine with FluentValidation's
/// built-in <c>.NotNull()</c> if the file is required.
/// </para>
/// <para>
/// <b>Security note:</b> these rules check size and declared content type, but they do not scan for malware. A virus/malware scanner API <b>MUST</b> be invoked before the file is made available to users or
/// other systems.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessFileValidators
{
    extension<T, TFile>(IRuleBuilder<T, TFile?> builder)
        where TFile : class, IUploadedFile
    {
        /// <summary>Validates that the file is not empty (has a length greater than zero).</summary>
        /// <returns>Rule builder options for further chaining.</returns>
        public IRuleBuilderOptions<T, TFile?> FileNotEmpty()
        {
            return FileRules.NotEmpty(builder, static file => file.Length);
        }

        /// <summary>Validates that the file size is at least <paramref name="minBytes"/> bytes.</summary>
        /// <param name="minBytes">The minimum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MinSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
        public IRuleBuilderOptions<T, TFile?> GreaterThanOrEqualTo(long minBytes)
        {
            return FileRules.MinSize(builder, static file => file.Length, minBytes);
        }

        /// <summary>Validates that the file size does not exceed <paramref name="maxBytes"/> bytes.</summary>
        /// <param name="maxBytes">The maximum acceptable file size in bytes.</param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// The validation error message includes <c>{MaxSize}</c> (formatted as MB) and <c>{TotalLength}</c>
        /// (the actual file size formatted as MB) placeholder arguments.
        /// </remarks>
        public IRuleBuilderOptions<T, TFile?> LessThanOrEqualTo(long maxBytes)
        {
            return FileRules.MaxSize(builder, static file => file.Length, maxBytes);
        }

        /// <summary>
        /// Validates that the file's declared content type is one of the allowed <paramref name="contentTypes"/>.
        /// </summary>
        /// <param name="contentTypes">
        /// The list of permitted MIME type strings (e.g. <c>"image/png"</c>, <c>"application/pdf"</c>).
        /// Comparison is case-insensitive.
        /// </param>
        /// <returns>Rule builder options for further chaining.</returns>
        /// <remarks>
        /// This rule validates only the <em>declared</em> content type sent by the client and can be spoofed. Use
        /// <c>HaveSignatures</c> from Headless.FluentValidation.FileSignatures in addition to verify the actual binary
        /// signature. The validation error message includes a <c>{ContentTypes}</c> placeholder listing the allowed
        /// types.
        /// </remarks>
        public IRuleBuilderOptions<T, TFile?> ContentTypes(IReadOnlyList<string> contentTypes)
        {
            return FileRules.ContentTypes(builder, static file => file.ContentType, contentTypes);
        }
    }
}
