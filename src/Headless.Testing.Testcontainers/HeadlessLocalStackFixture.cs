// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;

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
        return base.Configure().WithImage(TestImages.LocalStack).WithReuse(true);
    }
}
