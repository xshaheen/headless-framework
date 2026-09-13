// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests.Tenancy;

public abstract class MetadataTenantConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : MetadataTenantFixture
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync(AbortToken);
    }

    [Fact]
    public async Task should_filter_clr_and_shadow_rows_using_current_context_and_renamed_column()
    {
        await _SeedAsync("tenant-a");
        await _SeedAsync("tenant-b");
        IModel? firstModel = null;
        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            fixture.CurrentTenant.Id = tenant;
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
            (await db.Set<TenantRow>().SingleAsync(AbortToken)).Owner.Should().Be(tenant);
            (await db.Set<ShadowTenantRow>().Select(x => EF.Property<string>(x, "TenantId")).SingleAsync(AbortToken))
                .Should()
                .Be(tenant);
            if (firstModel is not null)
            {
                db.Model.Should().BeSameAs(firstModel);
            }
            firstModel = db.Model;
        }
    }

    [Fact]
    public async Task should_disable_only_tenant_filter_and_keep_sibling_filter_and_guard()
    {
        await _SeedAsync("tenant-a");
        await _SeedAsync("tenant-b");
        await _SeedAsync("tenant-b", hidden: true);
        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var visible = await db.Set<TenantRow>()
            .IgnoreQueryFilters([HeadlessQueryFilters.MultiTenancyFilter])
            .ToListAsync(AbortToken);
        visible.Should().HaveCount(2).And.OnlyContain(x => !x.Hidden);
        visible.Single(x => string.Equals(x.Owner, "tenant-b", StringComparison.Ordinal)).Name = "compromised";
        await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_stamp_required_shadow_alternate_key_before_add_and_reject_tenant_switch()
    {
        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var row = new ShadowTenantRow();
        db.Add(row);
        db.Entry(row).Property("TenantId").CurrentValue.Should().Be("tenant-a");
        fixture.CurrentTenant.Id = "tenant-b";
        await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<CrossTenantWriteException>();
        db.Entry(row).Property("TenantId").CurrentValue.Should().Be("tenant-a");
        (await db.Set<ShadowTenantRow>().IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false, "tenant-a", "tenant-b")]
    [InlineData(true, false, "tenant-a", "tenant-b")]
    [InlineData(false, true, "tenant-a", "tenant-b")]
    [InlineData(true, true, "tenant-a", "tenant-b")]
    [InlineData(false, false, "acme", "ACME")]
    [InlineData(true, false, "acme", "ACME")]
    public async Task should_fence_detached_writes_with_both_concurrency_tokens(
        bool delete,
        bool shadow,
        string attacker,
        string victim
    )
    {
        var id = await _SeedAsync(victim);
        fixture.CurrentTenant.Id = attacker;
        await using var scope = fixture.Services.CreateAsyncScope();
        var sql = new List<string>();
        await using var db = _Context(scope.ServiceProvider, sql);
        object crafted;
        if (shadow)
        {
            var row = new ShadowTenantRow { Id = id, Stamp = "victim-stamp" };
            db.Entry(row).Property("TenantId").CurrentValue = attacker;
            db.Attach(row);
            row.Name = "compromised";
            crafted = row;
        }
        else
        {
            var row = new TenantRow
            {
                Id = id,
                Owner = attacker,
                Stamp = "victim-stamp",
            };
            db.Attach(row);
            row.Name = "compromised";
            crafted = row;
        }
        if (delete)
        {
            db.Remove(crafted);
        }
        await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        var command = sql.Single(x => x.Contains(delete ? "DELETE FROM" : "UPDATE ", StringComparison.Ordinal));
        var predicate = command[command.IndexOf("WHERE", StringComparison.Ordinal)..];
        predicate.Should().Contain("tenant_key").And.Contain("Stamp");
        db.ChangeTracker.Clear();
        if (shadow)
        {
            (await db.Set<ShadowTenantRow>().IgnoreQueryFilters().SingleAsync(AbortToken)).Name.Should().Be("original");
        }
        else
        {
            (await db.Set<TenantRow>().IgnoreQueryFilters().SingleAsync(AbortToken)).Name.Should().Be("original");
        }
    }

    [Fact]
    public async Task should_keep_case_distinct_tenants_separate_and_scope_selected_uniqueness()
    {
        await _SeedAsync("acme", code: "shared");
        await _SeedAsync("ACME", code: "shared");
        foreach (var tenant in new[] { "acme", "ACME" })
        {
            fixture.CurrentTenant.Id = tenant;
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
            (await db.Set<TenantRow>().SingleAsync(AbortToken)).Owner.Should().Be(tenant);
            db.Add(new TenantRow { Code = "shared" });
            await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_fence_owned_only_update_with_root_predicates(bool crafted)
    {
        var id = await _SeedAsync("tenant-b");
        fixture.CurrentTenant.Id = crafted ? "tenant-a" : "tenant-b";
        await using var scope = fixture.Services.CreateAsyncScope();
        var sql = new List<string>();
        await using var db = _Context(scope.ServiceProvider, sql);
        var row = crafted
            ? new TenantRow { Id = id, Owner = "tenant-a" }
            : await db.Set<TenantRow>().SingleAsync(AbortToken);
        if (crafted)
        {
            db.Attach(row);
        }
        row.Detail.Value = "changed";
        db.ChangeTracker.DetectChanges();
        db.Entry(row).State.Should().Be(EntityState.Unchanged);
        if (crafted)
        {
            await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
        else
        {
            await db.SaveChangesAsync(AbortToken);
        }
        var update = sql.Single(x => x.Contains("UPDATE ", StringComparison.Ordinal));
        update[update.IndexOf("WHERE", StringComparison.Ordinal)..].Should().Contain("tenant_key").And.Contain("Stamp");
        db.ChangeTracker.Clear();
        (await db.Set<TenantRow>().IgnoreQueryFilters().SingleAsync(AbortToken))
            .Detail.Value.Should()
            .Be(crafted ? "original" : "changed");
    }

    [Fact]
    public async Task should_fence_detached_soft_delete_and_preserve_undeleted_victim()
    {
        var id = await _SeedAsync("tenant-b");
        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.Services.CreateAsyncScope();
        var sql = new List<string>();
        await using var db = _Context(scope.ServiceProvider, sql);
        var row = new TenantRow
        {
            Id = id,
            Owner = "tenant-a",
            Stamp = "victim-stamp",
        };
        db.Attach(row);
        row.IsDeleted = true;
        await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        var update = sql.Single(x => x.Contains("UPDATE ", StringComparison.Ordinal));
        update[update.IndexOf("WHERE", StringComparison.Ordinal)..].Should().Contain("tenant_key").And.Contain("Stamp");
        db.ChangeTracker.Clear();
        var victim = await db.Set<TenantRow>().IgnoreQueryFilters().SingleAsync(AbortToken);
        victim.IsDeleted.Should().BeFalse();
        victim.DeletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("save")]
    [InlineData("original")]
    public async Task should_reject_trailing_space_at_ef_boundary(string operation)
    {
        var id = await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = string.Equals(operation, "original", StringComparison.Ordinal)
            ? "tenant-a"
            : "tenant-a ";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        if (string.Equals(operation, "read", StringComparison.Ordinal))
        {
            await db.Invoking(x => x.Set<TenantRow>().ToListAsync(AbortToken))
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*U+0020*");
            return;
        }
        var row = new TenantRow { Id = id, Owner = "tenant-a" };
        db.Attach(row);
        row.Name = "compromised";
        if (string.Equals(operation, "original", StringComparison.Ordinal))
        {
            db.Entry(row).Property(x => x.Owner).OriginalValue = "tenant-a ";
        }
        await db.Invoking(x => x.SaveChangesAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*U+0020*");
    }

    [Fact]
    public async Task should_reject_trailing_space_direct_insert_with_database_constraint()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var q = db.GetService<ISqlGenerationHelper>();
        var sql =
            $"INSERT INTO {q.DelimitIdentifier("ShadowRows", "tenancy")} ({q.DelimitIdentifier("Id")}, {q.DelimitIdentifier("Name")}, {q.DelimitIdentifier("Stamp")}, {q.DelimitIdentifier("tenant_key")}) VALUES ({{0}}, {{1}}, {{2}}, {{3}})";
        var insert = () =>
            db.Database.ExecuteSqlRawAsync(sql, [Guid.NewGuid(), "raw", "stamp", "tenant-a "], AbortToken);
        var failure = (await insert.Should().ThrowAsync<System.Data.Common.DbException>()).Which;
        if (failure is PostgresException postgres)
        {
            postgres.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            postgres.ConstraintName.Should().Be("CK_ShadowRows_tenant_key_TenantCanonical");
        }
        else
        {
            failure.Should().BeOfType<SqlException>().Which.Number.Should().Be(547);
            failure.Message.Should().Contain("CK_ShadowRows_tenant_key_TenantCanonical");
        }
        (await db.Set<ShadowTenantRow>().IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public void should_generate_migration_collation_check_constraints_and_selected_index()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        tables.Should().HaveCount(3);
        foreach (var table in tables)
        {
            table
                .Columns.Single(x => x.Name is "tenant_key" or nameof(HostTenantRow.TenantId))
                .Collation.Should()
                .Be(fixture.Provider == TenantDatabaseProvider.SqlServer ? "Latin1_General_100_BIN2" : "C");
            table.CheckConstraints.Should().NotBeEmpty();
        }
        operations
            .OfType<CreateIndexOperation>()
            .Single(x => string.Equals(x.Name, "TenantCodeIndex", StringComparison.Ordinal))
            .Columns.Should()
            .Equal("Code", "tenant_key");
        fixture.MigrationSql.Should().Contain("COLLATE").And.Contain("CHECK").And.Contain("TenantCodeIndex");
    }

    private MetadataTenantContext _Context(IServiceProvider services, List<string> sql)
    {
        var options = new DbContextOptionsBuilder<MetadataTenantContext>();
        fixture.ConfigureOptions(options);
        options.LogTo(sql.Add, [RelationalEventId.CommandExecuted]);
        return new MetadataTenantContext(services.GetRequiredService<HeadlessDbContextServices>(), options.Options);
    }

    private async Task<Guid> _SeedAsync(string tenant, bool hidden = false, string? code = null)
    {
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var row = new TenantRow { Hidden = hidden, Code = code ?? Guid.NewGuid().ToString() };
        db.Add(row);
        db.Add(new ShadowTenantRow { Id = row.Id });
        await db.SaveChangesAsync(AbortToken);
        return row.Id;
    }
}
