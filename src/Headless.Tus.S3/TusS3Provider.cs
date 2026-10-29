// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>The kind of S3 endpoint the store talks to, which decides the per-request settings it sends.</summary>
public enum TusS3Provider
{
    /// <summary>Amazon S3. Requests keep the AWS SDK's payload-signing default.</summary>
    Aws = 0,

    /// <summary>
    /// A self-hosted S3-compatible server such as MinIO. Payloads stay signed, because such servers are often reached
    /// over plain http, where the AWS SDK refuses unsigned payloads.
    /// </summary>
    S3Compatible = 1,

    /// <summary>
    /// Cloudflare R2. Payloads are sent unsigned, and an unset <see cref="TusS3StoreOptions.MultipartUploadLifetime"/>
    /// resolves to R2's seven-day abort window.
    /// </summary>
    R2 = 2,
}
