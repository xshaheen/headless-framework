// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs.CloudflareR2;

/// <summary>
/// Configuration for the Cloudflare R2 blob storage provider.
/// </summary>
/// <remarks>
/// R2 is accessed via the S3-compatible API using <see cref="AccountId"/>, <see cref="AccessKeyId"/>, and
/// <see cref="SecretAccessKey"/>. The S3 endpoint is derived from <see cref="Jurisdiction"/> unless
/// <see cref="EndpointUrl"/> is explicitly set. R2 does not support ACLs, chunked encoding, or payload signing;
/// these are disabled automatically by the setup registration.
/// </remarks>
[PublicAPI]
public sealed class R2BlobStorageOptions
{
    /// <summary>Cloudflare account id used to build the R2 S3 endpoint. Required.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>R2 access key id. Required.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>R2 secret access key. Required.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Jurisdiction selecting the R2 endpoint when <see cref="EndpointUrl"/> is not set.
    /// Defaults to <see cref="R2Jurisdiction.Default"/> (the global endpoint).
    /// </summary>
    public R2Jurisdiction Jurisdiction { get; set; } = R2Jurisdiction.Default;

    /// <summary>
    /// Optional explicit endpoint override. When set, it takes precedence over <see cref="Jurisdiction"/>.
    /// A <c>{0}</c> placeholder is replaced with <see cref="AccountId"/>.
    /// </summary>
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// Resolves the S3 endpoint URL for this configuration. Returns <see cref="EndpointUrl"/> (with
    /// <see cref="AccountId"/> substituted for <c>{0}</c>) when set; otherwise derives the URL from
    /// <see cref="AccountId"/> and <see cref="Jurisdiction"/>.
    /// </summary>
    /// <returns>The fully qualified HTTPS endpoint URL to use for S3 API calls against R2.</returns>
    public string GetEffectiveEndpointUrl()
    {
        if (!string.IsNullOrWhiteSpace(EndpointUrl))
        {
            // Plain token replacement, not string.Format: a custom URL may contain literal braces that are not
            // a format placeholder, which would otherwise throw FormatException at client construction.
            return EndpointUrl.Replace("{0}", AccountId, StringComparison.Ordinal);
        }

        var infix = Jurisdiction switch
        {
            R2Jurisdiction.EuropeanUnion => ".eu",
            R2Jurisdiction.FedRamp => ".fedramp",
            _ => string.Empty,
        };

        return $"https://{AccountId}{infix}.r2.cloudflarestorage.com";
    }
}

/// <summary>Cloudflare R2 jurisdiction, selecting the geographic S3 endpoint.</summary>
[PublicAPI]
public enum R2Jurisdiction
{
    /// <summary>Global endpoint: <c>https://{account}.r2.cloudflarestorage.com</c>.</summary>
    Default = 0,

    /// <summary>European Union endpoint: <c>https://{account}.eu.r2.cloudflarestorage.com</c>.</summary>
    EuropeanUnion = 1,

    /// <summary>FedRAMP endpoint: <c>https://{account}.fedramp.r2.cloudflarestorage.com</c>.</summary>
    FedRamp = 2,
}

internal sealed class R2BlobStorageOptionsValidator : AbstractValidator<R2BlobStorageOptions>
{
    public R2BlobStorageOptionsValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.AccessKeyId).NotEmpty();
        RuleFor(x => x.SecretAccessKey).NotEmpty();
    }
}
