// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.Sequences;

/// <summary>The object kind the Sequences module shows, and how a counter reads as the shell's object.</summary>
internal static class SequencesObjects
{
    /// <summary>One stored counter, by the id <see cref="SequenceCounterId"/> formats.</summary>
    public const string CounterKind = "sequence-counter";

    public static ObjectRef Ref(string id) =>
        new()
        {
            Module = SequencesDashboardOptions.ModuleId,
            Kind = CounterKind,
            Id = id,
        };

    /// <summary>The counter as the shell's object search lists it: name, scope, and partition, current or closed.</summary>
    public static DashboardObject From(SequenceCounter counter) =>
        new()
        {
            Target = Ref(SequenceCounterId.Format(counter.Key)),
            Title = string.Join(
                " · ",
                counter.Key.Name,
                counter.Key.TenantId.Length == 0 ? "Host" : counter.Key.TenantId,
                counter.Key.Partition.Length == 0 ? "No partition" : counter.Key.Partition
            ),
            Status = counter.IsCurrentPartition ? "current" : "closed",
        };
}
