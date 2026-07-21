// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the Jobs tenant-propagation conformance suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerTenancyTests(SqlServerJobsCoordinationFixture fixture)
    : JobsTenancyConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task direct_single_enqueue_persists_explicit_tenant()
    {
        return base.direct_single_enqueue_persists_explicit_tenant();
    }

    [Fact]
    public override Task direct_batch_enqueue_persists_explicit_tenants()
    {
        return base.direct_batch_enqueue_persists_explicit_tenants();
    }

    [Fact]
    public override Task coordinated_single_enqueue_persists_explicit_tenant_atomically()
    {
        return base.coordinated_single_enqueue_persists_explicit_tenant_atomically();
    }

    [Fact]
    public override Task coordinated_batch_enqueue_persists_explicit_tenants_atomically()
    {
        return base.coordinated_batch_enqueue_persists_explicit_tenants_atomically();
    }

    [Fact]
    public override Task coordinated_scheduler_enqueue_persists_options_tenant()
    {
        return base.coordinated_scheduler_enqueue_persists_options_tenant();
    }

    [Fact]
    public override Task coordinated_ambient_enqueue_captures_and_persists_tenant()
    {
        return base.coordinated_ambient_enqueue_captures_and_persists_tenant();
    }

    [Fact]
    public override Task pickup_after_lease_timeout_rematerializes_tenant()
    {
        return base.pickup_after_lease_timeout_rematerializes_tenant();
    }

    [Fact]
    public override Task chain_descendants_persist_resolved_tenants()
    {
        return base.chain_descendants_persist_resolved_tenants();
    }

    [Fact]
    public override Task coordinated_rollback_with_tenant_leaves_no_row()
    {
        return base.coordinated_rollback_with_tenant_leaves_no_row();
    }

    [Fact]
    public override Task update_preserves_stored_tenant_when_the_payload_omits_it()
    {
        return base.update_preserves_stored_tenant_when_the_payload_omits_it();
    }
}
