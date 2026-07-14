// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Resource names for composite-acquisition conformance. Shared by the mutex, reader-writer, and semaphore suites,
/// which are sibling bases with no common ancestor to hang this on.
/// </summary>
public static class CompositeTestResources
{
    /// <summary>
    /// Creates two resource names that are already in canonical (ordinal) order, so a test can name the ordinally
    /// earlier and later resource without hard-coding the sort. The GUID prefix keeps each test's pair disjoint from
    /// every other test's, which is what lets these run against a shared backend in parallel.
    /// </summary>
    public static (string First, string Second) CreatePair()
    {
        var prefix = $"composite:{Guid.NewGuid():N}";

        return ($"{prefix}:a", $"{prefix}:b");
    }
}
