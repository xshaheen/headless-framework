// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.FileSystem;

/// <summary>
/// <see cref="IBlobContainerManager"/> implementation for the file-system backend, where a top-level container is a
/// directory directly under <see cref="FileSystemBlobStorageOptions.BaseDirectoryPath"/>. Registered separately from
/// the storage (a resolved capability, not a cast) so it is discoverable only where it is honestly supported (KTD5).
/// </summary>
/// <remarks>
/// Creating intermediate path directories while writing a blob is path creation handled by
/// <see cref="FileSystemBlobStorage"/> itself, not container management; this type owns only the lifecycle of the
/// container root directory.
/// </remarks>
internal sealed class FileSystemBlobContainerManager : IBlobContainerManager
{
    private readonly string _basePath;
    private readonly IBlobNamingNormalizer _normalizer;

    public FileSystemBlobContainerManager(
        IOptions<FileSystemBlobStorageOptions> optionsAccessor,
        IBlobNamingNormalizer normalizer
    )
    {
        Argument.IsNotNull(optionsAccessor);
        _basePath = optionsAccessor.Value.BaseDirectoryPath.NormalizePath().EnsureEndsWith(Path.DirectorySeparatorChar);
        _normalizer = Argument.IsNotNull(normalizer);
    }

    public ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Idempotent: Directory.CreateDirectory is a no-op when the directory already exists.
        Directory.CreateDirectory(_ResolveContainerDirectory(container));

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Directory.Exists(_ResolveContainerDirectory(container)));
    }

    public ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = _ResolveContainerDirectory(container);

        if (!Directory.Exists(directory))
        {
            return ValueTask.FromResult(false);
        }

        Directory.Delete(directory, recursive: true);

        return ValueTask.FromResult(true);
    }

    private string _ResolveContainerDirectory(string container)
    {
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        var normalized = _normalizer.NormalizeContainerName(container);
        var directory = Path.Combine(_basePath, normalized);

        // Defense-in-depth: the resolved container directory must stay under the base directory.
        var fullPath = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(_basePath, fullPath);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new ArgumentException(
                $"Path traversal detected: the resolved container directory escapes the base directory ('{fullPath}')",
                nameof(container)
            );
        }

        return directory;
    }
}
