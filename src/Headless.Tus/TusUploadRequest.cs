// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Tus;

/// <summary>The upload a client asks an upload endpoint to create, as an <see cref="ITusUploadValidator"/> sees it.</summary>
[PublicAPI]
public sealed class TusUploadRequest
{
    /// <summary>
    /// The length the client declares for the upload, in bytes, or <see langword="null"/> when the client defers
    /// the length until it has sent the content.
    /// </summary>
    /// <remarks>The endpoint has already refused a declared length above its maximum size.</remarks>
    public required long? Length { get; init; }

    /// <summary>
    /// The metadata the client sent with the request, by key, with each value decoded as UTF-8. A key sent
    /// without a value has an empty one.
    /// </summary>
    /// <remarks>The client chooses every key and value: nothing here is verified.</remarks>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    /// <summary>The tenant the request runs in, or <see langword="null"/> when it runs in none.</summary>
    public required string? TenantId { get; init; }

    /// <summary>
    /// The owner the upload is created for: the key the endpoint's <c>OwnedBy</c> function returned, or the
    /// signed-in user's id when the endpoint has no such function.
    /// </summary>
    public required string OwnerKey { get; init; }

    /// <summary>The creating request.</summary>
    public required HttpContext HttpContext { get; init; }
}
