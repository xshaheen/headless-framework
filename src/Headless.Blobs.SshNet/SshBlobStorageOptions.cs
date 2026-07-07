// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Renci.SshNet;

namespace Headless.Blobs.SshNet;

/// <summary>Configuration for the SFTP blob storage provider.</summary>
[PublicAPI]
public sealed class SshBlobStorageOptions
{
    /// <summary>
    /// SFTP connection URI in the form <c>sftp://user:password@host:port</c>. Required.
    /// The password is URL-decoded before use, so special characters must be percent-encoded.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Optional proxy URI (<c>http://user:pass@host:port</c> or a SOCKS URI). When set,
    /// <see cref="ProxyType"/> is auto-detected from the scheme if left at <see cref="ProxyTypes.None"/>.
    /// </summary>
    public string? Proxy { get; set; }

    /// <summary>
    /// Proxy type to use with <see cref="Proxy"/>. Defaults to <see cref="ProxyTypes.None"/>. When a proxy
    /// URI is provided and this is <see cref="ProxyTypes.None"/>, HTTP proxy is auto-detected from the URI scheme.
    /// </summary>
    /// <remarks>
    /// This is a deliberate full-fidelity pass-through of the SSH.NET type <see cref="ProxyTypes"/>: the whole
    /// proxy-type vocabulary is exposed verbatim so no SSH.NET option is lost behind a lossy Headless wrapper. It
    /// intentionally couples this option to <c>SSH.NET</c>.
    /// </remarks>
    public ProxyTypes ProxyType { get; set; } = ProxyTypes.None;

    /// <summary>
    /// Optional private key stream for SSH public-key authentication. When provided, a
    /// <see cref="Renci.SshNet.PrivateKeyAuthenticationMethod"/> is added alongside any password method.
    /// </summary>
    public Stream? PrivateKey { get; set; }

    /// <summary>Passphrase for the <see cref="PrivateKey"/>, or <see langword="null"/> for unencrypted keys.</summary>
    public string? PrivateKeyPassPhrase { get; set; }

    /// <summary>
    /// Maximum concurrent operations for bulk upload/delete. Default is 4.
    /// SSH/SFTP connections have limited channel capacity, so this should be kept low.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 4;

    /// <summary>
    /// Maximum pooled SFTP connections. Must be >= MaxConcurrentOperations.
    /// Each connection uses ~40-70KB memory. Default is 4.
    /// </summary>
    public int MaxPoolSize { get; set; } = 4;

    /// <summary>
    /// Allow none-authentication fallback. When false (default), throws if no password or private key is provided.
    /// Set to true only if intentionally using passwordless authentication.
    /// </summary>
    public bool AllowNoneAuthentication { get; set; }
}

internal sealed class SshBlobStorageOptionsValidator : AbstractValidator<SshBlobStorageOptions>
{
    public SshBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.ProxyType).IsInEnum();
        RuleFor(x => x.MaxConcurrentOperations).InclusiveBetween(1, 100);

        RuleFor(x => x.MaxPoolSize)
            .InclusiveBetween(1, 100)
            .GreaterThanOrEqualTo(x => x.MaxConcurrentOperations)
            .WithMessage("MaxPoolSize must be >= MaxConcurrentOperations");
    }
}
