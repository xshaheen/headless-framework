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
/// Processors execute in registration order: the first call to <see cref="AddSaveEntryProcessor{TProcessor}"/>
/// runs first, the last call runs last. Defaults provided by <c>CreateDefault</c> are registered before any
/// consumer-supplied processors, so consumer registrations always run after the built-in chain. Registering
/// the same processor type more than once removes the prior registration and appends the new one to the
/// tail of the chain (so the latest call wins and dictates position).
/// </remarks>
public sealed class HeadlessDbContextOptions
{
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
    /// If <typeparamref name="TProcessor"/> is already registered, the prior entry is removed first and the
    /// new one is appended to the end of the chain — effectively re-prioritizing it to run last among the
    /// currently registered processors.
    /// </remarks>
    public HeadlessDbContextOptions AddSaveEntryProcessor<TProcessor>(ServiceLifetime lifetime)
        where TProcessor : class, IHeadlessSaveEntryProcessor
    {
        RemoveSaveEntryProcessor<TProcessor>();
        _saveEntryProcessors.Add(new(typeof(TProcessor), lifetime));

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

    private readonly record struct SaveEntryProcessorRegistration(Type ImplementationType, ServiceLifetime Lifetime);
}
