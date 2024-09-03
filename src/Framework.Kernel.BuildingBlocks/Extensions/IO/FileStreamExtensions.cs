using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;
using FileHelper = Framework.Kernel.BuildingBlocks.Helpers.IO.FileHelper;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

/// <summary>Provides a set of extension methods for operations on <see cref="Stream"/>.</summary>
[PublicAPI]
public static class FileStreamExtensions
{
    public static async ValueTask<Result<Exception>[]> SaveToLocalFileAsync(
        this IEnumerable<(Stream BlobStream, string BlobName)> blobs,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(directoryPath);

        Directory.CreateDirectory(directoryPath);

        var results = blobs.Select(async blob =>
        {
            try
            {
                await _BaseSaveFileAsync(blob.BlobStream, blob.BlobName, directoryPath, token);
                return Result<Exception>.Success();
            }
            catch (Exception e)
            {
                return Result<Exception>.Fail(e);
            }
        });

        return await Task.WhenAll(results).WithAggregatedExceptions();
    }

    public static async ValueTask SaveToLocalFileAsync(
        this Stream blobStream,
        string blobName,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(blobStream);
        Argument.IsNotNullOrEmpty(directoryPath);
        Argument.IsNotNullOrWhiteSpace(blobName);
        Directory.CreateDirectory(directoryPath);

        await _BaseSaveFileAsync(blobStream, blobName, directoryPath, token);
    }

    private static async Task _BaseSaveFileAsync(
        Stream blobStream,
        string uniqueSaveName,
        string directoryPath,
        CancellationToken token
    )
    {
        var filePath = Path.Combine(directoryPath, uniqueSaveName);
        await FileHelper.IoRetryPipeline.ExecuteAsync(writeFileAsync, (filePath, blobStream), token);

        return;

        static async ValueTask writeFileAsync((string FilePath, Stream BlobStream) state, CancellationToken token)
        {
            await using var fileStream = File.Open(state.FilePath, FileMode.Create, FileAccess.Write);
            await state.BlobStream.CopyToAsync(fileStream, token);
            await state.BlobStream.FlushAsync(token);
        }
    }

    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 is used for file integrity check."
    )]
    public static async Task<string> CalculateMd5Async(
        this Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream, cancellationToken);

        return Convert.ToBase64String(hash);
    }
}
