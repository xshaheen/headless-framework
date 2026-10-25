// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation;
using Headless.Checks;
using Headless.FluentValidation.Resources;

namespace Headless.FluentValidation;

/// <summary>
/// Builds the signature rule once for every file type: each entry point (<c>IUploadedFile</c> here, ASP.NET Core
/// <c>IFormFile</c> in Headless.Api.FluentValidation) passes how to open the content, so the check and its error
/// descriptor cannot drift between them.
/// </summary>
internal static class FileSignatureRules
{
    public static IRuleBuilderOptions<T, TFile?> Signatures<T, TFile>(
        IRuleBuilder<T, TFile?> builder,
        Func<TFile, Stream> openReadStream,
        IFileFormatInspector inspector,
        Func<FileFormat?, bool> predicate
    )
        where TFile : class
    {
        Argument.IsNotNull(inspector);
        Argument.IsNotNull(predicate);

        return builder
            .MustAsync(
                async (file, cancellationToken) =>
                {
                    if (file is null)
                    {
                        return true;
                    }

                    // The inspection itself is synchronous, so cancellation is honored before the content is opened.
                    cancellationToken.ThrowIfCancellationRequested();

                    await using var stream = openReadStream(file);
                    var format = inspector.DetermineFileFormat(stream);

                    return predicate(format);
                }
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Files.UnexpectedSignature);
    }
}
