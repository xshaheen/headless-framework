// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus;
using Tests.TestSetup;

namespace Tests.Flows;

[Collection<TusAzureFixture>]
public sealed class ExpirationCleanupTests(TusAzureFixture fixture)
    : TusExpirationConformanceTestsBase<TusAzureStore>(fixture)
{
    [Fact]
    public override Task should_cleanup_expired_uploads()
    {
        return base.should_cleanup_expired_uploads();
    }

    [Fact]
    public override Task should_not_delete_expired_completed_uploads()
    {
        return base.should_not_delete_expired_completed_uploads();
    }

    [Fact]
    public override Task should_cleanup_expired_defer_length_uploads()
    {
        return base.should_cleanup_expired_defer_length_uploads();
    }

    [Fact]
    public override Task should_not_delete_active_uploads()
    {
        return base.should_not_delete_active_uploads();
    }

    [Fact]
    public override Task should_cleanup_expired_incomplete_uploads()
    {
        return base.should_cleanup_expired_incomplete_uploads();
    }

    [Fact]
    public override Task should_report_no_expiration_for_completed_uploads()
    {
        return base.should_report_no_expiration_for_completed_uploads();
    }

    [Fact]
    public override Task should_set_expiration_even_when_request_token_is_cancelled()
    {
        return base.should_set_expiration_even_when_request_token_is_cancelled();
    }

    [Fact]
    public override Task should_list_only_expired_files()
    {
        return base.should_list_only_expired_files();
    }

    [Fact]
    public override Task should_return_partial_results_instead_of_throwing_when_cancelled()
    {
        return base.should_return_partial_results_instead_of_throwing_when_cancelled();
    }
}
