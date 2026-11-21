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
public class HeadlessAzuriteFixture : ContainerFixture<AzuriteBuilder, AzuriteContainer>
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Azurite"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.Azurite;

    // An account only this container serves, beside the well-known one clients use: Azurite refuses an unsigned request
    // to an account it knows and reports any other missing, so another Azurite on the same host port shows as foreign.
    private readonly string _identityAccount = $"probe{Guid.NewGuid():N}"[..24];

    /// <summary>Creates the fixture.</summary>
    public HeadlessAzuriteFixture()
        : base(TestContextMessageSink.Instance)
    {
        PortCheck = new(
            [AzuriteBuilder.BlobPort, AzuriteBuilder.QueuePort, AzuriteBuilder.TablePort],
            PublishedPortProbes.AzuriteAccount(_identityAccount, AzuriteBuilder.TablePort)
        );
    }

    /// <summary>
    /// Gets the check that proves the blob, queue, and table ports reach this container. Start a container that a test
    /// stopped through its <see cref="PublishedPortCheck.StartAsync"/>, so a restart that lands on another host
    /// listener's port moves on.
    /// </summary>
    public PublishedPortCheck PortCheck { get; }

    protected override AzuriteBuilder Configure()
    {
        return base.Configure()
            .WithImage(Image)
            .WithEnvironment(
                "AZURITE_ACCOUNTS",
                $"{AzuriteBuilder.AccountName}:{AzuriteBuilder.AccountKey};{_identityAccount}:{AzuriteBuilder.AccountKey}"
            );
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await PortCheck
            .VerifyAsync(Container, cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }
}
