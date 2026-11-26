// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tests.Fixture;

namespace Tests;

[Collection<HeadlessDbContextTestFixture>]
public sealed class PostgreSqlSaveChangesCommitHealthTests(HeadlessDbContextTestFixture fixture) : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_roll_back_when_a_pipeline_owned_transaction_was_aborted_before_commit(bool sync)
    {
        var database = fixture.PostgreSql.DerivedDatabaseName($"t_{Guid.NewGuid():N}");
        await fixture.PostgreSql.CreateDatabaseAsync(database, AbortToken);
        var connectionString = fixture.PostgreSql.ConnectionStringFor(database);
        var interceptor = new AbortTransactionAfterSave();
        var outcome = new SaveOutcome();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(outcome);
        services.AddScoped<IDomainEventHandler<RowSaved>, RowSavedHandler>();
        services
            .AddHeadlessDbContext<ProbeDbContext>(options =>
                options.UseNpgsql(connectionString).AddInterceptors(interceptor)
            )
            .AddDomainEvents();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        try
        {
            await db.Database.EnsureCreatedAsync(AbortToken);
            var row = new ProbeRow();
            row.EmitSaved();
            db.Rows.Add(row);

            var save = () => sync ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(AbortToken);

            var fault = (await save.Should().ThrowAsync<PostgresException>()).Which;
            fault.SqlState.Should().Be(PostgresErrorCodes.InFailedSqlTransaction);
            outcome.Failure.Should().Be(UnitOfWorkFailureReason.RolledBack);
            outcome.Completed.Should().BeFalse();
            (await db.Rows.CountAsync(AbortToken)).Should().Be(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(AbortToken);
        }
    }

    private sealed record RowSaved;

    private sealed class SaveOutcome
    {
        public bool Completed { get; set; }

        public UnitOfWorkFailureReason? Failure { get; set; }
    }

    private sealed class RowSavedHandler(ProbeDbContext db, SaveOutcome outcome) : IDomainEventHandler<RowSaved>
    {
        public ValueTask HandleAsync(EventContext<RowSaved> context, CancellationToken cancellationToken = default)
        {
            var unit = db.UnitOfWork()!;
            unit.OnCompleted(() =>
            {
                outcome.Completed = true;

                return ValueTask.CompletedTask;
            });
            unit.OnFailed(failure =>
            {
                outcome.Failure = failure.Reason;

                return ValueTask.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AbortTransactionAfterSave : SaveChangesInterceptor
    {
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            using var command = _CreateFailingCommand(eventData);
            var execute = () => command.ExecuteNonQuery();
            execute.Should().Throw<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            await using var command = _CreateFailingCommand(eventData);
            var execute = () => command.ExecuteNonQueryAsync(cancellationToken);
            var fault = (await execute.Should().ThrowAsync<PostgresException>()).Which;
            fault.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);

            return result;
        }

        private static DbCommand _CreateFailingCommand(SaveChangesCompletedEventData eventData)
        {
            var database = eventData.Context!.Database;
            var command = database.GetDbConnection().CreateCommand();
            command.Transaction = database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT 1 / 0";

            return command;
        }
    }

    public sealed class ProbeRow : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public void EmitSaved() => AddDomainEvent(new RowSaved());

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        public override string? DefaultSchema => null;
    }
}
