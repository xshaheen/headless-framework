// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Conformance;

// The shared termination scenarios, on each backend.

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3TerminationConformanceTests(TusS3MinioFixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task delete_terminates_the_upload() => base.delete_terminates_the_upload();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3TerminationConformanceTests(TusS3LocalStackFixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task delete_terminates_the_upload() => base.delete_terminates_the_upload();
}
