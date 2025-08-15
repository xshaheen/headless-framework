// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Domains;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Primitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests.Contexts;

public sealed class DbContextEntityProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _dbContext;
    private readonly DbContextEntityProcessor _processor;
    private readonly CurrentUser _currentUser;
    private readonly GuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public DbContextEntityProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _currentUser = new CurrentUser();
        _guidGenerator = new GuidGenerator();
        _clock = new Clock();

        _dbContext = new TestDbContext(options, _currentUser, _guidGenerator, _clock);
        _dbContext.Database.EnsureCreated();

        _processor = new DbContextEntityProcessor(_currentUser, _guidGenerator, _clock);
    }

    [Fact]
    public void process_entries_when_adding_entity_with_guid_id_should_set_id()
    {
        // given
        var entity = new TestEntityWithGuid();
        _dbContext.Add(entity);

        // when
        _processor.ProcessEntries(_dbContext);

        // then
        entity.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void process_entries_when_adding_entity_with_create_audit_should_set_create_audit_properties()
    {
        // given
        var entity = new TestEntityWithCreateAudit();
        _dbContext.Add(entity);

        // when
        _processor.ProcessEntries(_dbContext);

        // then
        entity.DateCreated.Should().NotBe(default);
        entity.CreatedById.Should().Be(_currentUser.UserId);
    }

    [Fact]
    public void process_entries_when_modifying_entity_with_update_audit_should_set_update_audit_properties()
    {
        // given
        var entity = new TestEntityWithUpdateAudit();
        _dbContext.Add(entity);
        _dbContext.SaveChanges();

        // when
        entity.Name = "Updated";
        _processor.ProcessEntries(_dbContext);

        // then
        entity.DateUpdated.Should().NotBeNull();
        entity.UpdatedById.Should().Be(_currentUser.UserId);
    }

    [Fact]
    public void process_entries_when_deleting_entity_with_delete_audit_should_set_delete_audit_properties()
    {
        // given
        var entity = new TestEntityWithDeleteAudit();
        _dbContext.Add(entity);
        _dbContext.SaveChanges();

        // when
        _dbContext.Remove(entity);
        _processor.ProcessEntries(_dbContext);

        // then
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().NotBeNull();
        entity.DeletedById.Should().Be(_currentUser.UserId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}

public class TestEntityWithGuid : IEntity<Guid>
{
    public Guid Id { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestEntityWithCreateAudit : IEntity<Guid>, ICreateAudit<UserId>
{
    public Guid Id { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public UserId? CreatedById { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestEntityWithUpdateAudit : IEntity<Guid>, IUpdateAudit<UserId>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "initial";
    public DateTimeOffset? DateUpdated { get; set; }
    public UserId? UpdatedById { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestEntityWithDeleteAudit : IEntity<Guid>, IDeleteAudit<UserId>
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DateDeleted { get; set; }
    public UserId? DeletedById { get; set; }
    public object[] GetKeys() => [Id];
}

public class TestDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public TestDbContext(DbContextOptions options, ICurrentUser currentUser, IGuidGenerator guidGenerator, IClock clock)
        : base(options)
    {
        _currentUser = currentUser;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntityWithGuid>();
        modelBuilder.Entity<TestEntityWithCreateAudit>();
        modelBuilder.Entity<TestEntityWithUpdateAudit>();
        modelBuilder.Entity<TestEntityWithDeleteAudit>();
    }
}

public class CurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public UserId? UserId => new(Guid.Parse("a8a8a8a8-a8a8-a8a8-a8a8-a8a8a8a8a8a8"));
    public AccountId? AccountId => new(Guid.Parse("b8b8b8b8-b8b8-b8b8-b8b8-b8b8b8b8b8b8"));
    public string[] Permissions => [];
    public string[] Roles => [];
}

public class GuidGenerator : IGuidGenerator
{
    public Guid Create() => Guid.NewGuid();
}

public class Clock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public IReadOnlyList<TimeZoneInfo> GetTimeZones() => TimeZoneInfo.GetSystemTimeZones();
}
