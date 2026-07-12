// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Describes the outcome of an image processing operation.</summary>
/// <remarks>
/// This value is consumer-observable via <c>ImageProcessResult{T}.State</c>. Additional members may be
/// added in future versions; callers that switch on it should handle unrecognized values defensively.
/// </remarks>
[PublicAPI]
public enum ImageProcessState
{
    /// <summary>The image format or MIME type is not supported by any registered contributor.</summary>
    Unsupported = 0,

    /// <summary>The operation completed successfully and a result is available.</summary>
    Done = 1,

    /// <summary>
    /// The operation was attempted but could not produce a usable result. For example, a compressor
    /// returns <c>Failed</c> when the re-encoded output is larger than the original.
    /// </summary>
    Failed = 2,
}
