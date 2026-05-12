// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Configures the save-time pipeline used by <see cref="HeadlessDbContext"/>. The primary knob is the
/// ordered chain of <see cref="IHeadlessSaveEntryProcessor"/> implementations that runs against every
/// tracked <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry"/> before <c>SaveChanges</c>
/// is dispatched to the database.
/// </summary>
/// <remarks>
/// Processors execute in registration order. The entity and audit defaults run before consumer-supplied
/// processors. The lifecycle event and message collector defaults stay terminal so custom processors can
/// enrich entries or queue messages before the framework collects and publishes them. Registering the same
/// processor type more than once removes the prior registration and inserts the new one at its effective
/// priority.
/// </remarks>
public sealed class HeadlessDbContextOptions
{
    private static readonly Type[] _TerminalProcessorOrder =
    [
        typeof(HeadlessLocalEventSaveEntryProcessor),
        typeof(HeadlessMessageCollectorSaveEntryProcessor),
    ];

    private readonly List<SaveEntryProcessorRegistration> _saveEntryProcessors = [];

    public HeadlessDbContextOptions()
    {
        AddSaveEntryProcessor<HeadlessEntitySaveEntryProcessor>(ServiceLifetime.Singleton);
        AddSaveEntryProcessor<HeadlessAuditSaveEntryProcessor>(ServiceLifetime.Singleton);
        AddSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>(ServiceLifetime.Singleton);
        AddSaveEntryProcessor<HeadlessMessageCollectorSaveEntryProcessor>(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// Registers a save-entry processor. The processor is resolved from the request <see cref="IServiceProvider"/>
    /// once per <c>SaveChanges</c> call and invoked once per tracked entity, in the order processors were added.
    /// </summary>
    /// <typeparam name="TProcessor">Processor implementation to register.</typeparam>
    /// <param name="lifetime">DI lifetime used when registering and resolving the processor.</param>
    /// <returns>The same options instance to support chaining.</returns>
    /// <remarks>
    /// If <typeparamref name="TProcessor"/> is already registered, the prior entry is removed first. Normal
    /// processors are inserted before the terminal lifecycle and message collector processors. Terminal
    /// processors keep their relative framework order.
    /// </remarks>
    public HeadlessDbContextOptions AddSaveEntryProcessor<TProcessor>(ServiceLifetime lifetime)
        where TProcessor : class, IHeadlessSaveEntryProcessor
    {
        RemoveSaveEntryProcessor<TProcessor>();
        _InsertSaveEntryProcessor(new(typeof(TProcessor), lifetime));

        return this;
    }

    /// <summary>
    /// Removes any registration of <typeparamref name="TProcessor"/> from the processor chain.
    /// </summary>
    /// <typeparam name="TProcessor">Processor implementation to remove.</typeparam>
    /// <returns><see langword="true"/> if a registration was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveSaveEntryProcessor<TProcessor>()
        where TProcessor : class, IHeadlessSaveEntryProcessor
    {
        return _saveEntryProcessors.RemoveAll(x => x.ImplementationType == typeof(TProcessor)) > 0;
    }

    internal void RegisterServices(IServiceCollection services)
    {
        foreach (var registration in _saveEntryProcessors)
        {
            services.TryAdd(
                ServiceDescriptor.Describe(
                    registration.ImplementationType,
                    registration.ImplementationType,
                    registration.Lifetime
                )
            );
        }
    }

    internal IReadOnlyList<IHeadlessSaveEntryProcessor> ResolveSaveEntryProcessors(IServiceProvider serviceProvider)
    {
        return _saveEntryProcessors
            .Select(x => (IHeadlessSaveEntryProcessor)serviceProvider.GetRequiredService(x.ImplementationType))
            .ToArray();
    }

    private void _InsertSaveEntryProcessor(SaveEntryProcessorRegistration registration)
    {
        var terminalOrder = _GetTerminalOrder(registration.ImplementationType);

        if (terminalOrder is null)
        {
            _InsertBeforeTerminalProcessors(registration);
            return;
        }

        _InsertTerminalProcessor(registration, terminalOrder.Value);
    }

    private void _InsertBeforeTerminalProcessors(SaveEntryProcessorRegistration registration)
    {
        var firstTerminalIndex = _saveEntryProcessors.FindIndex(x =>
            _GetTerminalOrder(x.ImplementationType) is not null
        );

        if (firstTerminalIndex < 0)
        {
            _saveEntryProcessors.Add(registration);
            return;
        }

        _saveEntryProcessors.Insert(firstTerminalIndex, registration);
    }

    private void _InsertTerminalProcessor(SaveEntryProcessorRegistration registration, int terminalOrder)
    {
        var nextTerminalIndex = _saveEntryProcessors.FindIndex(x =>
        {
            var existingOrder = _GetTerminalOrder(x.ImplementationType);

            return existingOrder is not null && existingOrder > terminalOrder;
        });

        if (nextTerminalIndex < 0)
        {
            _saveEntryProcessors.Add(registration);
            return;
        }

        _saveEntryProcessors.Insert(nextTerminalIndex, registration);
    }

    private static int? _GetTerminalOrder(Type implementationType)
    {
        for (var i = 0; i < _TerminalProcessorOrder.Length; i++)
        {
            if (_TerminalProcessorOrder[i] == implementationType)
            {
                return i;
            }
        }

        return null;
    }

    private readonly record struct SaveEntryProcessorRegistration(Type ImplementationType, ServiceLifetime Lifetime);
}
