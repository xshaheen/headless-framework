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
/// These are the rules of <see cref="HeadlessFileValidators"/> and <see cref="HeadlessFileSignatureValidators"/>, read
/// from an <see cref="IFormFile"/>: they emit the same error codes and messages and expose the same bounds to API
/// documentation, and each member's documentation is inherited from its <c>IUploadedFile</c> counterpart. A command
/// that should not depend on ASP.NET Core takes an <c>IUploadedFile</c> instead, built at the edge with
/// <c>IFormFile.ToUploadedFile()</c>, and validates it with those classes.
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
        /// <inheritdoc cref="HeadlessFileValidators.extension{T, TFile}(IRuleBuilder{T, TFile}).FileNotEmpty"/>
        public IRuleBuilderOptions<T, TFormFile?> FileNotEmpty()
        {
            return FileRules.NotEmpty(builder, static file => file.Length);
        }

        /// <inheritdoc cref="HeadlessFileValidators.extension{T, TFile}(IRuleBuilder{T, TFile}).GreaterThanOrEqualTo"/>
        public IRuleBuilderOptions<T, TFormFile?> GreaterThanOrEqualTo(long minBytes)
        {
            return FileRules.MinSize(builder, static file => file.Length, minBytes);
        }

        /// <inheritdoc cref="HeadlessFileValidators.extension{T, TFile}(IRuleBuilder{T, TFile}).LessThanOrEqualTo"/>
        public IRuleBuilderOptions<T, TFormFile?> LessThanOrEqualTo(long maxBytes)
        {
            return FileRules.MaxSize(builder, static file => file.Length, maxBytes);
        }

        /// <inheritdoc cref="HeadlessFileValidators.extension{T, TFile}(IRuleBuilder{T, TFile}).ContentTypes"/>
        public IRuleBuilderOptions<T, TFormFile?> ContentTypes(IReadOnlyList<string> contentTypes)
        {
            return FileRules.ContentTypes(builder, static file => file.ContentType, contentTypes);
        }

        /// <inheritdoc cref="HeadlessFileSignatureValidators.extension{T, TFile}(IRuleBuilder{T, TFile}).HaveSignatures"/>
        public IRuleBuilderOptions<T, TFormFile?> HaveSignatures(
            IFileFormatInspector inspector,
            Func<FileFormat?, bool> predicate
        )
        {
            return FileSignatureRules.Signatures(builder, static file => file.OpenReadStream(), inspector, predicate);
        }
    }
}
