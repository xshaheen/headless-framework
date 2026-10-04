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
/// providers that share a storage implementation: AWS supports bucket lifecycle, but R2 — whose
/// object-scoped tokens cannot create buckets — reuses the AWS storage type. This capability is a
/// separately registered service resolved from dependency injection, not a cast from the storage instance. Capable providers
/// register an implementation; providers that cannot manage containers (such as R2) register none.
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
/// <see cref="IBlobStorage.UploadAsync"/> does not create a missing top-level container. Callers ensure it through
/// this capability (or provision it out-of-band) first. File-system providers still create the intermediate
/// path directories required to write a blob, which is path creation, not container management.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBlobContainerManager
{
    /// <summary>Ensures the top-level container exists, creating it if necessary. Idempotent.</summary>
    /// <param name="container">The top-level container (bucket/container/root) to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="container"/> is empty or whitespace, or fails path-security validation.</exception>
    ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>Determines whether the top-level container exists.</summary>
    /// <param name="container">The top-level container to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the container exists; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the top-level container and all blobs it holds.
    /// </summary>
    /// <param name="container">The top-level container to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the container existed and was deleted; <see langword="false"/> if it was not found.</returns>
    ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default);
}
