// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.TestSetup;

namespace Tests.Probes;

[Collection<TusS3MinioFixture>]
public sealed class MinioS3CapabilityProbeTests(TusS3MinioFixture fixture) : S3CapabilityProbeTestsBase
{
    protected override S3TestClient CreateClient() => fixture.CreateClient();

    [Fact]
    public override Task should_create_only_when_key_is_absent_with_if_none_match() =>
        base.should_create_only_when_key_is_absent_with_if_none_match();

    [Fact]
    public override Task should_replace_only_the_etag_a_writer_read_with_if_match() =>
        base.should_replace_only_the_etag_a_writer_read_with_if_match();

    [Fact]
    public override Task should_reject_an_if_match_write_to_a_missing_key() =>
        base.should_reject_an_if_match_write_to_a_missing_key();

    [Fact]
    public override Task should_complete_a_multipart_upload_from_a_subset_of_its_parts() =>
        base.should_complete_a_multipart_upload_from_a_subset_of_its_parts();

    [Fact]
    public override Task should_copy_a_byte_range_of_an_object_into_a_part() =>
        base.should_copy_a_byte_range_of_an_object_into_a_part();
}
