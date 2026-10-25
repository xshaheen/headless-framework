// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation;
using Headless.Checks;
using Headless.FluentValidation.Resources;

namespace Headless.FluentValidation;

/// <summary>
/// Builds the signature rule for any file type, given how to open its content. <c>HeadlessFormFileValidators</c> reads it
/// from an <c>IFormFile</c>.
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
