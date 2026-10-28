// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Conformance;

// The shared tus conformance scenarios the S3 store supports so far, on each backend. Scenarios that append, complete,
// terminate, sweep, concatenate, or read content join as the store gains those operations.

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3StoreConformanceTests(TusS3MinioFixture fixture)
    : TusStoreConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_upload_metadata() => base.should_get_upload_metadata();

    [Fact]
    public override Task should_check_file_existence() => base.should_check_file_existence();

    [Fact]
    public override Task should_get_upload_length() => base.should_get_upload_length();

    [Fact]
    public override Task should_get_expiration() => base.should_get_expiration();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3StoreConformanceTests(TusS3LocalStackFixture fixture)
    : TusStoreConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_upload_metadata() => base.should_get_upload_metadata();

    [Fact]
    public override Task should_check_file_existence() => base.should_check_file_existence();

    [Fact]
    public override Task should_get_upload_length() => base.should_get_upload_length();

    [Fact]
    public override Task should_get_expiration() => base.should_get_expiration();
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3ConcatenationConformanceTests(TusS3MinioFixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_partial_file_info() => base.should_get_partial_file_info();

    [Fact]
    public override Task should_return_null_for_regular_file() => base.should_return_null_for_regular_file();

    [Fact]
    public override Task should_return_null_for_nonexistent_file() => base.should_return_null_for_nonexistent_file();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3ConcatenationConformanceTests(TusS3LocalStackFixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_partial_file_info() => base.should_get_partial_file_info();

    [Fact]
    public override Task should_return_null_for_regular_file() => base.should_return_null_for_regular_file();

    [Fact]
    public override Task should_return_null_for_nonexistent_file() => base.should_return_null_for_nonexistent_file();
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3ProtocolConformanceTests(TusS3MinioFixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset() =>
        base.head_echoes_metadata_verbatim_and_reports_length_and_offset();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3ProtocolConformanceTests(TusS3LocalStackFixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset() =>
        base.head_echoes_metadata_verbatim_and_reports_length_and_offset();
}
