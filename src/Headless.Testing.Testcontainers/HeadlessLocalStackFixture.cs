// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared LocalStack (AWS emulator) container fixture pinned to <see cref="TestImages.LocalStack"/>.
/// Subclass to enable specific AWS services (e.g., S3, SQS, SNS) via environment variables.
/// </summary>
[PublicAPI]
public class HeadlessLocalStackFixture()
    : ContainerFixture<LocalStackBuilder, LocalStackContainer>(TestContextMessageSink.Instance)
{
    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.LocalStack"/> unless overridden. Override it to run
    /// another version or a variant of the same server.
    /// </summary>
    protected virtual string Image => TestImages.LocalStack;

    /// <summary>
    /// Gets the check that proves the container's edge port reaches this container's LocalStack, by its session id.
    /// Start a container that a test stopped through its <see cref="PublishedPortCheck.StartAsync"/>, so a restart that
    /// lands on another host listener's port moves on.
    /// </summary>
    public PublishedPortCheck PortCheck { get; } =
        new(LocalStackBuilder.LocalStackPort, PublishedPortProbes.LocalStackSessionId);

    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithImage(Image);
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
