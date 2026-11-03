// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A domain-event handler that injects the context type being saved receives the instance being saved, whichever
/// way that instance was created, so <c>db.UnitOfWork()</c> in the handler reaches the save's unit instead of an
/// unrelated context's (or none).
/// </summary>
public sealed class HeadlessDbContextHandlerResolutionTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_hand_the_handler_the_pooled_factory_context_being_saved(bool sync)
    {
        // given — a pooled context created outside any scope: its save runs in a private scope the context opens
        await using var host = await _CreateHostAsync(
            nameof(should_hand_the_handler_the_pooled_factory_context_being_saved) + sync
        );
        var factory = host.Provider.GetRequiredService<IDbContextFactory<HandlerDbContext>>();
        await using var db = await factory.CreateDbContextAsync(AbortToken);
        db.Rows.Add(_RowWithEvent("factory"));

        // when
        await _SaveAsync(db, sync, AbortToken);

        // then — the handler's injected context is the one being saved, so it reads the save's unit
        host.Evidence.HandlerContext.Should().BeSameAs(db);
        host.Evidence.HandlerUnit.Should()
            .NotBeNull("the handler reads the unit the pipeline bound to the saving context");
        host.Evidence.HandlerUnitTransactionMatched.Should().BeTrue();
        (await _CountAsync(factory)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_hand_the_handler_the_pooled_scope_context_being_saved(bool sync)
    {
        // given — the scope-resolved path, unchanged: the scope's context is the one being saved
        await using var host = await _CreateHostAsync(
            nameof(should_hand_the_handler_the_pooled_scope_context_being_saved) + sync
        );
        await using var scope = host.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HandlerDbContext>();
        db.Rows.Add(_RowWithEvent("scope"));

        // when
        await _SaveAsync(db, sync, AbortToken);

        // then
        host.Evidence.HandlerContext.Should().BeSameAs(db);
        host.Evidence.HandlerUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_a_pooled_context_to_the_pool_once_after_its_handler_resolved_it()
    {
        // given — the handler resolved the saving instance in the context's own private scope, so disposing that
        // scope disposes the instance a second time; a second return would let the pool lease it twice.
        await using var host = await _CreateHostAsync(
            nameof(should_return_a_pooled_context_to_the_pool_once_after_its_handler_resolved_it),
            poolSize: 1
        );
        var factory = host.Provider.GetRequiredService<IDbContextFactory<HandlerDbContext>>();

        HandlerDbContext first;
        await using (first = await factory.CreateDbContextAsync(AbortToken))
        {
            first.Rows.Add(_RowWithEvent("first"));
            await first.SaveChangesAsync(AbortToken);
            host.Evidence.HandlerContext.Should().BeSameAs(first);
        }

        // when
        await using var second = await factory.CreateDbContextAsync(AbortToken);
        await using var third = await factory.CreateDbContextAsync(AbortToken);

        // then — the pool hands the returned instance back once, then builds a new one
        second.Should().BeSameAs(first, "a pool of one hands back the returned instance");
        third.Should().NotBeSameAs(second, "the instance was returned once, so it is leased once");
        second.Rows.Add(_RowWithEvent("second"));
        await second.SaveChangesAsync(AbortToken);
        host.Evidence.HandlerContext.Should().BeSameAs(second);
        (await _CountAsync(factory)).Should().Be(2);
    }

    [Fact]
    public void should_refuse_a_transient_context_lifetime()
    {
        // given — a transient context hands every resolution its own instance, so a handler injecting it would never
        // see the instance being saved or its unit
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessDbContext<HandlerDbContext>(
                options => options.UseSqlite("Data Source=:memory:"),
                contextLifetime: ServiceLifetime.Transient
            );

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("contextLifetime");
    }

    #region Setup

    private static async Task<TestHost> _CreateHostAsync(string databaseName, int poolSize = 8)
    {
        // A named shared-cache database lives as long as one connection holds it open.
        var keeper = new SqliteConnection($"Data Source={databaseName};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(AbortToken);

        var evidence = new HandlerEvidence();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(evidence);
        services.AddScoped<IDomainEventHandler<RowSaved>, RowSavedHandler>();
        services
            .AddHeadlessDbContextPool<HandlerDbContext>(
                options => options.UseSqlite(keeper.ConnectionString),
                poolSize: poolSize
            )
            .AddDomainEvents();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (
            var db = await provider
                .GetRequiredService<IDbContextFactory<HandlerDbContext>>()
                .CreateDbContextAsync(AbortToken)
        )
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
        }

        return new TestHost(provider, keeper, evidence);
    }

    private static Task<int> _SaveAsync(DbContext db, bool sync, CancellationToken cancellationToken)
    {
        return sync ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<int> _CountAsync(IDbContextFactory<HandlerDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync(AbortToken);

        return await db.Rows.AsNoTracking().CountAsync(AbortToken);
    }

    private static HandlerRow _RowWithEvent(string name)
    {
        var row = new HandlerRow { Name = name };
        row.Emit(new RowSaved());

        return row;
    }

    private sealed class TestHost(ServiceProvider provider, SqliteConnection keeper, HandlerEvidence evidence)
        : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public HandlerEvidence Evidence { get; } = evidence;

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await keeper.DisposeAsync();
        }
    }

    #endregion

    #region Test model

    private sealed record RowSaved;

    private sealed class HandlerEvidence
    {
        public HandlerDbContext? HandlerContext { get; set; }

        public IUnitOfWork? HandlerUnit { get; set; }

        public bool HandlerUnitTransactionMatched { get; set; }
    }

    private sealed class RowSavedHandler(HandlerDbContext db, HandlerEvidence evidence) : IDomainEventHandler<RowSaved>
    {
        public ValueTask HandleAsync(EventContext<RowSaved> context, CancellationToken cancellationToken = default)
        {
            var unit = db.UnitOfWork();
            evidence.HandlerContext = db;
            evidence.HandlerUnit = unit;
            evidence.HandlerUnitTransactionMatched =
                unit?.Resource is IRelationalUnitOfWorkResource resource
                && ReferenceEquals(resource.Transaction, db.Database.CurrentTransaction?.GetDbTransaction());

            return ValueTask.CompletedTask;
        }
    }

    public sealed class HandlerRow : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public void Emit(object domainEvent) => AddDomainEvent(domainEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class HandlerDbContext(DbContextOptions<HandlerDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<HandlerRow> Rows => Set<HandlerRow>();

        public override string DefaultSchema => "";
    }

    #endregion
}
