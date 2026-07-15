// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Fixtures;

public enum DispatchKind
{
    Local = 0,
    Distributed = 1,
}

public sealed record DispatchCall(int Index, DispatchKind Kind, object Payload);

/// <summary>
/// Test double recording both event tiers: implements <see cref="ILocalEventBus"/> (domain events,
/// in-transaction) and <see cref="IHeadlessOutboxDispatcher"/> (integration events, outbox) and captures
/// everything the save pipeline dispatches, preserving dispatch order via <see cref="Calls"/>.
/// </summary>
public sealed class RecordingHeadlessMessageDispatcher : ILocalEventBus, IHeadlessOutboxDispatcher
{
    private int _callIndex;
    private readonly List<DispatchCall> _calls = [];

    public List<IIntegrationEvent> EmittedDistributedMessages { get; } = [];

    public List<IDomainEvent> EmittedLocalMessages { get; } = [];

    public IReadOnlyList<DispatchCall> Calls => _calls;

    public int NextIndex()
    {
        return Interlocked.Increment(ref _callIndex);
    }

    public void RecordExternal(DispatchKind kind, object payload)
    {
        _calls.Add(new DispatchCall(NextIndex(), kind, payload));
    }

    public void Publish<T>(T domainEvent)
        where T : class, IDomainEvent
    {
        _RecordLocal(domainEvent);
    }

    public void Publish(IDomainEvent domainEvent)
    {
        _RecordLocal(domainEvent);
    }

    public ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
        where T : class, IDomainEvent
    {
        _RecordLocal(domainEvent);
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _RecordLocal(domainEvent);
        return ValueTask.CompletedTask;
    }

    public Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default
    )
    {
        _RecordDistributed(integrationEvents);
        return Task.CompletedTask;
    }

    public void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents)
    {
        _RecordDistributed(integrationEvents);
    }

    private void _RecordLocal(IDomainEvent domainEvent)
    {
        EmittedLocalMessages.Add(domainEvent);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Local, domainEvent));
    }

    private void _RecordDistributed(IReadOnlyList<IIntegrationEvent> integrationEvents)
    {
        EmittedDistributedMessages.AddRange(integrationEvents);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Distributed, integrationEvents));
    }
}

public static class RecordingHeadlessDispatcherSetup
{
    /// <summary>
    /// Registers a single <see cref="RecordingHeadlessMessageDispatcher"/> instance as both the
    /// <see cref="ILocalEventBus"/> and the <see cref="IHeadlessOutboxDispatcher"/> for the current scope.
    /// </summary>
    public static IServiceCollection AddRecordingHeadlessDispatcher(this IServiceCollection services)
    {
        services.AddScoped<RecordingHeadlessMessageDispatcher>();
        services.AddScoped<ILocalEventBus>(sp => sp.GetRequiredService<RecordingHeadlessMessageDispatcher>());
        services.AddScoped<IHeadlessOutboxDispatcher>(sp =>
            sp.GetRequiredService<RecordingHeadlessMessageDispatcher>()
        );

        return services;
    }
}
