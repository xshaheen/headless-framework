// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs.Aws;

/// <summary>
/// Configuration for an S3-compatible endpoint (MinIO, Ceph RGW, Garage, SeaweedFS, and similar) reached through
/// the AWS S3 engine.
/// </summary>
/// <remarks>
/// The registration always uses path-style addressing, sends checksums only when an operation requires them,
/// sends no canned ACL, and uploads a signed, non-chunked body. S3-compatible servers commonly reject the AWS SDK
/// v4 defaults for these, so they are not configurable here; use <c>UseAws</c> with an explicit
/// <c>AWSOptions</c> when an endpoint needs a different combination.
/// </remarks>
[PublicAPI]
public sealed class S3CompatibleBlobStorageOptions
{
    /// <summary>Absolute endpoint URL, for example <c>https://minio.internal:9000</c>. Required.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Access key id. Required.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Secret access key. Required.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Region used to sign requests. Defaults to <c>us-east-1</c>, the region MinIO and most S3-compatible
    /// servers assume when none is configured on the server.
    /// </summary>
    public string AuthenticationRegion { get; set; } = "us-east-1";

    /// <summary>
    /// Allows a plaintext <c>http://</c> <see cref="ServiceUrl"/>. Defaults to <see langword="false"/>, so a
    /// local-development endpoint cannot silently ship credentials and data unencrypted to production.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Maximum degree of parallelism for bulk operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;
}

internal sealed class S3CompatibleBlobStorageOptionsValidator : AbstractValidator<S3CompatibleBlobStorageOptions>
{
    public S3CompatibleBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ServiceUrl)
            .NotEmpty()
            .Must(url => _TryGetScheme(url) is not null)
            .WithMessage("'{PropertyName}' must be an absolute http or https URL.");

        RuleFor(x => x.ServiceUrl)
            .Must(url => !string.Equals(_TryGetScheme(url), Uri.UriSchemeHttp, StringComparison.Ordinal))
            .When(x => !x.AllowInsecureHttp)
            .WithMessage(
                "'{PropertyName}' uses plaintext http. Set AllowInsecureHttp to true to allow it (local development only)."
            );

        RuleFor(x => x.AccessKeyId).NotEmpty();
        RuleFor(x => x.SecretAccessKey).NotEmpty();
        RuleFor(x => x.AuthenticationRegion).NotEmpty();
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }

    private static string? _TryGetScheme(string? url)
    {
        return
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            )
            ? uri.Scheme
            : null;
    }
}
