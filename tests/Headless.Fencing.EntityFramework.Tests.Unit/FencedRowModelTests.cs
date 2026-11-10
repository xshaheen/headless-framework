// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Fencing;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.Sql.SqlServer;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSubstitute;

namespace Tests;

public sealed class FencedRowModelTests : TestBase
{
    private static readonly ISqlDialect _Dialect = SqliteDialect.Instance;

    [Fact]
    public void should_map_the_lease_onto_the_default_columns_when_no_names_are_given()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var lease = context.Model.FindEntityType(typeof(WorkItem))!.FindComplexProperty(nameof(WorkItem.Lease))!;
        var store = StoreObjectIdentifier.Table("work_items");

        string column(string name) => lease.ComplexType.FindProperty(name)!.GetColumnName(store)!;

        column(nameof(RowLeaseSnapshot.Generation)).Should().Be("lease_generation");
        column(nameof(RowLeaseSnapshot.State)).Should().Be("lease_state");
        column(nameof(RowLeaseSnapshot.ExpiresAt)).Should().Be("lease_expires_at");
        column(nameof(RowLeaseSnapshot.Holder)).Should().Be("lease_holder");
        column(nameof(RowLeaseSnapshot.TakeoverCount)).Should().Be("lease_takeover_count");

        var state = lease.ComplexType.FindProperty(nameof(RowLeaseSnapshot.State))!;
        state.GetTypeMapping().Converter!.ProviderClrType.Should().Be<short>();
        lease.ComplexType.FindProperty(nameof(RowLeaseSnapshot.Holder))!.GetMaxLength().Should().Be(200);
        lease.ComplexType.FindProperty(nameof(RowLeaseSnapshot.Generation))!.GetDefaultValue().Should().Be(0L);
    }

    [Fact]
    public void should_never_write_the_lease_columns_on_update_when_the_entity_is_saved()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var lease = context.Model.FindEntityType(typeof(WorkItem))!.FindComplexProperty(nameof(WorkItem.Lease))!;

        lease
            .ComplexType.GetProperties()
            .Select(p => p.GetAfterSaveBehavior())
            .Should()
            .AllBeEquivalentTo(PropertySaveBehavior.Ignore);
    }

    [Fact]
    public void should_build_the_descriptor_from_the_models_names_when_the_columns_are_custom()
    {
        var columns = new FencedRowColumns
        {
            Generation = "LeaseGeneration",
            State = "LeaseState",
            ExpiresAt = "LeaseExpiresAt",
            Holder = "LeaseHolder",
            TakeoverCount = "LeaseTakeoverCount",
        };
        using var context = _Context(
            "custom",
            b => b.Entity<WorkItem>().ToTable("Work_Items").HasRowLease(w => w.Lease, columns)
        );

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect);

        table.Name.Should().Be("Work_Items");
        table.Dialect.Should().BeSameAs(_Dialect);
        table.Key.Parts.Should().ContainSingle().Which.Column.Should().Be("Id");
        table.Key.Parts[0].Type.Should().Be(SqlColumnType.Guid);
    }

    [Fact]
    public void should_return_the_cached_descriptor_when_built_twice_for_one_model()
    {
        using var first = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );
        using var second = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        first
            .FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect)
            .Should()
            .BeSameAs(second.FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect));
    }

    [Fact]
    public void should_bind_the_converted_key_when_the_primary_key_has_a_value_converter()
    {
        using var context = _Context(
            "converted",
            b =>
                b.Entity<TypedItem>(e =>
                {
                    e.ToTable("typed_items");
                    e.Property(x => x.Id).HasConversion(id => id.Value, value => new TypedId(value));
                    e.HasRowLease(x => x.Lease);
                })
        );
        var id = new TypedId(Guid.NewGuid());

        var table = context.FencedRowTable<TypedItem, TypedId>(x => x.Lease, _Dialect);

        table.Key.Parts.Should().ContainSingle();
        table.Key.Parts[0].Type.Should().Be(SqlColumnType.Guid);
        table.Key.Parts[0].Value(id).Should().Be(id.Value);
    }

    [Fact]
    public void should_refuse_a_composite_primary_key_when_no_key_mapping_is_given()
    {
        using var context = _CompositeContext();

        var build = () => context.FencedRowTable<CompositeItem, CompositeKey>(x => x.Lease, _Dialect);

        build.Should().Throw<InvalidOperationException>().WithMessage("*has 2 columns*Composite*");
    }

    [Fact]
    public void should_build_a_composite_descriptor_when_the_key_mapping_is_given()
    {
        using var context = _CompositeContext();
        var key = FencedRowKey<CompositeKey>.Composite(
            [
                new FencedRowKeyPart<CompositeKey>("Tenant", SqlColumnType.Int32, k => k.Tenant),
                new FencedRowKeyPart<CompositeKey>("Number", SqlColumnType.Int32, k => k.Number),
            ],
            static (reader, ordinal) => new CompositeKey(reader.GetInt32(ordinal), reader.GetInt32(ordinal + 1))
        );

        var table = context.FencedRowTable<CompositeItem, CompositeKey>(x => x.Lease, _Dialect, key);

        table.Key.Should().BeSameAs(key);
        table.Name.Should().Be("composite_items");
    }

    [Fact]
    public void should_refuse_an_entity_split_across_tables()
    {
        using var context = _Context(
            "split",
            b =>
                b.Entity<WorkItem>(e =>
                {
                    e.ToTable("work_items").HasRowLease(w => w.Lease);
                    e.SplitToTable("work_details", t => t.Property(w => w.Status));
                })
        );

        var build = () => context.FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect);

        build.Should().Throw<InvalidOperationException>().WithMessage("*maps to 2 tables*");
    }

    [Fact]
    public void should_refuse_a_table_per_type_derived_entity()
    {
        using var context = _Context(
            "tpt",
            b =>
            {
                b.Entity<BaseItem>().ToTable("base_items").HasRowLease(x => x.Lease);
                b.Entity<DerivedItem>().ToTable("derived_items");
            }
        );

        var build = () => context.FencedRowTable<DerivedItem, Guid>(x => x.Lease, _Dialect);

        build.Should().Throw<InvalidOperationException>().WithMessage("*maps to 2 tables*");
    }

    [Fact]
    public void should_refuse_a_dialect_that_does_not_match_the_contexts_provider()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var build = () => context.FencedRowTable<WorkItem, Guid>(w => w.Lease, SqlServerDialect.Instance);

        build.Should().Throw<ArgumentException>().WithParameterName("dialect").WithMessage("*Sqlite*");
    }

    [Fact]
    public void should_refuse_a_key_type_that_is_not_the_primary_keys_type()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var build = () => context.FencedRowTable<WorkItem, long>(w => w.Lease, _Dialect);

        build.Should().Throw<InvalidOperationException>().WithMessage("*'Guid', not the key type 'Int64'*");
    }

    [Fact]
    public void should_refuse_a_lease_that_has_row_lease_never_mapped()
    {
        using var context = _Context(
            "unmapped",
            b => b.Entity<WorkItem>().ToTable("work_items").ComplexProperty(w => w.Lease)
        );

        var build = () => context.FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect);

        build.Should().Throw<InvalidOperationException>().WithMessage("*Call HasRowLease*");
    }

    [Fact]
    public void should_refuse_a_lease_expression_that_is_not_a_property_of_the_entity()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var build = () => context.FencedRowTable<WorkItem, Guid>(_ => default, _Dialect);

        build.Should().Throw<ArgumentException>().WithParameterName("lease");
    }

    [Fact]
    public void should_name_a_sqlite_table_without_a_schema_even_when_the_model_has_one()
    {
        using var context = _Context(
            "plain",
            b =>
            {
                b.HasDefaultSchema("ops");
                b.Entity<WorkItem>().ToTable("items").HasRowLease(w => w.Lease);
            }
        );

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, _Dialect, generationSequence: "generations");

        table.Name.Should().Be("items", "SQLite has no schemas, so the model's schema never names the table");
    }

    [Fact]
    public void should_address_the_table_in_its_own_schema_before_the_models_default_one()
    {
        using var context = _Context(
            "sqlserver-own-schema",
            b =>
            {
                b.HasDefaultSchema("work");
                b.Entity<WorkItem>().ToTable("work_items", "ops").HasRowLease(w => w.Lease);
            },
            SqlServerDialect.Instance
        );

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, SqlServerDialect.Instance);

        table.Name.Should().Be("ops.work_items");
    }

    [Fact]
    public void should_address_the_table_in_the_models_default_schema_when_it_names_none()
    {
        using var context = _Context(
            "sqlserver-default-schema",
            b =>
            {
                b.HasDefaultSchema("work");
                b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease);
            },
            SqlServerDialect.Instance
        );

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, SqlServerDialect.Instance);

        table.Name.Should().Be("work.work_items");
    }

    [Fact]
    public void should_name_the_table_without_a_schema_instead_of_guessing_one_when_the_model_has_none()
    {
        using var context = _Context(
            "sqlserver-no-schema",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease),
            SqlServerDialect.Instance
        );

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, SqlServerDialect.Instance);

        table
            .Name.Should()
            .Be("work_items", "the database resolves an unqualified name in the connection's default schema");
    }

    [Fact]
    public void should_bind_a_long_primary_key_as_a_64_bit_integer()
    {
        var part = _KeyPart<long>("long-key");

        part.Type.Should().Be(SqlColumnType.Int64);
        part.Value(42L).Should().Be(42L);
    }

    [Fact]
    public void should_bind_int_and_short_primary_keys_in_their_own_width()
    {
        _KeyPart<int>("int-key").Type.Should().Be(SqlColumnType.Int32);
        _KeyPart<short>("short-key").Type.Should().Be(SqlColumnType.Int16);
    }

    [Fact]
    public void should_bind_a_string_primary_key_sized_to_its_column()
    {
        _KeyPart<string>("sized-string-key", e => e.Property(x => x.Id).HasMaxLength(64))
            .Type.Should()
            .Be(SqlColumnType.Text(64));
        _KeyPart<string>("unbounded-string-key")
            .Type.Should()
            .Be(SqlColumnType.Text(-1), "an unbounded column binds unsized");
    }

    [Fact]
    public void should_bind_a_byte_array_primary_key_as_binary()
    {
        _KeyPart<byte[]>("binary-key").Type.Should().Be(SqlColumnType.Binary);
    }

    [Fact]
    public void should_bind_a_converted_key_in_the_long_its_converter_stores_and_read_it_back()
    {
        using var context = _Context(
            "converted-long",
            b =>
                b.Entity<KeyedItem<LongId>>(e =>
                {
                    e.ToTable("long_items");
                    e.Property(x => x.Id).HasConversion(id => id.Value, value => new LongId(value));
                    e.HasRowLease(x => x.Lease);
                })
        );

        var table = context.FencedRowTable<KeyedItem<LongId>, LongId>(x => x.Lease, _Dialect);

        table.Key.Parts.Should().ContainSingle();
        table.Key.Parts[0].Type.Should().Be(SqlColumnType.Int64);
        table.Key.Parts[0].Value(new LongId(7)).Should().Be(7L);
    }

    [Fact]
    public void should_refuse_a_primary_key_stored_as_a_type_no_fenced_row_key_binds()
    {
        using var context = _Context(
            "decimal-key",
            b => b.Entity<KeyedItem<decimal>>(e => e.ToTable("decimal_items").HasRowLease(x => x.Lease))
        );

        var build = () => context.FencedRowTable<KeyedItem<decimal>, decimal>(x => x.Lease, _Dialect);

        build.Should().Throw<NotSupportedException>().WithMessage("*'Decimal'*");
    }

    [Fact]
    public void should_refuse_a_dialect_without_a_known_clock()
    {
        var dialect = Substitute.For<ISqlDialect>();
        dialect.DisplayName.Returns("Oracle");

        var map = () => new ModelBuilder().UseFencingDatabaseClock(dialect);

        map.Should().Throw<NotSupportedException>().WithMessage("*'Oracle'*");
    }

    [Fact]
    public void should_compare_the_expiry_with_the_database_clock_when_selecting_live_leases()
    {
        using var context = _Context(
            "default",
            b => b.Entity<WorkItem>().ToTable("work_items").HasRowLease(w => w.Lease)
        );

        var live = context.Set<WorkItem>().WhereLeaseLive(w => w.Lease).ToQueryString();
        var grantable = context.Set<WorkItem>().WhereLeaseGrantable(w => w.Lease).ToQueryString();

        live.Should()
            .Contain("\"w\".\"lease_state\" = 1")
            .And.Contain("\"w\".\"lease_expires_at\" > (strftime('%Y-%m-%d %H:%M:%f', 'now') || '000+00:00')");
        // EF Core rewrites NOT (live) as "not active OR not before expiry", the clock comparison mapped to a CASE.
        grantable
            .Should()
            .Contain("\"w\".\"lease_state\" <> 1 OR")
            .And.Contain("\"w\".\"lease_expires_at\" > (strftime(");
    }

    [Fact]
    public void should_refuse_to_read_the_database_clock_outside_a_query()
    {
        var items = new[] { new WorkItem { Lease = new RowLeaseSnapshot(1, RowLeaseState.Active, null, "h", 0) } };

        var evaluate = () => items.AsQueryable().WhereLeaseLive(w => w.Lease).ToList();

        evaluate.Should().Throw<InvalidOperationException>().WithMessage("*UseFencingDatabaseClock*");
    }

    private static ModelContext _CompositeContext()
    {
        return _Context(
            "composite",
            b =>
                b.Entity<CompositeItem>(e =>
                {
                    e.ToTable("composite_items");
                    e.HasKey(x => new { x.Tenant, x.Number });
                    e.HasRowLease(x => x.Lease);
                })
        );
    }

    private static FencedRowKeyPart<TKey> _KeyPart<TKey>(
        string key,
        Action<EntityTypeBuilder<KeyedItem<TKey>>>? configure = null
    )
        where TKey : notnull
    {
        using var context = _Context(
            key,
            b =>
                b.Entity<KeyedItem<TKey>>(e =>
                {
                    e.ToTable("keyed_items");
                    e.HasRowLease(x => x.Lease);
                    configure?.Invoke(e);
                })
        );

        var table = context.FencedRowTable<KeyedItem<TKey>, TKey>(x => x.Lease, _Dialect);

        table.Key.Parts.Should().ContainSingle();

        return table.Key.Parts[0];
    }

    private static ModelContext _Context(string key, Action<ModelBuilder> build, ISqlDialect? dialect = null)
    {
        dialect ??= _Dialect;
        var builder = new DbContextOptionsBuilder<ModelContext>();

        // The descriptor reads only the model, so the SQL Server context never opens its connection.
        _ =
            dialect is SqlServerDialect
                ? builder.UseSqlServer("Server=localhost;Database=fencing_model")
                : builder.UseSqlite("Data Source=:memory:");

        var options = builder.ReplaceService<IModelCacheKeyFactory, ModelKeyFactory>().Options;

        return new ModelContext(options, key, build, dialect);
    }

    private sealed class ModelContext(
        DbContextOptions options,
        string key,
        Action<ModelBuilder> build,
        ISqlDialect dialect
    ) : DbContext(options)
    {
        public string Key { get; } = key;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            build(modelBuilder);
            modelBuilder.UseFencingDatabaseClock(dialect);
        }
    }

    private sealed class ModelKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((ModelContext)context).Key, designTime);
    }

    private sealed class WorkItem
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = "new";

        public int Priority { get; set; }

        public RowLeaseSnapshot Lease { get; set; }
    }

    private readonly record struct TypedId(Guid Value);

    private readonly record struct LongId(long Value);

    private sealed class KeyedItem<TKey>
    {
        public TKey Id { get; set; } = default!;

        public RowLeaseSnapshot Lease { get; set; }
    }

    private sealed class TypedItem
    {
        public TypedId Id { get; set; }

        public RowLeaseSnapshot Lease { get; set; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CompositeKey(int Tenant, int Number);

    private sealed class CompositeItem
    {
        public int Tenant { get; set; }

        public int Number { get; set; }

        public RowLeaseSnapshot Lease { get; set; }
    }

    private class BaseItem
    {
        public Guid Id { get; set; }

        public RowLeaseSnapshot Lease { get; set; }
    }

    private sealed class DerivedItem : BaseItem
    {
        public string Name { get; set; } = "";
    }
}
