// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Tus;

/// <summary>A finished upload, as an <see cref="ITusUploadCompletedHandler"/> sees it.</summary>
/// <param name="openContent">Opens a new stream over the upload's content on every call.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="openContent"/> is <see langword="null"/>.</exception>
[PublicAPI]
public sealed class TusCompletedUpload(Func<CancellationToken, Task<Stream>> openContent)
{
    private readonly Func<CancellationToken, Task<Stream>> _openContent = Argument.IsNotNull(openContent);

    /// <summary>The upload's file id, which names it in the store.</summary>
    public required string FileId { get; init; }

    /// <summary>The length of the upload's content, in bytes.</summary>
    public required long Length { get; init; }

    /// <summary>
    /// The metadata the client sent when it created the upload, by key, with each value decoded as UTF-8. A key
    /// sent without a value has an empty one. What the server recorded about the upload is not part of it.
    /// </summary>
    /// <remarks>The client chose every key and value: nothing here is verified.</remarks>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    /// <summary>The tenant the upload was created in, or <see langword="null"/> when it was created in none.</summary>
    public required string? TenantId { get; init; }

    /// <summary>
    /// The owner the upload was created for: the key the endpoint's <c>OwnedBy</c> function returned, or the
    /// signed-in user's id when the endpoint has no such function.
    /// </summary>
    public required string OwnerKey { get; init; }

    /// <summary>Opens the upload's content for reading, from its first byte.</summary>
    /// <param name="cancellationToken">Cancels opening the content.</param>
    /// <returns>A stream over the content, which the caller disposes.</returns>
    /// <exception cref="NotSupportedException">The host's upload store cannot return an upload's content.</exception>
    /// <exception cref="InvalidOperationException">The upload is no longer in the store.</exception>
    public Task<Stream> OpenContentAsync(CancellationToken cancellationToken)
    {
        return _openContent(cancellationToken);
    }
}
