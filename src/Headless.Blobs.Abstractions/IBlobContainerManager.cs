// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs;

/// <summary>
/// Optional capability for blob backends that can manage the lifecycle of a top-level container (S3 bucket, Azure
/// container, file-system root directory, SFTP root directory). Container management is kept off the data-plane
/// <see cref="IBlobStorage"/> contract because runtime container/bucket creation is a management concern — and an
/// anti-pattern on some backends — that not every provider supports.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="IPresignedUrlBlobStorage"/> (which both AWS and Cloudflare R2 support, so an
/// <see langword="is"/>-cast from the resolved <see cref="IBlobStorage"/> stays honest), this capability must distinguish
/// providers that <i>share</i> a storage implementation: AWS supports bucket lifecycle, but R2 — whose
/// object-scoped tokens cannot create buckets — reuses the AWS storage type. So this capability is a
/// <b>separately registered service resolved from DI</b>, not a cast from the storage instance. Capable providers
/// register an implementation; providers that cannot manage containers (for example R2) register none.
/// </para>
/// <para>
/// Consumers resolve the capability instead of casting the store:
/// <code>
/// var manager = serviceProvider.GetKeyedService&lt;IBlobContainerManager&gt;("images");
/// if (manager is not null)
/// {
///     await manager.EnsureContainerAsync("images");
/// }
/// </code>
/// </para>
/// <para>
/// <see cref="IBlobStorage.UploadAsync"/> does not create a missing top-level container — callers ensure it through
/// this capability (or provision it out-of-band) first. Filesystem-like providers still create the intermediate
/// path directories required to write a blob, which is path creation, not container management.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBlobContainerManager
{
    /// <summary>Ensures the top-level container exists, creating it if necessary. Idempotent.</summary>
    /// <param name="container">The top-level container (bucket/container/root) to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="container"/> is null, empty, or fails path-security validation.</exception>
    ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Determines whether the top-level container exists.</summary>
    /// <param name="container">The top-level container to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the container exists; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the top-level container and all blobs it holds.
    /// </summary>
    /// <param name="container">The top-level container to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the container existed and was deleted; <see langword="false"/> if it was not found.</returns>
    ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default);
}
