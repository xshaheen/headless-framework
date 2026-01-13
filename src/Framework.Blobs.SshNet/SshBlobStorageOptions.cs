// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Renci.SshNet;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorageOptions
{
    public required string ConnectionString { get; set; }

    public string? Proxy { get; set; }

    public ProxyTypes ProxyType { get; set; } = ProxyTypes.None;

    public Stream? PrivateKey { get; set; }

    public string? PrivateKeyPassPhrase { get; set; }

    /// <summary>
    /// Maximum concurrent operations for bulk upload/delete. Default is 4.
    /// SSH/SFTP connections have limited channel capacity, so this should be kept low.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 4;

    /// <summary>
    /// Allow none-authentication fallback. When false (default), throws if no password or private key is provided.
    /// Set to true only if intentionally using passwordless authentication.
    /// </summary>
    public bool AllowNoneAuthentication { get; set; } = false;
}

internal sealed class SshBlobStorageOptionsValidator : AbstractValidator<SshBlobStorageOptions>
{
    public SshBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.ProxyType).IsInEnum();
        RuleFor(x => x.MaxConcurrentOperations).InclusiveBetween(1, 100);
    }
}
