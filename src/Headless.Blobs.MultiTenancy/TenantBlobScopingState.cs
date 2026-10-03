// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>Holds the decoration the blob seam registered, so startup validation can tell whether it wrapped a store.</summary>
internal sealed class TenantBlobScopingState(BlobStorageDecoration decoration)
{
    public BlobStorageDecoration Decoration { get; } = decoration;
}
