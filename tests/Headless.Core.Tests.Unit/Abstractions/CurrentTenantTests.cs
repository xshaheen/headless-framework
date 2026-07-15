// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Primitives;

namespace Tests.Abstractions;

public sealed class CurrentTenantTests
{
    [Fact]
    public void should_be_false_when_is_available_no_tenant_is_set()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;
        var currentTenant = new CurrentTenant(currentTenantAccessor);

        // when
        var isAvailable = currentTenant.IsAvailable;

        // then
        isAvailable.Should().BeFalse();
    }

    [Fact]
    public void should_be_true_when_is_available_tenant_is_set()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;

        var currentTenant = new CurrentTenant(currentTenantAccessor);

        // when
        using var _ = currentTenant.Change("123", "Test Tenant");

        var isAvailable = currentTenant.IsAvailable;

        // then
        isAvailable.Should().BeTrue();
    }

    [Fact]
    public void should_set_tenant_id_and_name_when_change()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;
        currentTenantAccessor.Current = new TenantInformation("235", "Old Tenant");
        var currentTenant = new CurrentTenant(currentTenantAccessor);

        // when
        using var _ = currentTenant.Change("123", "New Tenant");

        // then
        currentTenant.Id.Should().Be("123");
        currentTenant.Name.Should().Be("New Tenant");
    }

    [Fact]
    public void should_reset_tenant_to_previous_state_when_change_disposed()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;
        var currentTenant = new CurrentTenant(currentTenantAccessor);

        using (currentTenant.Change("123", "Test Tenant"))
        {
            // when
            using (currentTenant.Change("456", "Another Tenant"))
            {
                // then the tenant was changed
                currentTenant.Id.Should().Be("456");
                currentTenant.Name.Should().Be("Another Tenant");
            }

            // then
            currentTenant.Id.Should().Be("123");
            currentTenant.Name.Should().Be("Test Tenant");
        }
    }

    [Fact]
    public void should_allow_null_tenant_id_and_name_when_change()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;
        currentTenantAccessor.Current = new TenantInformation("123", "Test Tenant");
        var currentTenant = new CurrentTenant(currentTenantAccessor);

        // when
        using var _ = currentTenant.Change(null);

        // then
        currentTenant.Id.Should().BeNull();
        currentTenant.Name.Should().BeNull();
    }

    [Fact]
    public void should_support_multiple_nested_scopes_when_change()
    {
        // given
        var currentTenantAccessor = AsyncLocalCurrentTenantAccessor.Instance;
        var currentTenant = new CurrentTenant(currentTenantAccessor);

        using (currentTenant.Change("Tenant1", "First Tenant"))
        {
            // then the first tenant is set
            currentTenant.Id.Should().Be("Tenant1");
            currentTenant.Name.Should().Be("First Tenant");

            // when
            using (currentTenant.Change("Tenant2", "Second Tenant"))
            {
                // then the second tenant is set
                currentTenant.Id.Should().Be("Tenant2");
                currentTenant.Name.Should().Be("Second Tenant");
            }

            // then
            // Ensure it reverts back to the first tenant
            currentTenant.Id.Should().Be("Tenant1");
            currentTenant.Name.Should().Be("First Tenant");
        }
    }
}
