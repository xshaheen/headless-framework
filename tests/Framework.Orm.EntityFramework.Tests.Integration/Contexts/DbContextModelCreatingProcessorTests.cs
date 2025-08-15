// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Contexts;

public sealed class DbContextModelCreatingProcessorTests
{
    private readonly DbContextModelCreatingProcessor _processor;
    private readonly ModelBuilder _modelBuilder;
    private readonly CurrentTenant _currentTenant;
    private readonly Clock _clock;
    private readonly DbContextGlobalFiltersStatus _filterStatus;

    public DbContextModelCreatingProcessorTests()
    {
        _currentTenant = new CurrentTenant();
        _clock = new Clock();
        _filterStatus = new DbContextGlobalFiltersStatus();
        _processor = new DbContextModelCreatingProcessor(_currentTenant, _clock, _filterStatus);
        _modelBuilder = new ModelBuilder();
    }

    [Fact]
    public void process_model_creating_for_multi_tenant_entity_should_add_tenant_filter()
    {
        // given
        _modelBuilder.Entity<TestMultiTenantEntity>();

        // when
        _processor.ProcessModelCreating(_modelBuilder);

        // then
        var entityType = _modelBuilder.Model.FindEntityType(typeof(TestMultiTenantEntity));
        entityType.Should().NotBeNull();
        var queryFilter = entityType!.GetQueryFilter();
        queryFilter.Should().NotBeNull();
        queryFilter!.ToString().Should().Contain(nameof(IMultiTenant<int>.TenantId));
    }

    [Fact]
    public void process_model_creating_for_delete_audit_entity_should_add_delete_filter()
    {
        // given
        _modelBuilder.Entity<TestDeleteAuditEntity>();

        // when
        _processor.ProcessModelCreating(_modelBuilder);

        // then
        var entityType = _modelBuilder.Model.FindEntityType(typeof(TestDeleteAuditEntity));
        entityType.Should().NotBeNull();
        var queryFilter = entityType!.GetQueryFilter();
        queryFilter.Should().NotBeNull();
        queryFilter!.ToString().Should().Contain(nameof(IDeleteAudit.IsDeleted));
    }

    [Fact]
    public void ProcessModelCreating_ForSuspendAuditEntity_ShouldAddSuspendFilter()
    {
        // Arrange
        _modelBuilder.Entity<TestSuspendAuditEntity>();

        // Act
        _processor.ProcessModelCreating(_modelBuilder);

        // Assert
        var entityType = _modelBuilder.Model.FindEntityType(typeof(TestSuspendAuditEntity));
        Assert.NotNull(entityType);
        var queryFilter = entityType.GetQueryFilter();
        Assert.NotNull(queryFilter);
        Assert.Contains(nameof(ISuspendAudit.IsSuspended), queryFilter.ToString());
    }
}

public class TestMultiTenantEntity : IEntity<Guid>, IMultiTenant<string>
{
    public Guid Id { get; set; }
    public string? TenantId { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestDeleteAuditEntity : IEntity<Guid>, IDeleteAudit
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DateDeleted { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestSuspendAuditEntity : IEntity<Guid>, ISuspendAudit
{
    public Guid Id { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? DateSuspended { get; set; }
    public object[] GetKeys() => [Id];
}

public class CurrentTenant : ICurrentTenant
{
    public string? Id => "test_tenant";
}

public class Clock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public IReadOnlyList<TimeZoneInfo> GetTimeZones() => TimeZoneInfo.GetSystemTimeZones();
}
