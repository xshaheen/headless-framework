// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Hosting;

namespace Headless.Encryption;

/// <summary>
/// Offers the host-scope data keys as a provisioned resource, so a deploy step creates them with
/// <c>resources provision -k EncryptionKeys</c> and a starting host only reads them.
/// </summary>
/// <remarks>
/// Tenant keys are not provisioned here: tenant onboarding creates them through
/// <see cref="IDataKeyManager.EnsureAsync" />, or the first encryption for the tenant does. Purge and delete are
/// refused because they would destroy every host-level value; <see cref="IDataKeyManager.ShredAsync" /> is the only
/// path that deletes keys.
/// </remarks>
internal sealed class EncryptionKeysProvisioner(IDataKeyManager manager, IDataKeyStore store) : IResourceProvisioner
{
    public const string ResourceKind = "EncryptionKeys";

    public string Kind => ResourceKind;

    public string Name => "host";

    public bool CanPurge => false;

    public bool CanDelete => false;

    public Task ProvisionAsync(CancellationToken cancellationToken) =>
        manager.EnsureAsync(FieldContext.HostScope, cancellationToken);

    // One read of the host scope's keys.
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var keys = await store.ListScopeAsync(FieldContext.HostScope, cancellationToken).ConfigureAwait(false);
        var missing = Enum.GetValues<DataKeyKind>().Where(kind => keys.All(k => k.Kind != kind)).ToList();

        if (missing.Count > 0)
        {
            throw new ResourceProvisioningException(
                $"The host-scope encryption keys are missing ({string.Join(", ", missing)}). Run "
                    + $"'resources provision -k {ResourceKind}' to create them."
            );
        }
    }

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Encryption keys are never purged; shred a scope through IDataKeyManager.");

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Encryption keys are never deleted; shred a scope through IDataKeyManager.");

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var keys = await store.ListScopeAsync(FieldContext.HostScope, cancellationToken).ConfigureAwait(false);

        return
        [
            .. Enum.GetValues<DataKeyKind>()
                .Select(kind =>
                {
                    var versions = keys.Where(k => k.Kind == kind).Select(static k => k.Version).ToList();

                    return new KeyValuePair<string, string>(
                        kind.ToString().ToLowerInvariant(),
                        versions.Count == 0
                            ? "missing"
                            : string.Create(CultureInfo.InvariantCulture, $"version {versions.Max()}")
                    );
                }),
        ];
    }

    // The keys live in a table the schema runner creates.
    public IEnumerable<IResourceProvisioner> DependsOn(IReadOnlyList<IResourceProvisioner> provisioners) =>
        provisioners.Where(static p => string.Equals(p.Kind, "Schema", StringComparison.Ordinal));
}
