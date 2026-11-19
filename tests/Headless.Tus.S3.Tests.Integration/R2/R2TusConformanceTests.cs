// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.R2;

// The shared tus conformance scenarios against a real Cloudflare R2 account, the same set the MinIO and LocalStack
// classes run. TusS3R2Fixture skips each one without R2 credentials; R2CapabilityProbeTests holds the credential-free
// fact that keeps the module from reporting zero tests.

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3StoreConformanceTests(TusS3R2Fixture fixture)
    : TusStoreConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_upload_metadata() => base.should_get_upload_metadata();

    [Fact]
    public override Task should_get_file() => base.should_get_file();

    [Fact]
    public override Task should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys() =>
        base.should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys();

    [Fact]
    public override Task should_upload_data_before_upload_length_is_known() =>
        base.should_upload_data_before_upload_length_is_known();

    [Fact]
    public override Task should_refuse_a_different_upload_length_once_one_is_set() =>
        base.should_refuse_a_different_upload_length_once_one_is_set();

    [Fact]
    public override Task should_reject_upload_length_below_uploaded_bytes() =>
        base.should_reject_upload_length_below_uploaded_bytes();

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

    [Fact]
    public override Task should_get_expired_files() => base.should_get_expired_files();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ProtocolConformanceTests(TusS3R2Fixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset() =>
        base.head_echoes_metadata_verbatim_and_reports_length_and_offset();

    [Fact]
    public override Task defer_length_upload_reports_defer_header_until_length_is_declared() =>
        base.defer_length_upload_reports_defer_header_until_length_is_declared();

    [Fact]
    public override Task patch_declaring_length_below_offset_is_rejected_and_keeps_length_deferred() =>
        base.patch_declaring_length_below_offset_is_rejected_and_keeps_length_deferred();

    [Fact]
    public override Task patch_advances_offset_and_reports_expiration() =>
        base.patch_advances_offset_and_reports_expiration();

    [Fact]
    public override Task patch_with_wrong_offset_conflicts_without_modifying_the_upload() =>
        base.patch_with_wrong_offset_conflicts_without_modifying_the_upload();

    [Fact]
    public override Task delete_terminates_the_upload() => base.delete_terminates_the_upload();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3UploadFlowConformanceTests(TusS3R2Fixture fixture)
    : TusUploadFlowConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_complete_small_file_upload() => base.should_complete_small_file_upload();

    [Fact]
    public override Task should_resume_interrupted_upload() => base.should_resume_interrupted_upload();

    [Fact]
    public override Task should_handle_concurrent_uploads() => base.should_handle_concurrent_uploads();

    [Fact]
    public override Task should_handle_large_file_chunking() => base.should_handle_large_file_chunking();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ExpirationConformanceTests(TusS3R2Fixture fixture)
    : TusExpirationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_cleanup_expired_uploads() => base.should_cleanup_expired_uploads();

    [Fact]
    public override Task should_not_delete_expired_completed_uploads() =>
        base.should_not_delete_expired_completed_uploads();

    [Fact]
    public override Task should_cleanup_expired_defer_length_uploads() =>
        base.should_cleanup_expired_defer_length_uploads();

    [Fact]
    public override Task should_not_delete_active_uploads() => base.should_not_delete_active_uploads();

    [Fact]
    public override Task should_cleanup_expired_incomplete_uploads() =>
        base.should_cleanup_expired_incomplete_uploads();

    [Fact]
    public override Task should_report_no_expiration_for_completed_uploads() =>
        base.should_report_no_expiration_for_completed_uploads();

    [Fact]
    public override Task should_set_expiration_even_when_request_token_is_cancelled() =>
        base.should_set_expiration_even_when_request_token_is_cancelled();

    [Fact]
    public override Task should_list_only_expired_files() => base.should_list_only_expired_files();

    [Fact]
    public override Task should_return_partial_results_instead_of_throwing_when_cancelled() =>
        base.should_return_partial_results_instead_of_throwing_when_cancelled();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ConcatenationConformanceTests(TusS3R2Fixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_partial_file_info() => base.should_get_partial_file_info();

    [Fact]
    public override Task should_return_null_for_regular_file() => base.should_return_null_for_regular_file();

    [Fact]
    public override Task should_return_null_for_nonexistent_file() => base.should_return_null_for_nonexistent_file();

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
