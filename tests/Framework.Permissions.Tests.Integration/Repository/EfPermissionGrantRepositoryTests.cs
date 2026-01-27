// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Domain;
using Framework.Permissions.Entities;
using Framework.Permissions.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Repository;

[Collection<PermissionsTestFixture>]
public sealed class EfPermissionGrantRepositoryTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "Admin";

    [Fact]
    public async Task should_find_grant_by_name_provider_key()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant = new PermissionGrantRecord(
            id: guidGenerator.Create(),
            name: "Permission1",
            providerName: _ProviderName,
            providerKey: _ProviderKey,
            isGranted: true
        );
        await repository.InsertAsync(grant, AbortToken);

        // when
        var result = await repository.FindAsync("Permission1", _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Id.Should().Be(grant.Id);
        result.Name.Should().Be("Permission1");
        result.ProviderName.Should().Be(_ProviderName);
        result.ProviderKey.Should().Be(_ProviderKey);
        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_null_when_grant_not_found()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();

        // when
        var result = await repository.FindAsync("NonExistent", _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_get_list_by_provider_and_key()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant1 = new PermissionGrantRecord(guidGenerator.Create(), "Perm1", _ProviderName, _ProviderKey, true);
        var grant2 = new PermissionGrantRecord(guidGenerator.Create(), "Perm2", _ProviderName, _ProviderKey, false);
        var grant3 = new PermissionGrantRecord(guidGenerator.Create(), "Perm3", "OtherProvider", _ProviderKey, true);

        await repository.InsertManyAsync([grant1, grant2, grant3], AbortToken);

        // when
        var result = await repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(g => g.Name == "Perm1");
        result.Should().Contain(g => g.Name == "Perm2");
        result.Should().NotContain(g => g.Name == "Perm3");
    }

    [Fact]
    public async Task should_get_list_by_names_provider_key()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant1 = new PermissionGrantRecord(guidGenerator.Create(), "Perm1", _ProviderName, _ProviderKey, true);
        var grant2 = new PermissionGrantRecord(guidGenerator.Create(), "Perm2", _ProviderName, _ProviderKey, true);
        var grant3 = new PermissionGrantRecord(guidGenerator.Create(), "Perm3", _ProviderName, _ProviderKey, true);

        await repository.InsertManyAsync([grant1, grant2, grant3], AbortToken);

        // when
        var result = await repository.GetListAsync(["Perm1", "Perm3"], _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(g => g.Name == "Perm1");
        result.Should().Contain(g => g.Name == "Perm3");
        result.Should().NotContain(g => g.Name == "Perm2");
    }

    [Fact]
    public async Task should_insert_new_grant_record()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant = new PermissionGrantRecord(
            id: guidGenerator.Create(),
            name: "NewPermission",
            providerName: _ProviderName,
            providerKey: _ProviderKey,
            isGranted: true
        );

        // when
        await repository.InsertAsync(grant, AbortToken);

        // then
        var inserted = await repository.FindAsync("NewPermission", _ProviderName, _ProviderKey, AbortToken);
        inserted.Should().NotBeNull();
        inserted!.Id.Should().Be(grant.Id);
    }

    [Fact]
    public async Task should_insert_many_grant_records()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grants = new[]
        {
            new PermissionGrantRecord(guidGenerator.Create(), "BatchPerm1", _ProviderName, _ProviderKey, true),
            new PermissionGrantRecord(guidGenerator.Create(), "BatchPerm2", _ProviderName, _ProviderKey, false),
            new PermissionGrantRecord(guidGenerator.Create(), "BatchPerm3", _ProviderName, _ProviderKey, true),
        };

        // when
        await repository.InsertManyAsync(grants, AbortToken);

        // then
        var inserted = await repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken);
        inserted.Should().HaveCount(3);
        inserted.Should().Contain(g => g.Name == "BatchPerm1" && g.IsGranted);
        inserted.Should().Contain(g => g.Name == "BatchPerm2" && !g.IsGranted);
        inserted.Should().Contain(g => g.Name == "BatchPerm3" && g.IsGranted);
    }

    [Fact]
    public async Task should_delete_grant_record()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant = new PermissionGrantRecord(guidGenerator.Create(), "ToDelete", _ProviderName, _ProviderKey, true);
        await repository.InsertAsync(grant, AbortToken);

        var beforeDelete = await repository.FindAsync("ToDelete", _ProviderName, _ProviderKey, AbortToken);
        beforeDelete.Should().NotBeNull();

        // when
        await repository.DeleteAsync(grant, AbortToken);

        // then
        var afterDelete = await repository.FindAsync("ToDelete", _ProviderName, _ProviderKey, AbortToken);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task should_publish_entity_changed_on_delete()
    {
        // given
        await Fixture.ResetAsync();
        var localPublisher = Substitute.For<ILocalMessagePublisher>();

        using var host = CreateHost(b => b.Services.AddSingleton(localPublisher));
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant = new PermissionGrantRecord(
            guidGenerator.Create(),
            "ToDeleteWithEvent",
            _ProviderName,
            _ProviderKey,
            true
        );
        await repository.InsertAsync(grant, AbortToken);

        // when
        await repository.DeleteAsync(grant, AbortToken);

        // then
        await localPublisher
            .Received(1)
            .PublishAsync(
                Arg.Is<EntityChangedEventData<PermissionGrantRecord>>(e => e.Entity.Id == grant.Id),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_delete_many_grant_records()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant1 = new PermissionGrantRecord(
            guidGenerator.Create(),
            "DeleteMany1",
            _ProviderName,
            _ProviderKey,
            true
        );
        var grant2 = new PermissionGrantRecord(
            guidGenerator.Create(),
            "DeleteMany2",
            _ProviderName,
            _ProviderKey,
            true
        );
        var grant3 = new PermissionGrantRecord(guidGenerator.Create(), "KeepThis", _ProviderName, _ProviderKey, true);

        await repository.InsertManyAsync([grant1, grant2, grant3], AbortToken);

        // when
        await repository.DeleteManyAsync([grant1, grant2], AbortToken);

        // then
        var remaining = await repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken);
        remaining.Should().ContainSingle();
        remaining[0].Name.Should().Be("KeepThis");
    }

    [Fact]
    public async Task should_publish_entity_changed_for_each_deleted()
    {
        // given
        await Fixture.ResetAsync();
        var localPublisher = Substitute.For<ILocalMessagePublisher>();

        using var host = CreateHost(b => b.Services.AddSingleton(localPublisher));
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var guidGenerator = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var grant1 = new PermissionGrantRecord(guidGenerator.Create(), "Multi1", _ProviderName, _ProviderKey, true);
        var grant2 = new PermissionGrantRecord(guidGenerator.Create(), "Multi2", _ProviderName, _ProviderKey, true);
        var grant3 = new PermissionGrantRecord(guidGenerator.Create(), "Multi3", _ProviderName, _ProviderKey, true);

        await repository.InsertManyAsync([grant1, grant2, grant3], AbortToken);

        // when
        await repository.DeleteManyAsync([grant1, grant2, grant3], AbortToken);

        // then
        await localPublisher
            .Received(3)
            .PublishAsync(Arg.Any<EntityChangedEventData<PermissionGrantRecord>>(), Arg.Any<CancellationToken>());
    }
}
