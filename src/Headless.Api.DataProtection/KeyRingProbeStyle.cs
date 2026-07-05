// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Which probe the key-ring health check (<c>AddDataProtectionKeyRing</c>) runs on every health ping. A dedicated
/// enum (mirroring <see cref="BlobContainerProvisioning"/>) so opting down to the weaker probe is an explicit,
/// self-documenting choice at the registration site rather than an implicit consequence of the wiring.
/// </summary>
[PublicAPI]
public enum KeyRingProbeStyle
{
    /// <summary>
    /// Default: a reserved sentinel blob is uploaded and deleted through the same ensure + retry pipeline the key
    /// writes use — verifying the full persistence path (exactly what the ~90-day key rotation needs), manager or
    /// not. <c>Healthy</c> therefore always means "the key ring can be persisted".
    /// </summary>
    WriteProbe = 0,

    /// <summary>
    /// Cheap container-existence check via the wired <see cref="Headless.Blobs.IBlobContainerManager"/>. Does NOT
    /// verify write access — revoked write permission still reports <c>Healthy</c> as long as the container exists.
    /// When no manager is wired, the check falls back to <see cref="WriteProbe"/> (the only probe possible) and says
    /// so in its description.
    /// </summary>
    ContainerExistence = 1,
}
