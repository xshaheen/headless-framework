// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
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
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var first = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");
        var second = new SettingValueRecord(Guid.NewGuid(), "Font", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new SettingValueRecord(Guid.NewGuid(), "Locale", "new", "Tenant", "t1");
        var validUpdate = new SettingValueRecord(first.Id, "Theme", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new SettingValueRecord(
            second.Id,
            "Font",
            new string('x', SettingValueRecordConstants.ValueMaxLength + 1),
            "Tenant",
            "t1"
        );

        // when
        var act = async () => await repository.SaveAsync([added], [validUpdate, failingUpdate], [], AbortToken);

        // then
        await act.Should().ThrowAsync<DbUpdateException>();
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "old"), ("Font", "old")]);
    }
}
