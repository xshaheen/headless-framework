// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using Headless.FluentValidation;
using Headless.IO;

namespace FluentValidation;

/// <summary>
/// FluentValidation rule that checks the binary signature of a file property typed as <see cref="IUploadedFile"/> or
/// one of its implementations.
/// </summary>
/// <remarks>
/// <para>
/// It lives here rather than in <c>Headless.FluentValidation</c>, which nearly every Headless package references, so
/// only file-handling API hosts carry FileSignatures and its OpenMcdf dependency. An ASP.NET Core request model whose
/// property is an <c>IFormFile</c> uses the same rule; both emit the same error code and message.
/// </para>
/// <para>
/// <b>Security note:</b> the rule checks the binary signature only; it does not scan for malware. A virus/malware
/// scanner API <b>MUST</b> be invoked before the file is made available to users or other systems.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessFileSignatureValidators
{
    extension<T, TFile>(IRuleBuilder<T, TFile?> builder)
        where TFile : class, IUploadedFile
    {
        /// <summary>
        /// Validates the file's binary signature (magic bytes) using the provided <paramref name="inspector"/> and
        /// <paramref name="predicate"/>. A <see langword="null"/> file passes.
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
        /// The rule opens its own stream with <see cref="IUploadedFile.OpenReadStream"/> and disposes it after
        /// inspection, so the code that stores the file can still read the whole content. The inspector requires a
        /// seekable stream, as the <see cref="IUploadedFile"/> contract states. The rule is asynchronous, so validate
        /// with <c>ValidateAsync</c>; it checks the validation's cancellation token before it opens the content.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="inspector"/> or <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        public IRuleBuilderOptions<T, TFile?> HaveSignatures(
            IFileFormatInspector inspector,
            Func<FileFormat?, bool> predicate
        )
        {
            return FileSignatureRules.Signatures(builder, static file => file.OpenReadStream(), inspector, predicate);
        }
    }
}
