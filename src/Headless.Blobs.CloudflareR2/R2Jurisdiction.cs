// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs.CloudflareR2;

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
