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
/// Test double recording both event tiers: implements <see cref="IDomainEventDispatcher"/> (domain events,
/// in-transaction) and <see cref="IHeadlessOutboxDispatcher"/> (integration events, outbox) and captures
/// everything the save pipeline dispatches, preserving dispatch order via <see cref="Calls"/>.
/// </summary>
public sealed class RecordingHeadlessMessageDispatcher : IDomainEventDispatcher, IHeadlessOutboxDispatcher
{
    private int _callIndex;
    private readonly List<DispatchCall> _calls = [];

    public List<object> EmittedDistributedMessages { get; } = [];

    public List<object> EmittedLocalMessages { get; } = [];

    public IReadOnlyList<DispatchCall> Calls => _calls;

    public int NextIndex()
    {
        return Interlocked.Increment(ref _callIndex);
    }

    public void RecordExternal(DispatchKind kind, object payload)
    {
        _calls.Add(new DispatchCall(NextIndex(), kind, payload));
    }

    public ValueTask DispatchAsync<TPayload>(
        EventContext<TPayload> context,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        _RecordLocal(context.Payload);
        return ValueTask.CompletedTask;
    }

    public Task DispatchAsync(
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    )
    {
        _RecordDistributed(integrationEvents);
        return Task.CompletedTask;
    }

    public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
    {
        _RecordDistributed(integrationEvents);
    }

    private void _RecordLocal(object domainEvent)
    {
        EmittedLocalMessages.Add(domainEvent);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Local, domainEvent));
    }

    private void _RecordDistributed(IReadOnlyList<EventContext<object>> integrationEvents)
    {
        var payloads = integrationEvents.Select(occurrence => occurrence.Payload).ToArray();
        EmittedDistributedMessages.AddRange(payloads);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Distributed, payloads));
    }
}

public static class RecordingHeadlessDispatcherSetup
{
    /// <summary>
    /// Registers a single <see cref="RecordingHeadlessMessageDispatcher"/> instance as both the
    /// <see cref="IDomainEventDispatcher"/> and the <see cref="IHeadlessOutboxDispatcher"/> for the current scope.
    /// </summary>
    public static IServiceCollection AddRecordingHeadlessDispatcher(this IServiceCollection services)
    {
        services.AddScoped<RecordingHeadlessMessageDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp => sp.GetRequiredService<RecordingHeadlessMessageDispatcher>());
        services.AddScoped<IHeadlessOutboxDispatcher>(sp =>
            sp.GetRequiredService<RecordingHeadlessMessageDispatcher>()
        );

        return services;
    }
}
