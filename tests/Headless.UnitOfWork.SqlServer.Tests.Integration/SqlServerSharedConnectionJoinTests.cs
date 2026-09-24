// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Two <see cref="DbContext" /> types over one SQL Server database, the second built over the first one's
/// connection: the second joins the first one's unit by adopting its SqlClient transaction, so both contexts'
/// writes commit or roll back together. SQL Server refuses a command on a connection with a pending local
/// transaction unless the command carries that transaction, so a sibling that did not really adopt it fails here
/// rather than passing the way it would on SQLite.
/// </summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerSharedConnectionJoinTests(SqlServerUnitOfWorkFixture fixture) : TestBase
{
    [Fact]
    public async Task should_commit_both_contexts_writes_in_one_transaction_when_the_sibling_joins()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var owner = scope.ServiceProvider.GetRequiredService<SqlServerCrossProviderJoinTests.ProbeDbContext>();
        await using var sibling = _CreateSibling((SqlConnection)owner.Database.GetDbConnection());

        await using (var unit = await factory.BeginAsync(owner, cancellationToken: AbortToken))
        {
            owner.Probes.Add(new SqlServerCrossProviderJoinTests.ProbeRow { Name = "owner" });
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

            // No mid-transaction count: the uncommitted rows would block an independent SQL Server reader until
            // timeout. The shared transaction and the unit's state prove nothing committed on its own.
            sibling
                .Database.CurrentTransaction!.GetDbTransaction()
                .Should()
                .BeSameAs(owner.Database.CurrentTransaction!.GetDbTransaction());
            unit.State.Should().Be(UnitOfWorkState.Active, "the joined block commits nothing on its own");

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
        var owner = scope.ServiceProvider.GetRequiredService<SqlServerCrossProviderJoinTests.ProbeDbContext>();
        await using var sibling = _CreateSibling((SqlConnection)owner.Database.GetDbConnection());

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
        services.AddDbContext<SqlServerCrossProviderJoinTests.ProbeDbContext>(options =>
            options.UseSqlServer(fixture.ConnectionString)
        );

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SiblingDbContext _CreateSibling(SqlConnection connection)
    {
        return new SiblingDbContext(new DbContextOptionsBuilder<SiblingDbContext>().UseSqlServer(connection).Options);
    }

    /// <summary>A second context type over the same table, standing in for another module's context.</summary>
    private sealed class SiblingDbContext(DbContextOptions<SiblingDbContext> options) : DbContext(options)
    {
        public DbSet<SiblingRow> Rows => Set<SiblingRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SiblingRow>(entity =>
            {
                entity.ToTable("probe_rows", "dbo");
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
