// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using FluentValidation;

namespace Headless.Tus;

/// <summary>
/// Configuration for the TUS S3 store.
/// </summary>
/// <remarks>
/// Options are validated at startup via FluentValidation, and again when the store is constructed. The store does not
/// create its bucket: <c>resources provision -k S3Bucket</c> does, as a deploy step. The <c>IAmazonS3</c>
/// client comes from the application's DI registration, and its client-level settings (endpoint, path style,
/// authentication region, checksum mode) are the application's to set.
/// </remarks>
public sealed class TusS3StoreOptions
{
    /// <summary>The minimum size of every part except the last, which S3 and R2 both enforce.</summary>
    internal const int MinPartSize = 5 * 1024 * 1024;

    /// <summary>
    /// The largest part the store buffers. Each part is held in one pooled array, so the cap stays well below the
    /// provider's 5 GiB part limit, which a .NET array cannot reach.
    /// </summary>
    internal const int MaxPartSizeLimit = 1024 * 1024 * 1024;

    /// <summary>The abort window Cloudflare R2 applies to incomplete multipart uploads.</summary>
    /// <summary>The widest claim window, which still leaves 9,000 of the 10,000 part numbers for the upload's length.</summary>
    internal const int MaxPartClaimWindow = 250;

    internal static readonly TimeSpan R2MultipartUploadLifetime = TimeSpan.FromDays(7);

    /// <summary>The bucket that holds the uploads. Required.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>
    /// The key prefix the store owns inside <see cref="Bucket"/>. Do not share it with other writers.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>uploads/</c> and must not be empty. A trailing slash is appended when building keys, so
    /// <c>"uploads"</c> and <c>"uploads/"</c> are equivalent.
    /// </remarks>
    public string Prefix { get; set; } = "uploads/";

    /// <summary>The kind of endpoint the injected client talks to. Defaults to <see cref="TusS3Provider.Aws"/>.</summary>
    public TusS3Provider Provider { get; set; } = TusS3Provider.Aws;

    /// <summary>
    /// The part size, in bytes, for an upload whose length is deferred or small enough to fit the part budget at this
    /// size. Defaults to 8 MiB and must be at least 5 MiB.
    /// </summary>
    /// <remarks>
    /// A part is buffered in memory before it is uploaded, so this value times
    /// <see cref="MaxConcurrentPartUploads"/> bounds the memory one PATCH request holds.
    /// </remarks>
    public int PartSize { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// The largest part size, in bytes, the store picks for a large upload of known length. Defaults to 64 MiB; must
    /// be at least <see cref="PartSize"/> and at most 1 GiB. An upload that would need larger parts is rejected at
    /// creation.
    /// </summary>
    public int MaxPartSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>The number of parts one PATCH request uploads in parallel. Defaults to 2; must be at least 1.</summary>
    public int MaxConcurrentPartUploads { get; set; } = 2;

    /// <summary>
    /// How many part numbers a PATCH request reserves with one state write before uploading parts. Defaults to 64;
    /// must be between 1 and 250.
    /// </summary>
    /// <remarks>
    /// Four windows of part numbers are held back from the 10,000 parts a multipart upload allows, so a wider window
    /// lowers the largest upload: at the defaults an upload created without a length can grow to 9,744 parts.
    /// </remarks>
    public int PartClaimWindow { get; set; } = 64;

    /// <summary>
    /// The number of tail segments an upload may hold before the store folds them into a part. Defaults to 16; must
    /// be at least 1.
    /// </summary>
    public int MaxTailSegments { get; set; } = 16;

    /// <summary>
    /// How long the store keeps trying to persist bytes it already received after the client disconnects. Defaults
    /// to 30 seconds; must be positive.
    /// </summary>
    public TimeSpan PersistTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the provider keeps an incomplete multipart upload before aborting it, when the bucket has such a
    /// rule. <see langword="null"/> (the default) means no known limit.
    /// </summary>
    /// <remarks>
    /// The store never edits bucket lifecycle rules; this value only tells it when an incomplete upload's parts are
    /// gone. With <see cref="Provider"/> set to <see cref="TusS3Provider.R2"/> and this value unset, the store uses
    /// seven days, the window R2 applies to every bucket. Set it to match an abort rule configured on an AWS or
    /// S3-compatible bucket. Must be positive when set.
    /// </remarks>
    public TimeSpan? MultipartUploadLifetime { get; set; }

    /// <summary>The prefix the store builds keys under, always ending with <c>/</c>.</summary>
    internal string NormalizedPrefix => Prefix.EnsureEndsWith('/');

    /// <summary>
    /// The multipart upload lifetime the store acts on: <see cref="MultipartUploadLifetime"/> when set, otherwise
    /// R2's seven days for <see cref="TusS3Provider.R2"/>, otherwise none.
    /// </summary>
    internal TimeSpan? EffectiveMultipartUploadLifetime =>
        MultipartUploadLifetime ?? (Provider == TusS3Provider.R2 ? R2MultipartUploadLifetime : null);
}

internal sealed class TusS3StoreOptionsValidator : AbstractValidator<TusS3StoreOptions>
{
    public TusS3StoreOptionsValidator()
    {
        RuleFor(x => x.Bucket).NotEmpty();

        // The store owns every key under its prefix and purges them; an empty prefix would claim the whole bucket.
        RuleFor(x => x.Prefix).NotEmpty();

        RuleFor(x => x.Provider).IsInEnum();

        RuleFor(x => x.PartSize)
            .GreaterThanOrEqualTo(TusS3StoreOptions.MinPartSize)
            .WithMessage("PartSize must be at least 5 MiB, the provider's minimum part size");

        RuleFor(x => x.MaxPartSize)
            .GreaterThanOrEqualTo(x => x.PartSize)
            .WithMessage("MaxPartSize must not be below PartSize")
            .LessThanOrEqualTo(TusS3StoreOptions.MaxPartSizeLimit)
            .WithMessage("MaxPartSize must not exceed 1 GiB, because each part is buffered in one array");

        RuleFor(x => x.MaxConcurrentPartUploads).GreaterThanOrEqualTo(1);
        // Four windows of part numbers stay in reserve for crash recovery, so a wider window would eat most of the
        // 10,000-part budget an upload's length is spread over.
        RuleFor(x => x.PartClaimWindow)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(TusS3StoreOptions.MaxPartClaimWindow)
            .WithMessage(
                "PartClaimWindow must be between 1 and 250, because four windows are reserved from 10,000 parts"
            );
        RuleFor(x => x.MaxTailSegments).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PersistTimeout).GreaterThan(TimeSpan.Zero);

        RuleFor(x => x.MultipartUploadLifetime)
            .GreaterThan(TimeSpan.Zero)
            .When(x => x.MultipartUploadLifetime.HasValue);
    }
}

/// <summary>Applies the per-request settings each <see cref="TusS3Provider"/> needs to object and part writes.</summary>
internal static class TusS3RequestPresets
{
    // Parts and tail segments are buffered in memory before they are sent, so aws-chunked streaming buys nothing and is
    // the mode S3-compatible servers most often reject; a plain Content-Length body works everywhere.
    public static void Apply(PutObjectRequest request, TusS3Provider provider)
    {
        request.UseChunkEncoding = false;

        if (_DisablePayloadSigning(provider) is { } disable)
        {
            request.DisablePayloadSigning = disable;
        }
    }

    /// <inheritdoc cref="Apply(PutObjectRequest, TusS3Provider)"/>
    public static void Apply(UploadPartRequest request, TusS3Provider provider)
    {
        request.UseChunkEncoding = false;

        if (_DisablePayloadSigning(provider) is { } disable)
        {
            request.DisablePayloadSigning = disable;
        }
    }

    // R2 accepts unsigned payloads over https and skipping the payload hash saves a pass over every part. A
    // compatible server may be plain http, where the SDK refuses unsigned payloads, so it stays signed. AWS keeps the
    // SDK default.
    private static bool? _DisablePayloadSigning(TusS3Provider provider)
    {
        return provider switch
        {
            TusS3Provider.R2 => true,
            TusS3Provider.S3Compatible => false,
            _ => null,
        };
    }
}
