// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>The <see cref="PresignedUploadConstraints"/> members a presigning backend enforces.</summary>
[PublicAPI]
[Flags]
public enum PresignedUploadConstraintKinds
{
    /// <summary>The backend enforces no upload constraint.</summary>
    None = 0,

    /// <summary>The backend rejects an upload whose <c>Content-Type</c> differs from <see cref="PresignedUploadConstraints.ContentType"/>.</summary>
    ContentType = 1,

    /// <summary>The backend rejects an upload larger than <see cref="PresignedUploadConstraints.MaxLength"/>.</summary>
    MaxLength = 2,
}
