// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The mixed-stack join: EF Core owns the unit of work, and a raw-ADO helper handed the context's connection —
/// a Dapper repository, a bulk insert — wraps its own work in the Npgsql <c>RunAsync(connection, …)</c> and
/// joins that unit through the connection binding. The reverse (EF beginning over a connection an ADO unit
/// already owns) is refused rather than joined: an owning begin over someone else's transaction has no honest
/// semantics.
/// </summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlCrossProviderJoinTests(PostgreSqlUnitOfWorkFixture fixture) : TestBase
{
    [Fact]
    public async Task should_join_an_ef_owned_unit_from_the_npgsql_run_async_on_the_contexts_connection()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        IUnitOfWork? joined = null;

        await using (var owner = await factory.BeginAsync(db, cancellationToken: AbortToken))
        {
            // The raw-ADO helper's own RunAsync, on the connection EF opened the transaction on.
            await factory.RunAsync(
                connection,
                async (unit, ct) =>
                {
                    joined = unit;
                    var relational = (IRelationalUnitOfWorkResource)unit.Resource!;
                    await PostgreSqlUnitOfWorkFixture.InsertProbeRowAsync(
                        connection,
                        (NpgsqlTransaction)relational.Transaction,
                        "dapper-row",
                        ct
                    );
                },
                cancellationToken: AbortToken
            );

            joined.Should().BeSameAs(owner);
            owner.State.Should().Be(UnitOfWorkState.Active);
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the ADO block commits nothing on its own");

            await owner.CompleteAsync(AbortToken);
        }

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
        connection.UnitOfWork().Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_an_ef_begin_over_a_connection_an_ado_unit_already_owns()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var adoOwner = await factory.BeginAsync(connection, cancellationToken: AbortToken);

        var act = () => factory.BeginAsync(db, cancellationToken: AbortToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*connection already carries an active unit of work*RunAsync(connection*"
        );
        adoOwner.State.Should().Be(UnitOfWorkState.Active, "the refused begin leaves the owner untouched");
    }

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<ProbeDbContext>(options => options.UseNpgsql(fixture.ConnectionString));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Maps the fixture's probe table so EF can open the transaction the ADO block joins.</summary>
    public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Probes => Set<ProbeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeRow>(entity =>
            {
                entity.ToTable("probe_rows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.Name).HasColumnName("name");
            });
        }
    }

    public sealed class ProbeRow
    {
        public int Id { get; init; }

        public string? Name { get; init; }
    }
}
