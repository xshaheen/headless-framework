// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingValueRecordRepositoryTests(SettingsTestFixture fixture) : SettingsTestBase(fixture)
{
    [Fact]
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var kept = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var removed = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var changed = new SettingValueRecord(kept.Id, "Theme", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "new"), ("Locale", "new")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await Fixture.ResetAsync();
        var interceptor = new FailingWriteInterceptor(failOnCommand: 3);
        using var host = CreateHost(builder =>
            // One statement per command makes each write a separate round trip, so the first two have run on the
            // server before the third fails. Only a transaction spanning all three can undo them.
            builder.Services.ConfigureDbContext<SettingsTestDbContext>(options =>
                options.UseNpgsql(npgsql => npgsql.MaxBatchSize(1)).AddInterceptors(interceptor)
            )
        );
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var second = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var firstUpdate = new SettingValueRecord(first.Id, "Theme", "new", "Tenant", "t1");
        var secondUpdate = new SettingValueRecord(second.Id, "Font", "new", "Tenant", "t1");

        // when
        interceptor.Arm();
        var act = async () => await repository.SaveAsync([added], [firstUpdate, secondUpdate], [], AbortToken);

        // then
        await act.Should().ThrowAsync<DbUpdateException>().WithInnerException(typeof(InvalidOperationException));
        interceptor.Disarm();
        interceptor.SucceededCommands.Should().BeGreaterThanOrEqualTo(2);
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "old"), ("Font", "old")]);
    }

    /// <summary>Counts commands that complete while armed and throws on the chosen one.</summary>
    private sealed class FailingWriteInterceptor(int failOnCommand) : DbCommandInterceptor
    {
        private int _started;
        private int _succeeded;
        private volatile bool _armed;

        public int SucceededCommands => Volatile.Read(ref _succeeded);

        public void Arm()
        {
            Interlocked.Exchange(ref _started, 0);
            Interlocked.Exchange(ref _succeeded, 0);
            _armed = true;
        }

        public void Disarm() => _armed = false;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _FailIfChosen();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            _CountSuccess();
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _FailIfChosen();
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            _CountSuccess();
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        private void _FailIfChosen()
        {
            if (_armed && Interlocked.Increment(ref _started) == failOnCommand)
            {
                throw new InvalidOperationException($"Simulated failure of write command {failOnCommand}.");
            }
        }

        private void _CountSuccess()
        {
            if (_armed)
            {
                Interlocked.Increment(ref _succeeded);
            }
        }
    }
}
