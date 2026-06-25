// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Core;
using Headless.Primitives;
using Humanizer;
using Polly;
using File = System.IO.File;

namespace Headless.IO;

/// <summary>A helper class for reading, writing, and deleting files on the local file system.</summary>
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

    /// <summary>
    /// Saves each blob in <paramref name="blobs"/> as a file under <paramref name="directoryPath"/>, creating the
    /// directory if needed. Each save is attempted independently; a per-blob failure is captured as a failed
    /// <see cref="Result{T}"/> rather than thrown, so the returned array always has one entry per input blob.
    /// </summary>
    /// <param name="blobs">The blobs to save, each pairing a source stream with the file name to write it under.</param>
    /// <param name="directoryPath">The target directory; created if it does not already exist.</param>
    /// <param name="token">A token to observe while saving.</param>
    /// <returns>
    /// An array with one <see cref="Result{T}"/> per input blob: a success result, or a failure result carrying the
    /// <see cref="Exception"/> that the corresponding save raised.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blobs"/> or <paramref name="directoryPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobs"/> or <paramref name="directoryPath"/> is empty.</exception>
    /// <exception cref="IOException">Thrown when <paramref name="directoryPath"/> cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to create <paramref name="directoryPath"/>.</exception>
    public static async ValueTask<Result<Exception>[]> SaveToLocalFileAsync(
        this IEnumerable<(Stream BlobStream, string BlobName)> blobs,
        string directoryPath,
        CancellationToken token = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(directoryPath);

        Directory.CreateDirectory(directoryPath);

        var items = blobs as IReadOnlyList<(Stream BlobStream, string BlobName)> ?? blobs.ToList();
        var results = new Result<Exception>[items.Count];

        // Bound concurrency so a large batch does not open unbounded FileStreams at once (amplified by the
        // per-file retry pipeline). Each blob's outcome is captured independently into its slot, preserving the
        // one-result-per-input contract and the input ordering.
        await Parallel
            .ForAsync(
                0,
                items.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (index, _) =>
                {
                    var (blobStream, blobName) = items[index];

                    try
                    {
                        await _BaseSaveFileAsync(blobStream, blobName, directoryPath, token).ConfigureAwait(false);
                        results[index] = Result<Exception>.Ok();
                    }
                    catch (Exception e)
                    {
                        results[index] = Result<Exception>.Fail(e);
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Saves <paramref name="blobStream"/> as a file named <paramref name="blobName"/> under
    /// <paramref name="directoryPath"/>, creating the directory if needed. The write is retried on transient
    /// <see cref="IOException"/> failures.
    /// </summary>
    /// <param name="blobStream">The source stream to write.</param>
    /// <param name="blobName">The relative file name to write under <paramref name="directoryPath"/>; must not contain path traversal sequences or be rooted.</param>
    /// <param name="directoryPath">The target directory; created if it does not already exist.</param>
    /// <param name="token">A token to observe while saving.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blobStream"/> is <see langword="null"/>, or when <paramref name="directoryPath"/> or <paramref name="blobName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="directoryPath"/> is empty, when <paramref name="blobName"/> is empty or white space,
    /// or when <paramref name="blobName"/> is not a relative path segment (it contains traversal sequences or is rooted).
    /// </exception>
    /// <exception cref="IOException">Thrown when the directory or file cannot be created or written after retries are exhausted.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to create or write the file.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
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

        await _BaseSaveFileAsync(blobStream, blobName, directoryPath, token).ConfigureAwait(false);
    }

    private static async Task _BaseSaveFileAsync(
        Stream blobStream,
        string uniqueSaveName,
        string directoryPath,
        CancellationToken token
    )
    {
        _EnsureSafePathSegment(uniqueSaveName);

        var filePath = Path.Combine(directoryPath, uniqueSaveName);
        await _IoRetryPipeline.ExecuteAsync(writeFileAsync, (filePath, blobStream), token).ConfigureAwait(false);

        return;

        static async ValueTask writeFileAsync((string FilePath, Stream BlobStream) state, CancellationToken token)
        {
            // Reset position so every retry attempt writes the full stream from the start; the retry pipeline
            // can re-invoke this delegate after a partial write that advanced the source position.
            if (state.BlobStream.CanSeek && state.BlobStream.Position != 0)
            {
                state.BlobStream.Seek(0, SeekOrigin.Begin);
            }

            // Use FileShare.Read to allow concurrent read access during write
            await using var fileStream = new FileStream(
                state.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true
            );
            await state.BlobStream.CopyToAsync(fileStream, token).ConfigureAwait(false);
        }
    }

    private static void _EnsureSafePathSegment(string name)
    {
        // Defense in depth: the name is combined with the target directory via Path.Combine, so a name
        // containing traversal sequences or a rooted/absolute path could escape the directory and write
        // anywhere on disk. Path.IsPathRooted rejects every rooted form for the current platform, including
        // Windows drive-qualified (C:\x, D:/x, C:x) and UNC (\\server\share) names that a bare '/' or '\'
        // prefix check would miss. Mirrors Headless.Blobs PathValidation semantics, kept local to avoid a
        // dependency on the Blobs packages.
        var isSafePathSegment = !(
            name.Contains("../", StringComparison.Ordinal)
            || name.Contains("..\\", StringComparison.Ordinal)
            || name.Contains("/..", StringComparison.Ordinal)
            || name.Contains("\\..", StringComparison.Ordinal)
            || name.StartsWith("..", StringComparison.Ordinal)
            || name.EndsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(name)
        );

        Argument.IsTrue(
            isSafePathSegment,
            "The file name must be a relative path segment without traversal sequences.",
            nameof(name)
        );
    }

    #endregion

    #region Delete If Exists

    /// <summary>Deletes the file at <paramref name="filePath"/> if it exists.</summary>
    /// <param name="filePath">Path of the file.</param>
    /// <returns><see langword="true"/> if the file existed and was deleted; <see langword="false"/> if it did not exist.</returns>
    /// <exception cref="IOException">Thrown when the file is in use or cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission, or the path is a directory or read-only file.</exception>
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

    /// <summary>
    /// Opens a text file, reads its entire content as a string, decoding it without a byte-order mark, then closes the file.
    /// </summary>
    /// <param name="path">The file to open for reading.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>The file content as a string, or <see langword="null"/> when the file is empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file at <paramref name="path"/> is not found.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory in <paramref name="path"/> is not found.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the file.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to read the file.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async Task<string?> ReadFileWithoutBomAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        var bytes = await ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

        return StringHelper.ConvertFromBytesWithoutBom(bytes);
    }

    /// <summary>Opens a binary file, reads its entire content into a byte array, and then closes the file.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A byte array containing the entire content of the file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file at <paramref name="path"/> is not found.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory in <paramref name="path"/> is not found.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the file.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to read the file.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(path, cancellationToken);
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
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>An array containing all lines of the file, one element per line.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file at <paramref name="path"/> is not found.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory in <paramref name="path"/> is not found.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the file.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to read the file.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async Task<string[]> ReadAllLinesAsync(
        string path,
        Encoding? encoding = null,
        FileMode fileMode = FileMode.Open,
        FileAccess fileAccess = FileAccess.Read,
        FileShare fileShare = FileShare.Read,
        int bufferSize = 4096,
        FileOptions fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan,
        CancellationToken cancellationToken = default
    )
    {
        encoding ??= Encoding.UTF8;

        var lines = new List<string>();

        await using (var stream = new FileStream(path, fileMode, fileAccess, fileShare, bufferSize, fileOptions))
        using (var reader = new StreamReader(stream, encoding))
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lines.Add(line);
            }
        }

        return lines.AsArray();
    }

    #endregion
}
