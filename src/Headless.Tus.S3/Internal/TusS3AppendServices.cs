// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Amazon.S3;
using Microsoft.Extensions.Logging;

namespace Headless.Tus.Internal;

/// <summary>What every append of one store shares: the client, the key layout, the state store, and the part pool.</summary>
/// <param name="CreateMultipartUploadAsync">
/// Starts the multipart upload of a file id, given the upload's raw tus metadata, and returns its upload id. Owned by
/// the store, so the headers the finished object carries are decided in one place.
/// </param>
internal sealed record TusS3AppendServices(
    IAmazonS3 S3,
    TusS3StoreOptions Options,
    TusS3Keys Keys,
    TusS3StateStore StateStore,
    ArrayPool<byte> PartPool,
    ILogger Logger,
    TimeProvider TimeProvider,
    Func<string, string?, CancellationToken, Task<string>> CreateMultipartUploadAsync
);
