// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Encryption;

/// <summary>
/// Unwraps the host-scope data key once after the host starts, so readiness stays <c>Unhealthy</c> until the
/// key-encryption key is reachable, and the first request finds the key cached. The start itself never waits on the
/// key service.
/// </summary>
internal sealed class KeyEncryptionKeyWarmup(DataKeyRing ring) : IWarmup
{
    public string Name => "KeyEncryptionKey";

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        using var key =
            await ring.TryGetActiveAsync(FieldContext.HostScope, DataKeyKind.Data, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The host-scope data key is missing. Run 'resources provision -k {EncryptionKeysProvisioner.ResourceKind}' to create it."
            );
    }
}
