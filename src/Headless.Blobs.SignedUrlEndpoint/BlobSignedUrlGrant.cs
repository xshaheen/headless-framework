// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>What a verified token grants: one verb on one blob of one store, until an instant.</summary>
/// <param name="Store">The named store, or <see langword="null"/> for the default (unkeyed) store.</param>
internal sealed record BlobSignedUrlGrant(
    BlobSignedUrlAccess Access,
    string? Store,
    BlobLocation Location,
    DateTimeOffset ExpiresAt,
    string? ContentType,
    long? MaxLength
);
