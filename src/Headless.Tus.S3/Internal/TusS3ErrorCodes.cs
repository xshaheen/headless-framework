// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;

namespace Headless.Tus.Internal;

/// <summary>
/// The S3 error codes the store acts on. Matched by code: the SDK throws a typed exception only for
/// AbortMultipartUpload, so every other refusal arrives as a plain <c>AmazonS3Exception</c>.
/// </summary>
internal static class TusS3ErrorCodes
{
    public const string NoSuchUpload = "NoSuchUpload";
    public const string NoSuchKey = "NoSuchKey";
    public const string ConditionalRequestConflict = "ConditionalRequestConflict";
    public const string BucketAlreadyOwnedByYou = "BucketAlreadyOwnedByYou";
    public const string InvalidPart = "InvalidPart";
    public const string InvalidPartOrder = "InvalidPartOrder";
    public const string EntityTooSmall = "EntityTooSmall";

    /// <summary>Whether storage answered with <paramref name="errorCode"/>, compared ordinally.</summary>
    public static bool HasErrorCode(this AmazonServiceException exception, string errorCode)
    {
        return string.Equals(exception.ErrorCode, errorCode, StringComparison.Ordinal);
    }
}
