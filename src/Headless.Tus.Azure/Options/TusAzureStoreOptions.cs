// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Headless.Tus.Options;

/// <summary>
/// Configuration for the TUS Azure Blob Storage store.
/// </summary>
/// <remarks>
/// Options are validated at startup via FluentValidation. The container is created synchronously
/// in the <c>TusAzureStore</c> constructor when <see cref="CreateContainerIfNotExists"/> is
/// <see langword="true"/>, so any connectivity or authorization failure surfaces immediately
/// rather than on the first upload request.
/// </remarks>
public sealed class TusAzureStoreOptions
{
    /// <summary>The name of the Azure Blob Storage container to use for storing uploads.</summary>
    public string ContainerName { get; set; } = "tus-uploads";

    /// <summary>
    /// A prefix prepended to every blob name within the container, acting as a virtual folder path.
    /// </summary>
    /// <remarks>A trailing slash is appended automatically when building blob names, so
    /// <c>"uploads"</c> and <c>"uploads/"</c> are equivalent.</remarks>
    public string BlobPrefix { get; set; } = "uploads/";

    /// <summary>
    /// When <see langword="true"/>, the container is created synchronously during store construction
    /// if it does not already exist.
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, incoming PATCH data is split into chunks no larger than
    /// <see cref="BlobMaxChunkSize"/> before being staged as Azure block blob blocks.
    /// </summary>
    /// <remarks>
    /// Enable this when clients may send chunks that exceed Azure Block Blob's 100 MB per-block
    /// limit. With splitting disabled, the entire PATCH body is staged as a single block, which
    /// fails for bodies larger than the limit.
    /// </remarks>
    public bool EnableChunkSplitting { get; set; } = true;

    /// <summary>
    /// Maximum size in bytes of a single Azure block blob block used when chunk splitting is
    /// enabled.
    /// </summary>
    /// <remarks>
    /// Must be between 1 byte and the Azure Block Blob limit of 100 MB (104,857,600 bytes).
    /// Defaults to 16 MB: this value is also the per-request buffering unit for uploads of
    /// 100 MB and above (both append paths hold one block in memory at a time), so the default
    /// bounds memory at 16 MB per concurrent large upload while still allowing 16 MB × 50,000
    /// blocks = ~780 GB per upload. Raise it for higher single-upload throughput at the cost of
    /// proportional memory per concurrent upload.
    /// </remarks>
    public int BlobMaxChunkSize { get; set; } = 16 * 1024 * 1024; // 16MB

    /// <summary>
    /// Default block size in bytes used for medium-sized uploads when chunk splitting is enabled.
    /// </summary>
    /// <remarks>Defaults to 4 MB. The store selects between this value and
    /// <see cref="BlobMaxChunkSize"/> based on total upload size heuristics.</remarks>
    public int BlobDefaultChunkSize { get; set; } = 4 * 1024 * 1024; // 4MB

    /// <summary>Public access level applied when creating the container.</summary>
    /// <remarks>Only used when <see cref="CreateContainerIfNotExists"/> is <see langword="true"/>
    /// and the container does not yet exist. Defaults to <c>None</c> (private).</remarks>
    public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;
}

internal sealed class TusAzureStoreOptionsValidator : AbstractValidator<TusAzureStoreOptions>
{
    public TusAzureStoreOptionsValidator()
    {
        RuleFor(x => x.ContainerName).NotEmpty();

        RuleFor(x => x.BlobMaxChunkSize)
            .InclusiveBetween(1, 100 * 1024 * 1024) // 100MB
            .WithMessage("BlockBlobMaxChunkSize must be between 1 byte and 100MB");

        // BlobDefaultChunkSize feeds _CalculateOptimalChunkSize/_SplitStreamAsync directly; an out-of-range value
        // (<= 0 or > 100MB) either trips the Azure 100MB block guard or stalls staging, so validate it like the max.
        RuleFor(x => x.BlobDefaultChunkSize)
            .InclusiveBetween(1, 100 * 1024 * 1024) // 100MB
            .WithMessage("BlobDefaultChunkSize must be between 1 byte and 100MB")
            .LessThanOrEqualTo(x => x.BlobMaxChunkSize)
            .WithMessage("BlobDefaultChunkSize must not exceed BlobMaxChunkSize");

        RuleFor(x => x.ContainerPublicAccessType).IsInEnum();
    }
}
