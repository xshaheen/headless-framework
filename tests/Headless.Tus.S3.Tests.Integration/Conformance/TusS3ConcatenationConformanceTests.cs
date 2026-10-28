// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Conformance;

// The shared concatenation scenarios that build or validate a final upload, on each backend. The scenarios that only
// read an upload's concat role run beside the other conformance suites.

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3FinalUploadConformanceTests(TusS3MinioFixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_throw_when_create_final_file_with_empty_partials() =>
        base.should_throw_when_create_final_file_with_empty_partials();

    [Fact]
    public override Task should_throw_when_create_final_file_with_nonexistent_partial() =>
        base.should_throw_when_create_final_file_with_nonexistent_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_regular_file_as_partial() =>
        base.should_throw_when_create_final_file_with_regular_file_as_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_incomplete_partial() =>
        base.should_throw_when_create_final_file_with_incomplete_partial();

    [Fact]
    public override Task should_get_final_file_info() => base.should_get_final_file_info();

    [Fact]
    public override Task should_preserve_data_integrity_after_concatenation() =>
        base.should_preserve_data_integrity_after_concatenation();

    [Fact]
    public override Task should_create_final_file_from_single_partial() =>
        base.should_create_final_file_from_single_partial();

    [Fact]
    public override Task should_allow_same_partial_multiple_times_in_final_file() =>
        base.should_allow_same_partial_multiple_times_in_final_file();

    [Fact]
    public override Task should_keep_partials_after_concat_by_default() =>
        base.should_keep_partials_after_concat_by_default();

    [Fact]
    public override Task should_preserve_user_metadata_in_final_file() =>
        base.should_preserve_user_metadata_in_final_file();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3FinalUploadConformanceTests(TusS3LocalStackFixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_throw_when_create_final_file_with_empty_partials() =>
        base.should_throw_when_create_final_file_with_empty_partials();

    [Fact]
    public override Task should_throw_when_create_final_file_with_nonexistent_partial() =>
        base.should_throw_when_create_final_file_with_nonexistent_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_regular_file_as_partial() =>
        base.should_throw_when_create_final_file_with_regular_file_as_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_incomplete_partial() =>
        base.should_throw_when_create_final_file_with_incomplete_partial();

    [Fact]
    public override Task should_get_final_file_info() => base.should_get_final_file_info();

    [Fact]
    public override Task should_preserve_data_integrity_after_concatenation() =>
        base.should_preserve_data_integrity_after_concatenation();

    [Fact]
    public override Task should_create_final_file_from_single_partial() =>
        base.should_create_final_file_from_single_partial();

    [Fact]
    public override Task should_allow_same_partial_multiple_times_in_final_file() =>
        base.should_allow_same_partial_multiple_times_in_final_file();

    [Fact]
    public override Task should_keep_partials_after_concat_by_default() =>
        base.should_keep_partials_after_concat_by_default();

    [Fact]
    public override Task should_preserve_user_metadata_in_final_file() =>
        base.should_preserve_user_metadata_in_final_file();
}
