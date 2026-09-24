// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class FeatureValueRecordRepositoryTests(FeaturesTestFixture fixture) : FeaturesTestBase(fixture)
{
    [Fact]
    public async Task should_save_a_value_batch_of_inserts_updates_and_deletes()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFeatureValueRecordRepository>();
        var kept = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var removed = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(kept, AbortToken);
        await repository.InsertAsync(removed, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var changed = new FeatureValueRecord(kept.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // when
        await repository.SaveAsync([added], [changed], [removed], AbortToken);

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "new"), ("Export.Enabled", "new")]);
    }

    [Fact]
    public async Task should_leave_every_value_unchanged_when_a_write_in_the_batch_fails()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFeatureValueRecordRepository>();
        var first = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "old", "Tenant", "t1");
        var second = new FeatureValueRecord(Guid.NewGuid(), "Reports.Enabled", "old", "Tenant", "t1");
        await repository.InsertAsync(first, AbortToken);
        await repository.InsertAsync(second, AbortToken);
        var added = new FeatureValueRecord(Guid.NewGuid(), "Export.Enabled", "new", "Tenant", "t1");
        var validUpdate = new FeatureValueRecord(first.Id, "Checkout.Enabled", "new", "Tenant", "t1");

        // the column rejects this value, so the batch fails after the insert and the first update already ran
        var failingUpdate = new FeatureValueRecord(
            second.Id,
            "Reports.Enabled",
            new string('x', FeatureValueRecordConstants.ValueMaxLength + 1),
            "Tenant",
            "t1"
        );

        // when
        var act = async () => await repository.SaveAsync([added], [validUpdate, failingUpdate], [], AbortToken);

        // then
        await act.Should().ThrowAsync<DbUpdateException>();
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("Checkout.Enabled", "old"), ("Reports.Enabled", "old")]);
    }
}
