// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Core;
using Framework.Primitives;
using Humanizer;
using Polly;

namespace Framework.IO;

[PublicAPI]
public static class FileHelper
{
    #region Save

    private static readonly ResiliencePipeline _IoRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(
            new()
            {
                Name = "FileRetryPolicy",
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                Delay = 100.Milliseconds(),
                ShouldHandle = new PredicateBuilder().Handle<IOException>(),
            }
        )
        .Build();

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
                return Result<Exception>.Ok();
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
        // Reset stream position for seekable streams
        if (blobStream.CanSeek && blobStream.Position != 0)
        {
            blobStream.Seek(0, SeekOrigin.Begin);
        }

        var filePath = Path.Combine(directoryPath, uniqueSaveName);
        await _IoRetryPipeline.ExecuteAsync(writeFileAsync, (filePath, blobStream), token);

        return;

        static async ValueTask writeFileAsync((string FilePath, Stream BlobStream) state, CancellationToken token)
        {
            // Use FileShare.Read to allow concurrent read access during write
            await using var fileStream = new FileStream(
                state.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true
            );
            await state.BlobStream.CopyToAsync(fileStream, token);
        }
    }

    #endregion

    #region Delete If Exists

    /// <summary>Checks and deletes given a file if it does exist.</summary>
    /// <param name="filePath">Path of the file</param>
    public static bool DeleteIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);

        return true;
    }

    #endregion

    #region Read Content

    /// <summary>Opens a text file, reads content without BOM.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string?> ReadFileWithoutBomAsync(string path)
    {
        var bytes = await ReadAllBytesAsync(path);

        return StringHelper.ConvertFromBytesWithoutBom(bytes);
    }

    /// <summary>
    /// Opens a text file, reads all lines of the file, and then closes the file.
    /// </summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        using var reader = File.OpenText(path);

        return await reader.ReadToEndAsync();
    }

    /// <summary>Opens a text file, reads all lines of the file, and then closes the file.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<byte[]> ReadAllBytesAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open);

        var result = new byte[stream.Length];
        _ = await stream.ReadAsync(result.AsMemory(0, (int)stream.Length));

        return result;
    }

    /// <summary>Opens a text file, reads all lines of the file, and then closes the file.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <param name="encoding">Encoding of the file. Default is UTF8</param>
    /// <param name="fileMode">Specifies how the operating system should open a file. Default is Open</param>
    /// <param name="fileAccess">
    /// Defines constants for read, write, or read/write access to a file. Default
    /// is Read
    /// </param>
    /// <param name="fileShare">
    /// Contains constants for controlling the kind of access other FileStream objects can have to the
    /// same file. Default is Read
    /// </param>
    /// <param name="bufferSize">Length of StreamReader buffer. Default is 4096.</param>
    /// <param name="fileOptions">
    /// Indicates FileStream options. Default is Asynchronous (The file is to be used for
    /// asynchronous reading.) and SequentialScan (The file is to be accessed sequentially from beginning
    /// to end.)
    /// </param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string[]> ReadAllLinesAsync(
        string path,
        Encoding? encoding = null,
        FileMode fileMode = FileMode.Open,
        FileAccess fileAccess = FileAccess.Read,
        FileShare fileShare = FileShare.Read,
        int bufferSize = 4096,
        FileOptions fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan
    )
    {
        encoding ??= Encoding.UTF8;

        var lines = new List<string>();

        await using (var stream = new FileStream(path, fileMode, fileAccess, fileShare, bufferSize, fileOptions))
        using (var reader = new StreamReader(stream, encoding))
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
            }
        }

        return lines.AsArray();
    }

    #endregion
}
