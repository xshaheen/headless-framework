using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using FluentDate;
using Framework.BuildingBlocks.Helpers;
using Polly;
using Polly.Retry;
using FileHelper = Framework.BuildingBlocks.Helpers.IO.FileHelper;
using PredicateBuilder = Polly.PredicateBuilder;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

/// <summary>Provides a set of extension methods for operations on <see cref="Stream"/>.</summary>
[PublicAPI]
public static class FileStreamExtensions
{
    public static readonly RetryStrategyOptions IoRetryStrategyOptions =
        new()
        {
            Name = "BlobToLocalFileRetryPolicy",
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            Delay = 0.5.Seconds(),
            ShouldHandle = new PredicateBuilder().Handle<IOException>()
        };

    public static readonly ResiliencePipeline IoRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(IoRetryStrategyOptions)
        .Build();

    [MustUseReturnValue]
    public static async ValueTask<(string SavedName, string DisplayName, long Size)[]> SaveToLocalFileAsync(
        this IEnumerable<(Stream BlobStream, string BlobName)> blobs,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(directoryPath);

        Directory.CreateDirectory(directoryPath);
        var results = blobs.Select(blob => _BaseSaveFileAsync(blob.BlobStream, blob.BlobName, directoryPath, token));

        return await Task.WhenAll(results);
    }

    [MustUseReturnValue]
    public static async ValueTask<(string SavedName, string DisplayName, long Size)> SaveToLocalFileAsync(
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

        return await _BaseSaveFileAsync(blobStream, blobName, directoryPath, token);
    }

    [MustUseReturnValue]
    private static async Task<(string SavedName, string DisplayName, long Size)> _BaseSaveFileAsync(
        Stream blobStream,
        string blobName,
        string directoryPath,
        CancellationToken token
    )
    {
        var (trustedFileNameForDisplay, uniqueSaveName) = FileHelper.GetTrustedFileNames(blobName);
        var filePath = Path.Combine(directoryPath, uniqueSaveName);
        await IoRetryPipeline.ExecuteAsync(_WriteFileAsync, (filePath, blobStream), token);

        return (uniqueSaveName, trustedFileNameForDisplay, blobStream.Length);
    }

    private static async ValueTask _WriteFileAsync((string FilePath, Stream BlobStream) state, CancellationToken token)
    {
        await using var fileStream = File.Open(state.FilePath, FileMode.Create, FileAccess.Write);
        await state.BlobStream.CopyToAsync(fileStream, token);
        await state.BlobStream.FlushAsync(token);
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
