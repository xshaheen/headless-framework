// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;
using Framework.Orm.EntityFramework.ChangeTrackers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class EntityFrameworkNavigationModifiedTrackerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDb _db;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _sut;

    public EntityFrameworkNavigationModifiedTrackerTests()
    {
        _sut = new HeadlessEntityFrameworkNavigationModifiedTracker();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options;
        _db = new TestDb(options);
        _db.Database.EnsureCreated();
        _db.ChangeTracker.Tracked += _sut.ChangeTrackerTracked;
        _db.ChangeTracker.StateChanged += _sut.ChangeTrackerStateChanged;
    }

    [Fact]
    public void is_entity_entry_modified_when_navigation_added_should_return_true()
    {
        // given
        var user = new User { Name = "Test User" };
        _db.Users.Add(user);
        var role = new Role { Name = "Admin" };
        _db.Roles.Add(role);
        _db.SaveChanges();

        // when
        user.Roles.Add(role); // Adding a navigation property
        _db.SaveChanges();

        // then
        var userEntry = _db.Entry(user);
        _sut.IsEntityEntryModified(userEntry).Should().BeTrue();
    }

    [Fact]
    public void is_navigation_entry_modified_when_navigation_added_should_return_true()
    {
        // given
        var user = new User { Name = "Test User" };
        _db.Users.Add(user);
        var role = new Role { Name = "Admin" };
        _db.Roles.Add(role);
        _db.SaveChanges();

        // when
        user.Roles.Add(role); // Adding a navigation property
        _db.SaveChanges();

        // then
        var userEntry = _db.Entry(user);
        _sut.IsNavigationEntryModified(userEntry).Should().BeTrue();
    }

    [Fact]
    public void get_modified_entity_entries_when_navigation_added_should_contain_entry()
    {
        // given
        var user = new User { Name = "Test User" };
        _db.Users.Add(user);
        var role = new Role { Name = "Admin" };
        _db.Roles.Add(role);
        _db.SaveChanges();

        // when
        user.Roles.Add(role); // Adding a navigation property
        _db.SaveChanges();

        // then
        var modifiedEntries = _sut.GetModifiedEntityEntries();
        modifiedEntries.Should().HaveCount(2);
        modifiedEntries.Should().Contain(e => e.Entity == user);
        modifiedEntries.Should().Contain(e => e.Entity == role);
    }

    [Fact]
    public void is_navigation_entry_modified_when_navigation_removed_should_return_true()
    {
        // given
        var role = new Role { Name = "Admin" };
        var user = new User { Name = "Test User", Roles = { role } };
        _db.Users.Add(user);
        _db.Roles.Add(role);
        _db.SaveChanges();

        // when
        user.Roles.Remove(role);
        _db.SaveChanges();

        // then
        _sut.IsNavigationEntryModified(_db.Entry(user)).Should().BeTrue();
    }

    [Fact]
    public void clear_when_called_should_empty_trackers()
    {
        // given
        var user = new User { Name = "Test User" };
        var role = new Role { Name = "Admin" };
        _db.Users.Add(user);
        _db.Roles.Add(role);
        _db.SaveChanges();
        user.Roles.Add(role);
        _db.SaveChanges();

        // when
        _sut.Clear();

        // then
        _sut.GetModifiedEntityEntries().Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    public sealed class User : IEntity<Guid>
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public List<Role> Roles { get; init; } = [];

        public List<Post> Posts { get; init; } = [];

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class Role : IEntity<Guid>
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public List<User> Users { get; init; } = [];

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class Post : IEntity<Guid>
    {
        public required Guid Id { get; init; }

        public required string Title { get; init; }

        public required Guid UserId { get; init; }

        public User User { get; init; } = null!;

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class TestDb(DbContextOptions options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }

        public DbSet<Role> Roles { get; set; }

        public DbSet<Post> Posts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().HasMany(u => u.Roles).WithMany(r => r.Users);
        }
    }
}
