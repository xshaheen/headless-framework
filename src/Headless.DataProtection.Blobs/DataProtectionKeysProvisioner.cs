// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.DataProtection;

/// <summary>
/// Offers the blob-backed key ring as a provisioned resource, so a deploy step creates its container and first key
/// with <c>resources provision -k DataProtectionKeys</c> and a starting host only reads them.
/// </summary>
/// <remarks>
/// <para>
/// The first key is a deploy step, not the side effect of the first protect on whichever replica serves first: one
/// writer creates it before any replica starts, so every replica loads the same ring and none needs write access
/// at start. Later rotations stay with Data Protection's own key generation.
/// </para>
/// <para>
/// Purge and delete are refused because they would make every protected payload (cookies, antiforgery and signed-URL
/// tokens) unreadable; revoke keys through <see cref="IKeyManager"/> instead.
/// </para>
/// </remarks>
internal sealed class DataProtectionKeysProvisioner(
    BlobStorageDataProtectionXmlRepository repository,
    IKeyManager keyManager,
    KeyManagementOptions keyManagementOptions,
    TimeProvider timeProvider
) : IResourceProvisioner
{
    // Verify and status only ask whether keys exist, so the listing stops early on a large ring.
    private const int _StatusListLimit = BlobStorageDataProtectionXmlRepository.MaxXmlElementCount;

    public string Kind => BlobStorageDataProtectionOptions.KeysResourceKind;

    public string Name => repository.ContainerName;

    public bool CanPurge => false;

    public bool CanDelete => false;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        if (repository.ContainerManager is { } containerManager)
        {
            await containerManager
                .EnsureContainerAsync(repository.ContainerName, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow();

        // Idempotent: a release re-runs provision, and a ring that already holds a usable key keeps it. Expired or
        // revoked keys do not count, because Data Protection would generate a new key on first use for them.
        var hasUsableKey = keyManager.GetAllKeys().Any(key => !key.IsRevoked && key.ExpirationDate > now);

        if (!hasUsableKey)
        {
            keyManager.CreateNewKey(now, now + keyManagementOptions.NewKeyLifetime);
        }
    }

    // One existence read when a manager is wired, then one listing.
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        if (
            repository.ContainerManager is { } containerManager
            && !await containerManager
                .ContainerExistsAsync(repository.ContainerName, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            throw new ResourceProvisioningException(
                $"The data-protection key container '{repository.ContainerName}' does not exist. Run "
                    + $"'resources provision -k {BlobStorageDataProtectionOptions.KeysResourceKind}' to create it and "
                    + "its first key."
            );
        }

        if (await repository.CountKeyBlobsAsync(maxCount: 1, cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new ResourceProvisioningException(
                $"The data-protection key container '{repository.ContainerName}' holds no key. Run "
                    + $"'resources provision -k {BlobStorageDataProtectionOptions.KeysResourceKind}' to create the "
                    + "first key."
            );
        }
    }

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Data-protection keys are never purged: every protected payload would become unreadable."
        );

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Data-protection keys are never deleted: every protected payload would become unreadable."
        );

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (repository.ContainerManager is { } containerManager)
        {
            var exists = await containerManager
                .ContainerExistsAsync(repository.ContainerName, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                return [new("container", "missing"), new("keys", "0")];
            }
        }

        var keys = await repository.CountKeyBlobsAsync(_StatusListLimit, cancellationToken).ConfigureAwait(false);

        return
        [
            new("container", repository.ContainerManager is null ? "pre-provisioned" : "exists"),
            new("keys", keys.ToString(CultureInfo.InvariantCulture)),
        ];
    }
}

/// <summary>
/// Yields the key-ring provisioner when the data-protection key ring is blob-backed, and nothing when another
/// <c>PersistKeysTo*</c> call replaced the repository.
/// </summary>
internal sealed class DataProtectionKeysProvisionerSource(IServiceProvider services) : IResourceProvisionerSource
{
    public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken)
    {
        // Materializing the options runs the PersistKeysToBlobStorage provisioning guardrail, so a wiring that can
        // never create the container fails here, naming the fix, instead of at the first key write.
        var keyManagementOptions = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        if (keyManagementOptions.XmlRepository is not BlobStorageDataProtectionXmlRepository repository)
        {
            return Task.FromResult<IReadOnlyList<IResourceProvisioner>>([]);
        }

        return Task.FromResult<IReadOnlyList<IResourceProvisioner>>([
            new DataProtectionKeysProvisioner(
                repository,
                services.GetRequiredService<IKeyManager>(),
                keyManagementOptions,
                services.GetService<TimeProvider>() ?? TimeProvider.System
            ),
        ]);
    }
}
