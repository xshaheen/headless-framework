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
    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.LocalStack);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                Container,
                LocalStackBuilder.LocalStackPort,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
