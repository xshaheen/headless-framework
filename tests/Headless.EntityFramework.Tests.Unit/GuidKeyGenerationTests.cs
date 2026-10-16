// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class GuidKeyGenerationTests : TestBase
{
    [Fact]
    public async Task should_generate_guid_keys_from_registered_generator_when_entities_added()
    {
        // given
        var generator = new CountingGuidGenerator();
        await using var harness = await Harness.CreateAsync(services =>
            services.AddKeyedSingleton<IGuidGenerator>(SequentialGuidType.Version7, generator)
        );
        await using var scope = harness.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KeyedDbContext>();

        // when
        var rows = Enumerable.Range(0, 100).Select(_ => new GuidRow { Id = Guid.Empty }).ToList();
        db.AddRange(rows);

        // then
        generator.Calls.Should().Be(100);
        rows.Should().OnlyHaveUniqueItems(r => r.Id).And.NotContain(r => r.Id == Guid.Empty);
    }

    [Fact]
    public async Task should_prefer_keyed_generator_for_provider_order_when_registered()
    {
        // given
        var unkeyed = new CountingGuidGenerator();
        var keyed = new CountingGuidGenerator();
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton<IGuidGenerator>(unkeyed);
            services.AddKeyedSingleton<IGuidGenerator>(SequentialGuidType.Version7, keyed);
        });
        await using var scope = harness.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KeyedDbContext>();

        // when
        db.Add(new GuidRow { Id = Guid.Empty });
        db.Add(new GuidRow { Id = Guid.Empty });

        // then: SQLite is not SQL Server, so the Version7-keyed generator serves the key
        keyed.Calls.Should().Be(2);
        unkeyed.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_generate_version7_keys_when_context_has_no_application_provider()
    {
        // given
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var options = new DbContextOptionsBuilder<KeyedDbContext>().UseSqlite(connection).Options;
        await using var db = new KeyedDbContext(options);
        var row = new GuidRow { Id = Guid.Empty };

        // when
        db.Add(row);

        // then
        row.Id.Version.Should().Be(7);
    }

    [Fact]
    public async Task should_generate_compact_string_keys_from_registered_generator_when_opted_in()
    {
        // given
        var generator = new CountingGuidGenerator();
        await using var harness = await Harness.CreateAsync(
            services => services.AddKeyedSingleton<IGuidGenerator>(SequentialGuidType.Version7, generator),
            compactStringKeys: true
        );
        await using var scope = harness.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KeyedDbContext>();
        var row = new StringRow();

        // when
        db.Add(row);

        // then
        generator.Calls.Should().Be(1);
        row.Id.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task should_not_allocate_per_key_for_provider_lookup_when_generating_keys()
    {
        // given
        await using var harness = await Harness.CreateAsync(_ => { });
        await using var scope = harness.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KeyedDbContext>();
        var entry = db.Entry(new GuidRow { Id = Guid.Empty });
        var generator = entry
            .Property(nameof(GuidRow.Id))
            .Metadata.GetValueGeneratorFactory()!(entry.Property(nameof(GuidRow.Id)).Metadata, entry.Metadata);

        for (var i = 0; i < 100; i++)
        {
            generator.Next(entry);
        }

        // when
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            generator.Next(entry);
        }

        var perKey = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;

        // then: a Version7 Guid is a struct returned boxed once by the non-generic Next; the provider name, which
        // builds a new AssemblyName on every read, must not be read per key
        perKey.Should().BeLessThan(100);
    }

    private sealed class CountingGuidGenerator : IGuidGenerator
    {
        public int Calls { get; private set; }

        public Guid Create()
        {
            Calls++;

            return Guid.CreateVersion7();
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private ServiceProvider _provider = null!;

        public static async Task<Harness> CreateAsync(
            Action<IServiceCollection> configure,
            bool compactStringKeys = false
        )
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            configure(services);
            services.AddHeadlessDbContext<KeyedDbContext>(options =>
            {
                options.UseSqlite(harness._connection);

                if (compactStringKeys)
                {
                    options.GenerateCompactGuidForStringPrimaryKeys();
                }
            });
            harness._provider = services.BuildServiceProvider();

            return harness;
        }

        public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class GuidRow : Entity<Guid>;

    public sealed class StringRow
    {
        public string Id { get; set; } = null!;
    }

    private sealed class KeyedDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Mapped before the base call: the framework's key conventions run over the types mapped at that point.
            modelBuilder.Entity<GuidRow>();
            modelBuilder.Entity<StringRow>().Property(e => e.Id).ValueGeneratedOnAdd();
            base.OnModelCreating(modelBuilder);
        }
    }
}
