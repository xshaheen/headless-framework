// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class DerivedSoftDeleteModelTests : TestBase
{
    private const string _EventName = "HeadlessDerivedDeleteAuditNotFiltered";

    // EF Core builds a context type's model once per process, so each test owns its context type: the warning fires
    // only for the test whose context builds the model first.
    [Fact]
    public async Task should_warn_once_when_only_a_derived_type_implements_delete_audit()
    {
        // given
        await using var connection = await _OpenAsync();
        var logs = new CapturingLoggerProvider();
        await using var provider = _Provider<WarningDbContext>(connection, logs);
        await using var scope = provider.CreateAsyncScope();

        // when
        var model = scope.ServiceProvider.GetRequiredService<WarningDbContext>().Model;

        // then
        var warning = logs.Entries.Should().ContainSingle(x => x.EventName == _EventName).Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(typeof(SoftDog).FullName!).And.Contain(typeof(PlainAnimal).FullName!);
        model.FindEntityType(typeof(PlainAnimal))!.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public async Task should_still_return_soft_deleted_derived_rows_when_the_root_is_not_soft_deletable()
    {
        // given
        await using var connection = await _OpenAsync();
        await using var provider = _Provider<PlainRootDbContext>(connection, new CapturingLoggerProvider());
        await using (var writeScope = provider.CreateAsyncScope())
        {
            var writer = writeScope.ServiceProvider.GetRequiredService<PlainRootDbContext>();
            await writer.Database.EnsureCreatedAsync(AbortToken);
            writer.Add(new SoftDog { Id = Guid.NewGuid() });
            writer.Add(new SoftDog { Id = Guid.NewGuid(), IsDeleted = true });
            await writer.SaveChangesAsync(AbortToken);
        }

        // when
        await using var readScope = provider.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<PlainRootDbContext>();
        var unfiltered = await reader.Set<SoftDog>().CountAsync(AbortToken);
        var bypassed = await reader.Set<SoftDog>().IgnoreNotDeletedFilter().CountAsync(AbortToken);
        var filteredByHand = await reader.Set<SoftDog>().Where(x => !x.IsDeleted).CountAsync(AbortToken);

        // then
        unfiltered.Should().Be(2);
        bypassed.Should().Be(2);
        filteredByHand.Should().Be(1);
    }

    [Fact]
    public async Task should_not_warn_and_should_filter_when_the_root_implements_delete_audit()
    {
        // given
        await using var connection = await _OpenAsync();
        var logs = new CapturingLoggerProvider();
        await using var provider = _Provider<SoftRootDbContext>(connection, logs);
        await using var scope = provider.CreateAsyncScope();

        // when
        var model = scope.ServiceProvider.GetRequiredService<SoftRootDbContext>().Model;

        // then
        logs.Entries.Should().NotContain(x => x.EventName == _EventName);
        model
            .FindEntityType(typeof(SoftAnimal))!
            .FindDeclaredQueryFilter(HeadlessQueryFilters.NotDeletedFilter)
            .Should()
            .NotBeNull();
    }

    private static async Task<SqliteConnection> _OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private static ServiceProvider _Provider<TContext>(SqliteConnection connection, ILoggerProvider logs)
        where TContext : HeadlessDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddHeadlessDbContext<TContext>(options => options.UseSqlite(connection));

        return services.BuildServiceProvider();
    }

    public class PlainAnimal : Entity<Guid>;

    public sealed class PlainCat : PlainAnimal;

    public class SoftDog : PlainAnimal, IDeleteAudit
    {
        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        public DateTimeOffset? RestoredAt { get; set; }
    }

    // Below the topmost soft-deletable type: shares its root and remedy, so it must not warn a second time.
    public sealed class SoftPuppy : SoftDog;

    public class SoftAnimal : Entity<Guid>, IDeleteAudit
    {
        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        public DateTimeOffset? RestoredAt { get; set; }
    }

    public sealed class SoftBird : SoftAnimal;

    private sealed class PlainRootDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PlainAnimal>();
            modelBuilder.Entity<PlainCat>();
            modelBuilder.Entity<SoftDog>();
            modelBuilder.Entity<SoftPuppy>();
        }
    }

    private sealed class WarningDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PlainAnimal>();
            modelBuilder.Entity<PlainCat>();
            modelBuilder.Entity<SoftDog>();
            modelBuilder.Entity<SoftPuppy>();
        }
    }

    private sealed class SoftRootDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SoftAnimal>();
            modelBuilder.Entity<SoftBird>();
        }
    }

    private sealed record LogEntry(LogLevel Level, string? EventName, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue(new LogEntry(logLevel, eventId.Name, formatter(state, exception)));
            }
        }
    }
}
