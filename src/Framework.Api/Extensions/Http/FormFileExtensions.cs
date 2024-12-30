// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.IO;
using Framework.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class FormFileExtensions
{
    /// <summary>Save a the file to the <paramref name="directoryPath"/> and return file name.</summary>
    /// <param name="formFile">File to be saved</param>
    /// <param name="directoryPath">The directory to save the file to.</param>
    /// <param name="token"></param>
    public static async ValueTask SaveAsync(
        this IFormFile formFile,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(formFile);

        await using var blobStream = formFile.OpenReadStream();

        await blobStream.SaveToLocalFileAsync(formFile.FileName, directoryPath, token);
    }

    public static async ValueTask<Result<Exception>[]> SaveAsync(
        this IReadOnlyCollection<IFormFile> files,
        string directoryPath,
        CancellationToken token = default
    )
    {
        var tasks = files.Select(async formFile =>
        {
            try
            {
                await formFile.SaveAsync(directoryPath, token);

                return Result<Exception>.Success();
            }
            catch (Exception ex)
            {
                return Result<Exception>.Fail(ex);
            }
        });

        var result = await Task.WhenAll(tasks).WithAggregatedExceptions();

        return result;
    }

    public static async ValueTask<string> CalculateMd5Async(
        this IFormFile file,
        CancellationToken cancellationToken = default
    )
    {
        await using var stream = file.OpenReadStream();

        return await stream.CalculateMd5Async(cancellationToken);
    }

    public static byte[] GetAllBytes(this IFormFile file)
    {
        using var stream = file.OpenReadStream();

        return stream.GetAllBytes();
    }

    public static async Task<byte[]> GetAllBytesAsync(this IFormFile file)
    {
        await using var stream = file.OpenReadStream();

        return await stream.GetAllBytesAsync();
    }
}
