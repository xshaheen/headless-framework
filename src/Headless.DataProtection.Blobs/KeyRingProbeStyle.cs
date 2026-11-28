// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DataProtection;

/// <summary>
/// Which probe the key-ring health check (<c>AddDataProtectionKeyRing</c>) runs on every health ping. A dedicated
/// enum (mirroring <see cref="BlobContainerProvisioning"/>) so opting into the writing probe is an explicit,
/// self-documenting choice at the registration site rather than an implicit consequence of the wiring.
/// </summary>
[PublicAPI]
public enum KeyRingProbeStyle
{
    /// <summary>
    /// Default: a read-only probe. With an <see cref="Headless.Blobs.IBlobContainerManager"/> wired it checks that the
    /// key container exists; without one it lists the container and requires at least one key. It does NOT verify
    /// write access, so revoked write permission still reports <c>Healthy</c>.
    /// </summary>
    ContainerExistence = 0,

    /// <summary>
    /// A reserved sentinel blob is uploaded and deleted on every ping, through the retry pipeline the key writes use,
    /// verifying the write path the ~90-day key rotation needs. Every probe writes to the shared store, so choose the
    /// probe interval with that in mind.
    /// </summary>
    WriteProbe = 1,
}
