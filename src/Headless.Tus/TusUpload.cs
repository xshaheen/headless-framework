// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Tus;

/// <summary>A finished upload, as <see cref="ITusUploads"/> gives it to an application.</summary>
/// <param name="openContent">Opens a new stream over the upload's content on every call.</param>
/// <param name="delete">Removes the upload from the store.</param>
/// <exception cref="ArgumentNullException">
/// Thrown when <paramref name="openContent"/> or <paramref name="delete"/> is <see langword="null"/>.
/// </exception>
[PublicAPI]
public sealed class TusUpload(Func<CancellationToken, Task<Stream>> openContent, Func<CancellationToken, Task> delete)
{
    private readonly Func<CancellationToken, Task<Stream>> _openContent = Argument.IsNotNull(openContent);
    private readonly Func<CancellationToken, Task> _delete = Argument.IsNotNull(delete);

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

    /// <summary>
    /// The name of the upload endpoint the upload was created on. The default endpoint's name is empty.
    /// </summary>
    public required string EndpointName { get; init; }

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
    public Task<Stream> OpenContentAsync(CancellationToken cancellationToken = default)
    {
        return _openContent(cancellationToken);
    }

    /// <summary>
    /// Removes the upload from the store. Its client cannot do that once the upload is finished, so an
    /// application that copied the content elsewhere, or no longer needs it, deletes the upload here.
    /// </summary>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes once the store has removed the upload.</returns>
    /// <exception cref="NotSupportedException">The host's upload store cannot delete an upload.</exception>
    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        return _delete(cancellationToken);
    }
}
