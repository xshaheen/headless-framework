// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EntityModelConventionTests : TestBase
{
    [Fact]
    public async Task should_apply_headless_mapping_when_entity_is_introduced_after_base_on_model_creating()
    {
        // given
        await using var connection = await _OpenAsync();
        await using var db = new LateEntityDbContext(_Options<LateEntityDbContext>(connection));

        // when
        var entity = db.Model.FindEntityType(typeof(LateNote))!;

        // then
        var id = entity.FindProperty(nameof(LateNote.Id))!;
        id.ValueGenerated.Should().Be(ValueGenerated.Never);
        id.GetValueGeneratorFactory().Should().NotBeNull();
        entity
            .FindProperty(nameof(LateNote.HappenedAt))!
            .GetValueConverter()
            .Should()
            .BeOfType<NormalizeDateTimeValueConverter>();
        entity.FindDeclaredQueryFilter(HeadlessQueryFilters.NotDeletedFilter).Should().NotBeNull();
        entity.FindProperty(nameof(LateNote.IsDeleted))!.GetDefaultValue().Should().Be(false);
        entity.FindProperty(nameof(LateNote.CreatedById))!.GetMaxLength().Should().Be(DomainConstants.IdMaxLength);
        entity
            .FindNavigation(nameof(LateNote.CreatedBy))!
            .ForeignKey.DeleteBehavior.Should()
            .Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public async Task should_normalize_date_time_values_of_an_entity_introduced_after_base_on_model_creating()
    {
        // given
        await using var connection = await _OpenAsync();
        var local = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);

        await using var provider = _Provider<LateEntityDbContext>(connection);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LateEntityDbContext>();
            await db.Database.EnsureCreatedAsync(AbortToken);
            db.Add(new LateNote { Id = Guid.Empty, HappenedAt = local });
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        await using var readScope = provider.CreateAsyncScope();
        var stored = await readScope
            .ServiceProvider.GetRequiredService<LateEntityDbContext>()
            .Set<LateNote>()
            .SingleAsync(AbortToken);

        // then
        stored.HappenedAt.Kind.Should().Be(DateTimeKind.Utc);
        stored.HappenedAt.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public async Task should_keep_explicit_configuration_made_before_or_after_base_on_model_creating()
    {
        // given
        await using var connection = await _OpenAsync();
        await using var db = new ExplicitlyMappedDbContext(_Options<ExplicitlyMappedDbContext>(connection));

        // when
        var entity = db.Model.FindEntityType(typeof(LateNote))!;

        // then
        entity.FindProperty(nameof(LateNote.IsDeleted))!.GetColumnName().Should().Be("is_deleted");
        entity.FindProperty(nameof(LateNote.CreatedAt))!.GetColumnName().Should().Be("created_at");
        var storeKey = db.Model.FindEntityType(typeof(LateAccount))!.FindProperty(nameof(LateAccount.Id))!;
        storeKey.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
        storeKey.GetValueGeneratorFactory().Should().BeNull();
    }

    [Fact]
    public async Task should_filter_soft_deleted_rows_when_is_deleted_maps_to_a_renamed_column()
    {
        // given
        await using var connection = await _OpenAsync();
        await using var provider = _Provider<ExplicitlyMappedDbContext>(connection);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExplicitlyMappedDbContext>();
            await db.Database.EnsureCreatedAsync(AbortToken);
            db.Add(new LateNote { Id = Guid.Empty });
            db.Add(new LateNote { Id = Guid.Empty, IsDeleted = true });
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        await using var readScope = provider.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<ExplicitlyMappedDbContext>();
        var visible = await reader.Set<LateNote>().CountAsync(AbortToken);
        var all = await reader.Set<LateNote>().IgnoreNotDeletedFilter().CountAsync(AbortToken);

        // then
        visible.Should().Be(1);
        all.Should().Be(2);
    }

    [Fact]
    public async Task should_map_actor_references_when_the_actor_exposes_an_inverse_collection()
    {
        // given: two references to the same actor type and one inverse collection on it, which relationship
        // discovery cannot pair on its own
        await using var connection = await _OpenAsync();
        await using var db = new AmbiguousActorDbContext(_Options<AmbiguousActorDbContext>(connection));

        // when
        var entity = db.Model.FindEntityType(typeof(ReviewedNote))!;

        // then
        var createdBy = entity.FindNavigation(nameof(ReviewedNote.CreatedBy))!.ForeignKey;
        createdBy.Properties.Should().ContainSingle(x => x.Name == nameof(ReviewedNote.CreatedById));
        createdBy.PrincipalToDependent.Should().BeNull();
        entity
            .FindNavigation(nameof(ReviewedNote.UpdatedBy))!
            .ForeignKey.DeleteBehavior.Should()
            .Be(DeleteBehavior.Restrict);
    }

    private static async Task<SqliteConnection> _OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private static ServiceProvider _Provider<TContext>(SqliteConnection connection)
        where TContext : HeadlessDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<TContext>(options => options.UseSqlite(connection));

        return services.BuildServiceProvider();
    }

    private static DbContextOptions<TContext> _Options<TContext>(SqliteConnection connection)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>().UseSqlite(connection).Options;
    }

    public sealed class LateAccount : Entity<Guid>;

    public sealed class LateNote
        : Entity<Guid>,
            ICreateAudit<Guid?, LateAccount>,
            IUpdateAudit<Guid?, LateAccount>,
            IDeleteAudit
    {
        public DateTime HappenedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public Guid? CreatedById { get; set; }

        public LateAccount CreatedBy { get; set; } = null!;

        public DateTimeOffset? UpdatedAt { get; set; }

        public Guid? UpdatedById { get; set; }

        public LateAccount? UpdatedBy { get; set; }

        public void Update(DateTimeOffset now, Guid? byId = null, LateAccount? by = null)
        {
            UpdatedAt = now;
            UpdatedById = byId;
            UpdatedBy = by;
        }

        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        public DateTimeOffset? RestoredAt { get; set; }
    }

    public sealed class Reviewer : Entity<Guid>
    {
        public List<ReviewedNote> Notes { get; set; } = [];
    }

    public sealed class ReviewedNote : Entity<Guid>, ICreateAudit<Guid?, Reviewer>, IUpdateAudit<Guid?, Reviewer>
    {
        public DateTimeOffset CreatedAt { get; set; }

        public Guid? CreatedById { get; set; }

        public Reviewer CreatedBy { get; set; } = null!;

        public DateTimeOffset? UpdatedAt { get; set; }

        public Guid? UpdatedById { get; set; }

        public Reviewer? UpdatedBy { get; set; }

        public void Update(DateTimeOffset now, Guid? byId = null, Reviewer? by = null)
        {
            UpdatedAt = now;
            UpdatedById = byId;
            UpdatedBy = by;
        }
    }

    private sealed class AmbiguousActorDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        public DbSet<ReviewedNote> Notes => Set<ReviewedNote>();
    }

    // No DbSet: both entity types are first introduced after the base call.
    private sealed class LateEntityDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<LateAccount>();
            modelBuilder.Entity<LateNote>();
        }
    }

    private sealed class ExplicitlyMappedDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LateNote>().Property(x => x.IsDeleted).HasColumnName("is_deleted");
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<LateNote>().Property(x => x.CreatedAt).HasColumnName("created_at");
            modelBuilder.Entity<LateAccount>().Property(x => x.Id).ValueGeneratedOnAdd();
        }
    }
}
