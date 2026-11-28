// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;

namespace Tests;

[Collection<MinioFixture>]
public sealed class MinioDataProtectionKeyRingProvisioningTests(MinioFixture fixture)
    : DataProtectionKeyRingProvisioningTestsBase
{
    protected override void UseBlobs(HeadlessBlobsSetupBuilder blobs)
    {
        blobs.UseS3Compatible(
            fixture.ConnectionString,
            MinioFixture.AccessKey,
            MinioFixture.SecretKey,
            options => options.AllowInsecureHttp = true
        );
    }
}
