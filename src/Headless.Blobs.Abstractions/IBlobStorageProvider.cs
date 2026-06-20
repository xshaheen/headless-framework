// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs;

/// <summary>
/// Resolves named <see cref="IBlobStorage"/> instances registered through the setup builder — for example
/// <c>setup.AddNamed("images", i => i.UseCloudflareR2(…))</c>. The default (unnamed) store is not resolved
/// here; inject it directly as <see cref="IBlobStorage"/>.
/// </summary>
[PublicAPI]
public interface IBlobStorageProvider
{
    /// <summary>Gets the blob storage registered under <paramref name="name"/>.</summary>
    /// <param name="name">The blob storage instance name.</param>
    /// <returns>The resolved blob storage.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no store is registered under <paramref name="name"/>.</exception>
    IBlobStorage GetStorage(string name);

    /// <summary>Gets the blob storage registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The blob storage instance name.</param>
    /// <returns>The resolved blob storage, or <see langword="null"/>.</returns>
    IBlobStorage? GetStorageOrNull(string name);
}
