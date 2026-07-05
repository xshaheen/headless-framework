// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.IO;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class FormFileExtensions
{
    /// <summary>Saves the uploaded file to <paramref name="directoryPath"/> on the local filesystem.</summary>
    /// <param name="formFile">The form file to save.</param>
    /// <param name="directoryPath">Absolute or relative directory path where the file is written.</param>
    /// <param name="token">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="formFile"/> is <see langword="null"/>.</exception>
    public static async ValueTask SaveAsync(
        this IFormFile formFile,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(formFile);

        await using var blobStream = formFile.OpenReadStream();

        await blobStream.SaveToLocalFileAsync(formFile.FileName, directoryPath, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves all files in <paramref name="files"/> to <paramref name="directoryPath"/> in parallel.
    /// </summary>
    /// <param name="files">Collection of uploaded files.</param>
    /// <param name="directoryPath">Absolute or relative directory path where the files are written.</param>
    /// <param name="token">Cancellation token propagated to each individual save.</param>
    /// <returns>
    /// An array of <c>Result&lt;Exception&gt;</c> in the same order as <paramref name="files"/>,
    /// where each entry is either a success or the exception thrown when saving that file.
    /// </returns>
    public static async ValueTask<Result<Exception>[]> SaveAsync(
        this IReadOnlyCollection<IFormFile> files,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(files);

        var indexedFiles = files.Select((formFile, index) => (formFile, index)).ToArray();
        var results = new Result<Exception>[indexedFiles.Length];

        await Parallel
            .ForEachAsync(
                indexedFiles,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (item, _) =>
                {
                    try
                    {
                        await item.formFile.SaveAsync(directoryPath, token).ConfigureAwait(false);
                        results[item.index] = Result<Exception>.Ok();
                    }
                    catch (Exception ex)
                    {
                        results[item.index] = Result<Exception>.Fail(ex);
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    /// <summary>Computes the MD5 hash of the uploaded file's content stream.</summary>
    /// <param name="file">The uploaded file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lowercase hex-encoded MD5 hash string.</returns>
    public static async ValueTask<string> CalculateMd5Async(
        this IFormFile file,
        CancellationToken cancellationToken = default
    )
    {
        await using var stream = file.OpenReadStream();

        return await stream.CalculateMd5Async(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads all bytes from the uploaded file's content stream synchronously.</summary>
    /// <param name="file">The uploaded file.</param>
    /// <returns>A byte array containing the full file content.</returns>
    public static byte[] GetAllBytes(this IFormFile file)
    {
        using var stream = file.OpenReadStream();

        return stream.GetAllBytes();
    }

    /// <summary>Reads all bytes from the uploaded file's content stream asynchronously.</summary>
    /// <param name="file">The uploaded file.</param>
    /// <returns>A byte array containing the full file content.</returns>
    public static async Task<byte[]> GetAllBytesAsync(this IFormFile file)
    {
        await using var stream = file.OpenReadStream();

        return await stream.GetAllBytesAsync().ConfigureAwait(false);
    }
}
