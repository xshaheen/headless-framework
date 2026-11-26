// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>One capture store as the module reads and changes it, whatever kind of message it holds.</summary>
internal interface IDevInboxChannel
{
    /// <summary>The object kind of the channel's captures (<c>email</c>, <c>sms</c>, <c>push</c>).</summary>
    string Kind { get; }

    /// <summary>The sidebar count key the channel answers (<c>emails</c>, <c>sms</c>, <c>push</c>).</summary>
    string CountKey { get; }

    /// <summary>
    /// A random value made once per process. The stores restart their sequences at zero with the process, so an
    /// Operation mark names the instance it was read from and a mark from another process is refused.
    /// </summary>
    string InstanceId { get; }

    int Capacity { get; }

    int Count { get; }

    long Version { get; }

    bool Delete(Guid id);

    /// <summary>Every capture's id and sequence, newest first.</summary>
    IReadOnlyList<(Guid Id, long Sequence)> Snapshot();

    /// <summary>The captures whose sequence is after <paramref name="sequence"/>, newest first, as list rows.</summary>
    IReadOnlyList<(Guid Id, long Sequence, JsonElement Row)> RowsAfter(long sequence);

    /// <summary>The capture as an object the shell can link to, or <see langword="null"/> when it is gone.</summary>
    DashboardObject? Describe(string moduleId, Guid id);
}
