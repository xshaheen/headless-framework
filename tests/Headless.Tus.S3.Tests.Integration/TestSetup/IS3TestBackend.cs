// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.TestSetup;

/// <summary>An S3 endpoint the provider-level suites (capability probes, bucket provisioning) run against.</summary>
public interface IS3TestBackend
{
    /// <summary>Creates a client with its own request gate. The caller owns and disposes it.</summary>
    S3TestClient CreateClient();
}
