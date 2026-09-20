// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// The registered <see cref="IBus"/> / <see cref="IQueue"/> facades are autonomous singletons: they publish
/// without reading the calling scope's unit of work, so a framework singleton can depend on them and a publish
/// inside an active unit is written standalone instead of joining that unit's transaction.
/// </summary>
public sealed class PublisherFacadeLifetimeTests : TestBase
{
    [Fact]
    public async Task should_resolve_both_facades_from_the_root_provider()
    {
        await using var provider = _BuildProvider();

        provider.GetRequiredService<IBus>().Should().NotBeNull();
        provider.GetRequiredService<IQueue>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_share_one_facade_instance_across_scopes()
    {
        await using var provider = _BuildProvider();

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        first
            .ServiceProvider.GetRequiredService<IBus>()
            .Should()
            .BeSameAs(second.ServiceProvider.GetRequiredService<IBus>());
        first
            .ServiceProvider.GetRequiredService<IQueue>()
            .Should()
            .BeSameAs(second.ServiceProvider.GetRequiredService<IQueue>());
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_write_a_standalone_durable_row_when_publishing_inside_an_active_unit_of_work(
        MessageLane lane
    )
    {
        // given — in-memory storage joins a resource-less unit of work, so anything that read the scope's
        // active unit would capture the row and hide it until that unit completes.
        await using var provider = _BuildProvider();
        var monitoring = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        await using var scope = provider.CreateAsyncScope();
        await using var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var receipt =
            lane is MessageLane.Bus
                ? await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(new Placed("value"), AbortToken)
                : await scope
                    .ServiceProvider.GetRequiredService<IQueue>()
                    .EnqueueAsync(new Placed("value"), AbortToken);

        // then — the row is durable and visible immediately, before the unit is resolved
        receipt.StorageId.Should().NotBeNull();
        (await monitoring.GetPublishedMessageAsync(receipt.StorageId!.Value, AbortToken)).Should().NotBeNull();

        // then — and it survives the unit's rollback, which no enlisted row would
        await unitOfWork.RollbackAsync();
        (await monitoring.GetPublishedMessageAsync(receipt.StorageId.Value, AbortToken)).Should().NotBeNull();
    }

    private static ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<Placed>(message => message.Contract("tests.facade-lifetime.placed"));
            setup.Queue.ForMessage<Placed>(message => message.Contract("tests.facade-lifetime.placed"));
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed record Placed(string Value);
}
