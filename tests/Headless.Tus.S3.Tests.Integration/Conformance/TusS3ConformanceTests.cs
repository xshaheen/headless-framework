// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Conformance;

// The shared tus conformance scenarios the S3 store supports so far, on each backend. Scenarios that complete,
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

    [Fact]
    public override Task should_append_data_when_file_exists() => base.should_append_data_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_to_nonexistent_file() =>
        base.should_throw_when_append_data_to_nonexistent_file();

    [Fact]
    public override Task should_append_data_using_pipe_when_file_exists() =>
        base.should_append_data_using_pipe_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_using_pipe_to_nonexistent_file() =>
        base.should_throw_when_append_data_using_pipe_to_nonexistent_file();

    [Fact]
    public override Task should_persist_received_bytes_when_client_disconnects_mid_stream() =>
        base.should_persist_received_bytes_when_client_disconnects_mid_stream();

    [Fact]
    public override Task should_persist_received_bytes_when_pipe_read_is_cancelled() =>
        base.should_persist_received_bytes_when_pipe_read_is_cancelled();
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

    [Fact]
    public override Task should_append_data_when_file_exists() => base.should_append_data_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_to_nonexistent_file() =>
        base.should_throw_when_append_data_to_nonexistent_file();

    [Fact]
    public override Task should_append_data_using_pipe_when_file_exists() =>
        base.should_append_data_using_pipe_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_using_pipe_to_nonexistent_file() =>
        base.should_throw_when_append_data_using_pipe_to_nonexistent_file();

    [Fact]
    public override Task should_persist_received_bytes_when_client_disconnects_mid_stream() =>
        base.should_persist_received_bytes_when_client_disconnects_mid_stream();

    [Fact]
    public override Task should_persist_received_bytes_when_pipe_read_is_cancelled() =>
        base.should_persist_received_bytes_when_pipe_read_is_cancelled();
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

    [Fact]
    public override Task patch_advances_offset_and_reports_expiration() =>
        base.patch_advances_offset_and_reports_expiration();

    [Fact]
    public override Task patch_with_wrong_offset_conflicts_without_modifying_the_upload() =>
        base.patch_with_wrong_offset_conflicts_without_modifying_the_upload();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3ProtocolConformanceTests(TusS3LocalStackFixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset() =>
        base.head_echoes_metadata_verbatim_and_reports_length_and_offset();

    [Fact]
    public override Task patch_advances_offset_and_reports_expiration() =>
        base.patch_advances_offset_and_reports_expiration();

    [Fact]
    public override Task patch_with_wrong_offset_conflicts_without_modifying_the_upload() =>
        base.patch_with_wrong_offset_conflicts_without_modifying_the_upload();
}
