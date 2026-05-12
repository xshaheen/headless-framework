// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Headless.EntityFramework.Processors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessDbContextRuntimeExtensibilityTests
{
    [Fact]
    public async Task save_changes_should_run_custom_entry_processors_by_order()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ProcessorOrderRecorder>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeBasicEntity { Name = "ordered" };

        db.BasicEntities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        recorder.Entries.Should().Equal("early", "late");
    }

    [Fact]
    public async Task save_changes_should_use_default_processors_when_entity_does_not_emit_messages()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeBasicEntity { Name = "defaults" };

        db.BasicEntities.Add(entity);
        entity.Id.Should().Be(Guid.Empty);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        entity.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task save_changes_should_throw_when_messages_are_emitted_without_dispatcher()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits" };

        db.Entities.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IHeadlessMessageDispatcher*");
    }

    [Fact]
    public async Task save_changes_should_use_registered_message_dispatcher_when_messages_are_emitted()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddHeadlessMessageDispatcher<RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits" };

        db.Entities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        dispatcher.LocalEmitters.Should().ContainSingle();
        dispatcher.DistributedEmitters.Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_should_publish_messages_queued_on_unchanged_tracked_emitters()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddHeadlessMessageDispatcher<RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits-later" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Entry(entity).State.Should().Be(EntityState.Unchanged);
        dispatcher.LocalEmitters.Clear();
        dispatcher.DistributedEmitters.Clear();

        entity.AddMessage(new RuntimeLocalMessage("local-later"));
        entity.AddMessage(new RuntimeDistributedMessage("distributed-later"));

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        dispatcher.LocalEmitters.Should().ContainSingle();
        dispatcher.LocalEmitters[0].Messages.Should().ContainSingle(x => x.UniqueId == "local-later");
        dispatcher.DistributedEmitters.Should().ContainSingle();
        dispatcher.DistributedEmitters[0].Messages.Should().ContainSingle(x => x.UniqueId == "distributed-later");
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> _CreateProviderAsync(
        Action<IServiceCollection>? configureServices = null
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddSingleton(connection);
        // Recorder lifetime must match the processor lifetimes (singleton) so test queries observe
        // the same instance the processors write to.
        services.AddSingleton<ProcessorOrderRecorder>();
        services.AddHeadlessDbContextServices(options =>
        {
            options.AddSaveEntryProcessor<EarlyRecordingSaveEntryProcessor>(ServiceLifetime.Singleton);
            options.AddSaveEntryProcessor<LateRecordingSaveEntryProcessor>(ServiceLifetime.Singleton);
        });
        configureServices?.Invoke(services);
        services.AddDbContext<RuntimeTestDbContext>(options => options.UseSqlite(connection).AddHeadlessExtension());

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<RuntimeTestDbContext>()
            .Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        return (provider, connection);
    }

    private sealed class ProcessorOrderRecorder
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class EarlyRecordingSaveEntryProcessor(ProcessorOrderRecorder recorder) : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: RuntimeBasicEntity, State: EntityState.Added })
            {
                recorder.Entries.Add("early");
            }
        }
    }

    private sealed class LateRecordingSaveEntryProcessor(ProcessorOrderRecorder recorder) : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: RuntimeBasicEntity, State: EntityState.Added })
            {
                recorder.Entries.Add("late");
            }
        }
    }

    private sealed class RuntimeRecordingMessageDispatcher : IHeadlessMessageDispatcher
    {
        public List<EmitterLocalMessages> LocalEmitters { get; } = [];

        public List<EmitterDistributedMessages> DistributedEmitters { get; } = [];

        public Task PublishLocalAsync(
            IReadOnlyList<EmitterLocalMessages> emitters,
            IDbContextTransaction currentTransaction,
            CancellationToken cancellationToken
        )
        {
            LocalEmitters.AddRange(emitters);
            return Task.CompletedTask;
        }

        public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction)
        {
            LocalEmitters.AddRange(emitters);
        }

        public Task PublishDistributedAsync(
            IReadOnlyList<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction,
            CancellationToken cancellationToken
        )
        {
            DistributedEmitters.AddRange(emitters);
            return Task.CompletedTask;
        }

        public void PublishDistributed(
            IReadOnlyList<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction
        )
        {
            DistributedEmitters.AddRange(emitters);
        }
    }

    private sealed record RuntimeLocalMessage(string UniqueId) : ILocalMessage;

    private sealed record RuntimeDistributedMessage(string UniqueId) : IDistributedMessage;

    private sealed class RuntimeTestDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : HeadlessDbContext(services, options)
    {
        public DbSet<RuntimeEntity> Entities => Set<RuntimeEntity>();

        public DbSet<RuntimeBasicEntity> BasicEntities => Set<RuntimeBasicEntity>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RuntimeEntity>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<RuntimeBasicEntity>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    private sealed class RuntimeBasicEntity : IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; set; }

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    private sealed class RuntimeEntity : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; set; }

        public override IReadOnlyList<object> GetKeys() => [Id];
    }
}
