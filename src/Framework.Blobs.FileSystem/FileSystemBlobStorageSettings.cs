using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobStorageSettings
{
    public required string BaseDirectoryPath { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }
}

public sealed class FileSystemBlobStorageSettingsValidator : AbstractValidator<FileSystemBlobStorageSettings>
{
    public FileSystemBlobStorageSettingsValidator()
    {
        RuleFor(x => x.BaseDirectoryPath).NotEmpty();
    }
}
