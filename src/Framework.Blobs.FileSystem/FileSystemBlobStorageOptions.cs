// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorageOptions
{
    public required string BaseDirectoryPath { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }
}

internal sealed class FileSystemBlobStorageOptionsValidator : AbstractValidator<FileSystemBlobStorageOptions>
{
    public FileSystemBlobStorageOptionsValidator()
    {
        RuleFor(x => x.BaseDirectoryPath).NotEmpty();
    }
}
