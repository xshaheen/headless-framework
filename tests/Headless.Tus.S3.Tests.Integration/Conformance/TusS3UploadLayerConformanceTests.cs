// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests.Conformance;

// The shared upload endpoint scenarios over the S3 store an application registers through UseS3, on each backend.

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3UploadLayerConformanceTests(TusS3MinioFixture fixture)
    : TusUploadLayerConformanceTestsBase(() => new TusS3UploadLayerBackend(fixture))
{
    [Fact]
    public override Task should_answer_not_found_to_another_owner_and_leave_the_upload_untouched() =>
        base.should_answer_not_found_to_another_owner_and_leave_the_upload_untouched();

    [Fact]
    public override Task should_run_a_failed_handler_again_on_the_next_status_request_and_not_after_it_returned() =>
        base.should_run_a_failed_handler_again_on_the_next_status_request_and_not_after_it_returned();

    [Fact]
    public override Task should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant() =>
        base.should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_return_the_clients_metadata_unchanged_and_without_the_servers_entries() =>
        base.should_return_the_clients_metadata_unchanged_and_without_the_servers_entries();

    [Fact]
    public override Task should_keep_metadata_that_fills_the_stores_limit_through_the_handoff() =>
        base.should_keep_metadata_that_fills_the_stores_limit_through_the_handoff();

    [Fact]
    public override Task should_leave_nothing_of_a_scoped_upload_the_handler_asked_to_delete() =>
        base.should_leave_nothing_of_a_scoped_upload_the_handler_asked_to_delete();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3UploadLayerConformanceTests(TusS3LocalStackFixture fixture)
    : TusUploadLayerConformanceTestsBase(() => new TusS3UploadLayerBackend(fixture))
{
    [Fact]
    public override Task should_answer_not_found_to_another_owner_and_leave_the_upload_untouched() =>
        base.should_answer_not_found_to_another_owner_and_leave_the_upload_untouched();

    [Fact]
    public override Task should_run_a_failed_handler_again_on_the_next_status_request_and_not_after_it_returned() =>
        base.should_run_a_failed_handler_again_on_the_next_status_request_and_not_after_it_returned();

    [Fact]
    public override Task should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant() =>
        base.should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_return_the_clients_metadata_unchanged_and_without_the_servers_entries() =>
        base.should_return_the_clients_metadata_unchanged_and_without_the_servers_entries();

    [Fact]
    public override Task should_keep_metadata_that_fills_the_stores_limit_through_the_handoff() =>
        base.should_keep_metadata_that_fills_the_stores_limit_through_the_handoff();

    [Fact]
    public override Task should_leave_nothing_of_a_scoped_upload_the_handler_asked_to_delete() =>
        base.should_leave_nothing_of_a_scoped_upload_the_handler_asked_to_delete();
}
