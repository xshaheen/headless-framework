// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerEncryptionFixture>]
public sealed class SqlServerEncryptionConformanceTests(SqlServerEncryptionFixture fixture)
    : EncryptionConformanceTests<SqlServerEncryptionFixture>(fixture)
{
    [Fact]
    public override Task should_provision_the_host_keys_and_round_trip_a_tenant_value()
    {
        return base.should_provision_the_host_keys_and_round_trip_a_tenant_value();
    }

    [Fact]
    public override Task should_create_one_data_key_when_two_hosts_race_on_a_new_tenant()
    {
        return base.should_create_one_data_key_when_two_hosts_race_on_a_new_tenant();
    }

    [Fact]
    public override Task should_decrypt_both_versions_and_flag_the_old_one_after_rotation()
    {
        return base.should_decrypt_both_versions_and_flag_the_old_one_after_rotation();
    }

    [Fact]
    public override Task should_stop_decrypting_on_every_host_within_the_cache_duration_after_a_shred()
    {
        return base.should_stop_decrypting_on_every_host_within_the_cache_duration_after_a_shred();
    }

    [Fact]
    public override Task should_rewrap_every_key_so_old_values_decrypt_under_the_new_key_encryption_key()
    {
        return base.should_rewrap_every_key_so_old_values_decrypt_under_the_new_key_encryption_key();
    }

    [Fact]
    public override Task should_refuse_an_insert_whose_version_is_taken()
    {
        return base.should_refuse_an_insert_whose_version_is_taken();
    }

    [Fact]
    public override Task should_keep_scopes_that_differ_only_by_case_apart()
    {
        return base.should_keep_scopes_that_differ_only_by_case_apart();
    }

    [Fact]
    public override Task should_page_every_key_in_id_order()
    {
        return base.should_page_every_key_in_id_order();
    }

    [Fact]
    public override Task should_replace_the_wrapped_form_of_one_key()
    {
        return base.should_replace_the_wrapped_form_of_one_key();
    }

    [Fact]
    public override Task should_fail_the_start_naming_the_deploy_step_when_the_key_table_is_missing()
    {
        return base.should_fail_the_start_naming_the_deploy_step_when_the_key_table_is_missing();
    }

    [Fact]
    public override Task should_report_an_unreachable_database_from_the_store()
    {
        return base.should_report_an_unreachable_database_from_the_store();
    }
}
