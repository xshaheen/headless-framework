// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.ChangeTrackers;

public sealed class EntityFrameworkNavigationModifiedTrackerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _dbContext;
    private readonly EntityFrameworkNavigationModifiedTracker _tracker;

    public EntityFrameworkNavigationModifiedTrackerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TestDbContext(options);
        _dbContext.Database.EnsureCreated();
        _tracker = new EntityFrameworkNavigationModifiedTracker();
        _dbContext.ChangeTracker.Tracked += _tracker.ChangeTrackerTracked;
        _dbContext.ChangeTracker.StateChanged += _tracker.ChangeTrackerStateChanged;
    }

    [Fact]
    public void is_navigation_entry_modified_when_navigation_added_should_return_true()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        _dbContext.Users.Add(user);
        _dbContext.Roles.Add(role);
        _dbContext.SaveChanges();

        // when
        user.Roles.Add(role);
        _dbContext.SaveChanges();

        // then
        _tracker.IsNavigationEntryModified(_dbContext.Entry(user)).Should().BeTrue();
    }

    [Fact]
    public void is_entity_entry_modified_when_navigation_added_should_return_true()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        _dbContext.Users.Add(user);
        _dbContext.Roles.Add(role);
        _dbContext.SaveChanges();

        // when
        user.Roles.Add(role);
        _dbContext.SaveChanges();

        // then
        _tracker.IsEntityEntryModified(_dbContext.Entry(user)).Should().BeTrue();
    }

    [Fact]
    public void get_modified_entity_entries_when_navigation_added_should_contain_entry()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        _dbContext.Users.Add(user);
        _dbContext.Roles.Add(role);
        _dbContext.SaveChanges();

        // when
        user.Roles.Add(role);
        _dbContext.SaveChanges();

        // then
        var modifiedEntries = _tracker.GetModifiedEntityEntries();
        modifiedEntries.Should().Contain(e => e.Entity == user);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void is_navigation_entry_modified_when_navigation_removed_should_return_true()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        user.Roles.Add(role);
        _dbContext.Users.Add(user);
        _dbContext.Roles.Add(role);
        _dbContext.SaveChanges();

        // when
        user.Roles.Remove(role);
        _dbContext.SaveChanges();

        // then
        _tracker.IsNavigationEntryModified(_dbContext.Entry(user)).Should().BeTrue();
    }

    [Fact]
    public void clear_when_called_should_empty_trackers()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        _dbContext.Users.Add(user);
        _dbContext.Roles.Add(role);
        _dbContext.SaveChanges();
        user.Roles.Add(role);
        _dbContext.SaveChanges();

        // when
        _tracker.Clear();

        // then
        _tracker.GetModifiedEntityEntries().Should().BeEmpty();
    }
}

public class User : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();

    public object[] GetKeys() => [Id];
}

public class Role : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public ICollection<User> Users { get; set; } = new List<User>();
    public object[] GetKeys() => [Id];
}

public class Post : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public object[] GetKeys() => [Id];
}

public class TestDbContext : DbContextBase
{
    public TestDbContext(DbContextOptions options)
        : base(new CurrentUser(), new CurrentTenant(), new GuidGenerator(), new Clock(), options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<Post> Posts { get; set; }

    public override string DefaultSchema => "test";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasMany(u => u.Roles).WithMany(r => r.Users);
    }

    protected override Task PublishMessagesAsync(List<EmitterDistributedMessages> emitters, IDbContextTransaction currentTransaction, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override void PublishMessages(List<EmitterDistributedMessages> emitters, IDbContextTransaction currentTransaction)
    {
    }

    protected override Task PublishMessagesAsync(List<EmitterLocalMessages> emitters, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override void PublishMessages(List<EmitterLocalMessages> emitters)
    {
    }
}

public class CurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public UserId? UserId => new(Guid.NewGuid());
    public AccountId? AccountId => new(Guid.NewGuid());
    public string[] Permissions => [];
    public string[] Roles => [];
}

public class CurrentTenant : ICurrentTenant
{
    public string? Id => Guid.NewGuid().ToString();
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
