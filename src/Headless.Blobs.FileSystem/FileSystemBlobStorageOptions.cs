// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs.FileSystem;

/// <summary>Configuration for the file-system blob storage provider.</summary>
public sealed class FileSystemBlobStorageOptions
{
    /// <summary>
    /// Absolute path to the root directory under which all containers and blobs are stored. Required.
    /// All blob paths are resolved relative to this directory, and path-traversal attempts that escape it
    /// are rejected with an <see cref="ArgumentException"/>.
    /// </summary>
    public required string BaseDirectoryPath { get; set; }
}

internal sealed class FileSystemBlobStorageOptionsValidator : AbstractValidator<FileSystemBlobStorageOptions>
{
    public FileSystemBlobStorageOptionsValidator()
    {
        RuleFor(x => x.BaseDirectoryPath)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(path => Path.IsPathFullyQualified(path))
            .WithMessage("'{PropertyName}' must be an absolute, fully-qualified path.");
    }
}
