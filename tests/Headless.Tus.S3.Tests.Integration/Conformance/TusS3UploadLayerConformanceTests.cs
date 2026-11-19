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
    public override Task should_give_the_application_the_creators_finished_upload_and_nothing_to_another_owner() =>
        base.should_give_the_application_the_creators_finished_upload_and_nothing_to_another_owner();

    [Fact]
    public override Task should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant() =>
        base.should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_refuse_a_clients_delete_of_a_finished_upload_and_leave_nothing_once_the_application_deletes_it() =>
        base.should_refuse_a_clients_delete_of_a_finished_upload_and_leave_nothing_once_the_application_deletes_it();

    [Fact]
    public override Task should_refuse_a_clients_delete_of_an_upload_that_finished_after_the_request_first_read_it() =>
        base.should_refuse_a_clients_delete_of_an_upload_that_finished_after_the_request_first_read_it();

    [Fact]
    public override Task should_find_an_upload_that_finished_after_the_same_request_first_looked_it_up() =>
        base.should_find_an_upload_that_finished_after_the_same_request_first_looked_it_up();

    [Fact]
    public override Task should_keep_an_upload_that_finished_while_a_refused_write_to_it_was_waiting() =>
        base.should_keep_an_upload_that_finished_while_a_refused_write_to_it_was_waiting();

    [Fact]
    public override Task should_keep_the_bytes_another_write_stored_while_a_refused_write_to_it_was_waiting() =>
        base.should_keep_the_bytes_another_write_stored_while_a_refused_write_to_it_was_waiting();

    [Fact]
    public override Task should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant() =>
        base.should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_return_the_clients_metadata_unchanged_and_without_the_servers_entries() =>
        base.should_return_the_clients_metadata_unchanged_and_without_the_servers_entries();

    [Fact]
    public override Task should_keep_metadata_that_fills_the_stores_limit_until_the_upload_finishes() =>
        base.should_keep_metadata_that_fills_the_stores_limit_until_the_upload_finishes();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3UploadLayerConformanceTests(TusS3LocalStackFixture fixture)
    : TusUploadLayerConformanceTestsBase(() => new TusS3UploadLayerBackend(fixture))
{
    [Fact]
    public override Task should_answer_not_found_to_another_owner_and_leave_the_upload_untouched() =>
        base.should_answer_not_found_to_another_owner_and_leave_the_upload_untouched();

    [Fact]
    public override Task should_give_the_application_the_creators_finished_upload_and_nothing_to_another_owner() =>
        base.should_give_the_application_the_creators_finished_upload_and_nothing_to_another_owner();

    [Fact]
    public override Task should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant() =>
        base.should_give_a_request_with_no_tenant_nothing_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_refuse_a_clients_delete_of_a_finished_upload_and_leave_nothing_once_the_application_deletes_it() =>
        base.should_refuse_a_clients_delete_of_a_finished_upload_and_leave_nothing_once_the_application_deletes_it();

    [Fact]
    public override Task should_refuse_a_clients_delete_of_an_upload_that_finished_after_the_request_first_read_it() =>
        base.should_refuse_a_clients_delete_of_an_upload_that_finished_after_the_request_first_read_it();

    [Fact]
    public override Task should_find_an_upload_that_finished_after_the_same_request_first_looked_it_up() =>
        base.should_find_an_upload_that_finished_after_the_same_request_first_looked_it_up();

    [Fact]
    public override Task should_keep_an_upload_that_finished_while_a_refused_write_to_it_was_waiting() =>
        base.should_keep_an_upload_that_finished_while_a_refused_write_to_it_was_waiting();

    [Fact]
    public override Task should_keep_the_bytes_another_write_stored_while_a_refused_write_to_it_was_waiting() =>
        base.should_keep_the_bytes_another_write_stored_while_a_refused_write_to_it_was_waiting();

    [Fact]
    public override Task should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant() =>
        base.should_keep_each_tenants_uploads_from_the_other_when_uploads_are_scoped_by_tenant();

    [Fact]
    public override Task should_return_the_clients_metadata_unchanged_and_without_the_servers_entries() =>
        base.should_return_the_clients_metadata_unchanged_and_without_the_servers_entries();

    [Fact]
    public override Task should_keep_metadata_that_fills_the_stores_limit_until_the_upload_finishes() =>
        base.should_keep_metadata_that_fills_the_stores_limit_until_the_upload_finishes();
}
