using FluentValidation;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorageOptions
{
    public required string ConnectionString { get; set; }

    public required string Proxy { get; set; }

    public ProxyTypes ProxyType { get; set; } = ProxyTypes.None;

    public Stream? PrivateKey { get; set; }

    public string? PrivateKeyPassPhrase { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }
}

public sealed class SshBlobStorageOptionsValidator : AbstractValidator<SshBlobStorageOptions>
{
    public SshBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.Proxy).NotEmpty();
        RuleFor(x => x.ProxyType).IsInEnum();
    }
}
