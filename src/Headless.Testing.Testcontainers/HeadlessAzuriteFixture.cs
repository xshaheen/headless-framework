// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared Azurite (Azure Storage emulator) container fixture pinned to <see cref="TestImages.Azurite"/>.
/// Subclass to expose connection helpers or pre-create containers.
/// </summary>
[PublicAPI]
public class HeadlessAzuriteFixture()
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Azurite"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Azurite;

    protected override AzuriteBuilder Configure()
    {
        return base.Configure().WithImage(Image);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                Container,
                AzuriteBuilder.BlobPort,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
