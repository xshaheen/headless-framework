// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Two <see cref="DbContext" /> types over one PostgreSQL database, the second built over the first one's
/// connection: the second joins the first one's unit by adopting its Npgsql transaction, so both contexts'
/// writes commit or roll back together — the modular-monolith shape, proven on the real provider.
/// </summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlSharedConnectionJoinTests(PostgreSqlUnitOfWorkFixture fixture) : TestBase
{
    [Fact]
    public async Task should_commit_both_contexts_writes_in_one_transaction_when_the_sibling_joins()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var owner = scope.ServiceProvider.GetRequiredService<PostgreSqlCrossProviderJoinTests.ProbeDbContext>();
        await using var sibling = _CreateSibling((NpgsqlConnection)owner.Database.GetDbConnection());

        await using (var unit = await factory.BeginAsync(owner, cancellationToken: AbortToken))
        {
            owner.Probes.Add(new PostgreSqlCrossProviderJoinTests.ProbeRow { Name = "owner" });
            await owner.SaveChangesAsync(AbortToken);

            await factory.RunAsync(
                sibling,
                async (joined, ct) =>
                {
                    joined.Should().BeSameAs(unit);
                    sibling.Rows.Add(new SiblingRow { Name = "sibling" });
                    await sibling.SaveChangesAsync(ct);
                },
                cancellationToken: AbortToken
            );

            sibling
                .Database.CurrentTransaction!.GetDbTransaction()
                .Should()
                .BeSameAs(owner.Database.CurrentTransaction!.GetDbTransaction());
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "nothing is visible before the commit");

            await unit.CompleteAsync(AbortToken);
        }

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(2);
        sibling.Database.CurrentTransaction.Should().BeNull("the unit released the adopted transaction");
    }

    [Fact]
    public async Task should_roll_back_the_sibling_writes_with_the_owner()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var owner = scope.ServiceProvider.GetRequiredService<PostgreSqlCrossProviderJoinTests.ProbeDbContext>();
        await using var sibling = _CreateSibling((NpgsqlConnection)owner.Database.GetDbConnection());

        await using (var unit = await factory.BeginAsync(owner, cancellationToken: AbortToken))
        {
            // A plain save on the sibling, after reading the unit the way a repository would.
            sibling.UnitOfWork().Should().BeSameAs(unit);
            sibling.Rows.Add(new SiblingRow { Name = "sibling" });
            await sibling.SaveChangesAsync(AbortToken);

            await unit.RollbackAsync();
        }

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
        sibling.Database.CurrentTransaction.Should().BeNull();
    }

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<PostgreSqlCrossProviderJoinTests.ProbeDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString)
        );

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SiblingDbContext _CreateSibling(NpgsqlConnection connection)
    {
        return new SiblingDbContext(new DbContextOptionsBuilder<SiblingDbContext>().UseNpgsql(connection).Options);
    }

    /// <summary>A second context type over the same table, standing in for another module's context.</summary>
    private sealed class SiblingDbContext(DbContextOptions<SiblingDbContext> options) : DbContext(options)
    {
        public DbSet<SiblingRow> Rows => Set<SiblingRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SiblingRow>(entity =>
            {
                entity.ToTable("probe_rows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.Name).HasColumnName("name");
            });
        }
    }

    private sealed class SiblingRow
    {
        public int Id { get; init; }

        public string? Name { get; init; }
    }
}
