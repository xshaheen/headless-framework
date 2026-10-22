// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared Azurite (Azure Storage emulator) container fixture pinned to <see cref="TestImages.Azurite"/>.
/// Subclass to expose connection helpers or pre-create containers.
/// </summary>
[PublicAPI]
public class HeadlessAzuriteFixture()
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(TestContextMessageSink.Instance)
{
    protected override AzuriteBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.Azurite);
    }
}
