// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Conformance;

// The shared expiration scenarios, on each backend.

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3ExpirationConformanceTests(TusS3MinioFixture fixture)
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

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3ExpirationConformanceTests(TusS3LocalStackFixture fixture)
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
