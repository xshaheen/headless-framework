// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Tests;

/// <summary>Records every operator-log entry the shell writes.</summary>
internal sealed class RecordingOperatorLog : IDashboardOperatorLog
{
    private readonly Lock _lock = new();
    private readonly List<OperatorLogEntry> _entries = [];

    public IReadOnlyList<OperatorLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return ValueTask.CompletedTask;
    }
}
