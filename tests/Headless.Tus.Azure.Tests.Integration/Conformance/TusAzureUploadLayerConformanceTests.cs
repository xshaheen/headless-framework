// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests.Conformance;

// The shared upload endpoint scenarios over the Azure store an application registers through UseAzure.

[Collection<TusAzureFixture>]
public sealed class TusAzureUploadLayerConformanceTests(TusAzureFixture fixture)
    : TusUploadLayerConformanceTestsBase(() => new TusAzureUploadLayerBackend(fixture))
{
    [Fact]
    public override Task should_answer_not_found_to_another_owner_and_leave_the_upload_untouched() =>
        base.should_answer_not_found_to_another_owner_and_leave_the_upload_untouched();

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
