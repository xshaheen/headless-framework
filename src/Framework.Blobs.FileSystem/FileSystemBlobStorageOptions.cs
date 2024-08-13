using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorageOptions
{
    public required string BaseDirectoryPath { get; init; }

    public ILoggerFactory? LoggerFactory { get; init; }
}

public sealed class FileSystemBlobStorageOptionsValidator : AbstractValidator<FileSystemBlobStorageOptions>
{
    public FileSystemBlobStorageOptionsValidator()
    {
        RuleFor(x => x.BaseDirectoryPath).NotEmpty();
    }
}
