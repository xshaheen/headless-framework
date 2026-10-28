// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests.Provisioning;

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3BucketProvisionerTests(TusS3MinioFixture fixture)
    : TusS3BucketProvisionerTestsBase(fixture)
{
    [Fact]
    public override Task should_create_missing_bucket_when_provisioning() =>
        base.should_create_missing_bucket_when_provisioning();

    [Fact]
    public override Task should_fail_verification_when_bucket_is_missing() =>
        base.should_fail_verification_when_bucket_is_missing();

    [Fact]
    public override Task should_verify_existing_bucket_with_one_request() =>
        base.should_verify_existing_bucket_with_one_request();

    [Fact]
    public override Task should_report_bucket_state_in_status() => base.should_report_bucket_state_in_status();

    [Fact]
    public override Task should_purge_store_objects_and_keep_foreign_keys() =>
        base.should_purge_store_objects_and_keep_foreign_keys();

    [Fact]
    public override Task should_purge_nothing_when_bucket_is_missing() =>
        base.should_purge_nothing_when_bucket_is_missing();

    [Fact]
    public override Task should_delete_bucket_when_it_holds_only_store_objects() =>
        base.should_delete_bucket_when_it_holds_only_store_objects();

    [Fact]
    public override Task should_keep_bucket_holding_foreign_keys_when_deleting() =>
        base.should_keep_bucket_holding_foreign_keys_when_deleting();

    [Fact]
    public override Task should_create_bucket_through_registered_provisioner() =>
        base.should_create_bucket_through_registered_provisioner();

    [Fact]
    public override Task should_make_no_storage_request_when_constructing_the_store() =>
        base.should_make_no_storage_request_when_constructing_the_store();
}
