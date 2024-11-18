// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorageOptions
{
    public required string ConnectionString { get; set; }

    public string? Proxy { get; set; }

    public ProxyTypes ProxyType { get; set; } = ProxyTypes.None;

    public Stream? PrivateKey { get; set; }

    public string? PrivateKeyPassPhrase { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }
}

internal sealed class SshBlobStorageOptionsValidator : AbstractValidator<SshBlobStorageOptions>
{
    public SshBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.ProxyType).IsInEnum();
    }
}
