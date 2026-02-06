// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Collects <see cref="ScheduledJobDefinition"/> instances discovered during consumer registration.
/// Registered as a singleton and consumed by <see cref="SchedulerJobReconciler"/> at startup.
/// </summary>
internal sealed class ScheduledJobDefinitionRegistry
{
    private readonly List<ScheduledJobDefinition> _definitions = [];

    /// <summary>
    /// Adds a job definition to the registry.
    /// </summary>
    public void Add(ScheduledJobDefinition definition) => _definitions.Add(definition);

    /// <summary>
    /// Returns all collected job definitions as a read-only list.
    /// </summary>
    public IReadOnlyList<ScheduledJobDefinition> GetAll() => _definitions.AsReadOnly();
}
